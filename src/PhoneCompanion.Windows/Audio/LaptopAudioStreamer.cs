using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows.Audio;

public sealed class LaptopAudioStreamer : IAsyncDisposable, IDisposable
{
    private const int TagSize = 16;
    private readonly PhoneStateManager _manager;
    private readonly IdentityAndTrustStore _trustStore;
    private readonly object _sync = new();
    private CancellationTokenSource? _lifetime;
    private Task? _run;
    private Guid? _streamId;
    private string _status = "Laptop audio is off";
    private bool _disposed;

    public LaptopAudioStreamer(PhoneStateManager manager, IdentityAndTrustStore trustStore)
    {
        _manager = manager;
        _trustStore = trustStore;
        _manager.AudioStreamStopReceived += OnRemoteStop;
        _manager.StateChanged += OnPhoneStateChanged;
    }

    public event Action? Changed;
    public bool IsStreaming { get { lock (_sync) return _run is not null; } }
    public string Status { get { lock (_sync) return _status; } }

    public async Task<string?> StartAsync()
    {
        TaskCompletionSource<string?> started;
        lock (_sync)
        {
            if (_disposed) return "Audio streaming is unavailable.";
            if (_run is not null) return null;
            _status = "Starting laptop audio…";
            _streamId = null;
            _lifetime = new CancellationTokenSource();
            started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _run = RunAsync(started, _lifetime);
        }
        Changed?.Invoke();
        return await started.Task.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Task? run;
        lock (_sync)
        {
            _lifetime?.Cancel();
            run = _run;
        }
        if (run is not null)
        {
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync(TaskCompletionSource<string?> started, CancellationTokenSource lifetime)
    {
        var streamId = Guid.NewGuid();
        var key = RandomNumberGenerator.GetBytes(32);
        var token = RandomNumberGenerator.GetBytes(16);
        WasapiRecorder? capture = null;
        TcpClient? client = null;
        var commandSent = false;
        try
        {
            lock (_sync) _streamId = streamId;
            capture = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .WithBufferLength(50)
                .Build();
            var format = capture.WaveFormat;
            if (format.SampleRate is < 8000 or > 96000 || format.Channels is < 1 or > 2)
                throw new InvalidOperationException("The current Windows output format must be mono or stereo at 8–96 kHz.");

            var buffered = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(500))
            {
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };
            capture.DataAvailable += (buffer, _, _, _) => buffered.AddSamples(buffer);
            var samples = buffered.ToSampleProvider();
            var ready = new TaskCompletionSource<AudioSinkReadyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(AudioSinkReadyMessage message)
            {
                if (message.StreamId == streamId) ready.TrySetResult(message);
            }
            _manager.AudioSinkReadyReceived += Ready;
            try
            {
                var result = await _manager.StartAudioStreamAsync(new AudioStreamStartCommandMessage(
                    streamId, key, token, format.SampleRate, format.Channels), lifetime.Token).ConfigureAwait(false);
                if (result != CommandResult.Sent)
                    throw new InvalidOperationException("The phone did not accept the audio request.");
                commandSent = true;

                using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                AudioSinkReadyMessage sink;
                try { sink = await ready.Task.WaitAsync(readyTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    throw new InvalidOperationException("The phone did not open its audio receiver.");
                }

                client = await ConnectToPhoneAsync(sink.Port, lifetime.Token).ConfigureAwait(false);
                await client.GetStream().WriteAsync(token, lifetime.Token).ConfigureAwait(false);
                capture.StartRecording();
                SetStatus("Streaming laptop audio to phone");
                started.TrySetResult(null);
                await PumpAsync(samples, format.SampleRate, format.Channels, client.GetStream(), streamId, key,
                    lifetime.Token).ConfigureAwait(false);
            }
            finally { _manager.AudioSinkReadyReceived -= Ready; }
        }
        catch (OperationCanceledException)
        {
            started.TrySetResult("Laptop audio stopped.");
        }
        catch (Exception error)
        {
            started.TrySetResult(error.Message);
            SetStatus("Laptop audio could not be streamed");
        }
        finally
        {
            try { capture?.StopRecording(); } catch { }
            capture?.Dispose();
            client?.Dispose();
            if (commandSent)
            {
                try { await _manager.StopAudioStreamAsync(streamId).ConfigureAwait(false); } catch { }
            }
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(token);
            lock (_sync)
            {
                if (ReferenceEquals(_lifetime, lifetime))
                {
                    _lifetime.Dispose();
                    _lifetime = null;
                    _run = null;
                    _streamId = null;
                    if (_status == "Streaming laptop audio to phone" || lifetime.IsCancellationRequested)
                        _status = "Laptop audio is off";
                }
            }
            Changed?.Invoke();
        }
    }

    private async Task<TcpClient> ConnectToPhoneAsync(int port, CancellationToken cancellationToken)
    {
        var hosts = new List<string>();
        AddHost(hosts, _trustStore.Load()?.WifiEndpoint);
        try
        {
            foreach (var phone in await MdnsPhoneDiscovery.FindAsync(cancellationToken).ConfigureAwait(false))
                AddHost(hosts, phone.Endpoint);
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        if (hosts.Count == 0)
            throw new InvalidOperationException("Connect the phone and laptop to the same Wi-Fi network.");
        foreach (var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                await candidate.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                return candidate;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                candidate.Dispose();
                throw;
            }
            catch { candidate.Dispose(); }
        }
        throw new InvalidOperationException("The phone audio receiver could not be reached over Wi-Fi.");
    }

    private static void AddHost(List<string> hosts, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        if (endpoint[0] == '[')
        {
            var close = endpoint.IndexOf(']');
            if (close > 1) hosts.Add(endpoint[1..close]);
            return;
        }
        var separator = endpoint.LastIndexOf(':');
        hosts.Add(separator > 0 ? endpoint[..separator] : endpoint);
    }

    private static async Task PumpAsync(ISampleProvider samples, int sampleRate, int channels, NetworkStream output,
        Guid streamId, byte[] key, CancellationToken cancellationToken)
    {
        var floats = new float[Math.Max(1, sampleRate / 50) * channels];
        var plaintext = new byte[floats.Length * sizeof(short)];
        var streamBytes = Encoding.UTF8.GetBytes(streamId.ToString("D").ToLowerInvariant());
        var sequence = 0UL;
        using var aes = new AesGcm(key, TagSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = samples.Read(floats);
            count -= count % channels;
            if (count == 0)
            {
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                continue;
            }
            var pcm = plaintext.AsSpan(0, count * sizeof(short));
            for (var index = 0; index < count; index++)
            {
                var value = (short)Math.Clamp((int)Math.Round(floats[index] * short.MaxValue), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(pcm[(index * 2)..], value);
            }

            var sequenceBytes = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(sequenceBytes, sequence);
            var nonce = new byte[12];
            sequenceBytes.CopyTo(nonce, 4);
            var aad = new byte[streamBytes.Length + sequenceBytes.Length];
            streamBytes.CopyTo(aad, 0);
            sequenceBytes.CopyTo(aad, streamBytes.Length);
            var ciphertext = new byte[pcm.Length];
            var tag = new byte[TagSize];
            aes.Encrypt(nonce, pcm, ciphertext, tag, aad);

            var length = sequenceBytes.Length + ciphertext.Length + tag.Length;
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, length);
            await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(sequenceBytes, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            sequence++;
        }
    }

    private void OnRemoteStop(Guid streamId)
    {
        lock (_sync)
        {
            if (_run is null || _streamId != streamId) return;
            _lifetime?.Cancel();
        }
    }

    private void OnPhoneStateChanged(PhoneCompanion.Core.Models.PhoneState state)
    {
        if (state.Connection != ConnectionState.Connected || state.IsDemo)
        {
            lock (_sync) _lifetime?.Cancel();
        }
    }

    private void SetStatus(string status)
    {
        lock (_sync) _status = status;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.AudioStreamStopReceived -= OnRemoteStop;
        _manager.StateChanged -= OnPhoneStateChanged;
        await StopAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.AudioStreamStopReceived -= OnRemoteStop;
        _manager.StateChanged -= OnPhoneStateChanged;
        lock (_sync) _lifetime?.Cancel();
    }
}
