using System;
using System.Threading;
using System.Threading.Tasks;
using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Windows.Brightness;

public sealed class LaptopAdaptiveBrightnessController : IDisposable, IAsyncDisposable
{
    private static readonly (double Lux, int Level)[] BrightnessCurve =
    [
        (0, 18), (5, 22), (20, 30), (80, 40), (250, 52),
        (800, 65), (2_500, 78), (8_000, 90), (20_000, 100)
    ];

    private readonly object _gate = new();
    private readonly WmiDisplayBrightnessProvider? _provider;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Task _worker;
    private bool _available;
    private bool _enabled;
    private bool _connected;
    private bool _overrideActive;
    private AmbientLightStatus _ambientStatus = AmbientLightStatus.Unavailable;
    private double? _smoothedLux;
    private int? _targetLevel;
    private int? _appliedLevel;
    private DateTimeOffset _lastTargetChange = DateTimeOffset.MinValue;
    private string _status;
    private int _disposeStarted;

    private LaptopAdaptiveBrightnessController(WmiDisplayBrightnessProvider? provider)
    {
        _provider = provider;
        _available = provider is not null;
        _status = provider is null ? "Laptop brightness control unavailable" : "Off";
        _worker = Task.Run(RunAsync);
    }

    public static Task<LaptopAdaptiveBrightnessController> CreateAsync() => Task.Run(() =>
        new LaptopAdaptiveBrightnessController(WmiDisplayBrightnessProvider.TryCreate()));

    public event Action? Changed;

    public bool IsSupported { get { lock (_gate) return _available; } }
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public int? CurrentLevel { get { lock (_gate) return _appliedLevel; } }
    public string Status { get { lock (_gate) return _status; } }

    public void SetEnabled(bool enabled)
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;
        lock (_gate)
        {
            if (!_available || _enabled == enabled) return;
            _enabled = enabled;
            _targetLevel = enabled && _smoothedLux is double lux ? MapLuxToLevel(lux) : null;
            _lastTargetChange = DateTimeOffset.UtcNow;
            _status = enabled ? DescribeInputLocked() : "Off";
        }

        Changed?.Invoke();
        Wake();
    }

    public void Update(PhoneState state)
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;
        lock (_gate)
        {
            _connected = state.Connection == ConnectionState.Connected && !state.IsDemo;
            var brightness = state.Brightness;
            _ambientStatus = brightness?.AmbientStatus ?? AmbientLightStatus.Unavailable;

            if (_connected && brightness is { AmbientStatus: AmbientLightStatus.Valid, AmbientLux: double rawLux })
            {
                var lux = Math.Clamp(rawLux, 0, 100_000);
                _smoothedLux = _smoothedLux is double previous ? previous * 0.75 + lux * 0.25 : lux;
                var candidate = MapLuxToLevel(_smoothedLux.Value);
                var now = DateTimeOffset.UtcNow;
                if (_targetLevel is null ||
                    (Math.Abs(candidate - _targetLevel.Value) >= 3 && now - _lastTargetChange >= TimeSpan.FromSeconds(1.5)))
                {
                    _targetLevel = candidate;
                    _lastTargetChange = now;
                }
            }

            if (_enabled) _status = DescribeInputLocked();
        }

        Changed?.Invoke();
        Wake();
    }

    private async Task RunAsync()
    {
        if (_provider is null) return;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(500), _lifetime.Token); }
                catch (OperationCanceledException) { break; }

                bool enabled;
                bool connected;
                AmbientLightStatus ambientStatus;
                int? target;
                int? applied;
                lock (_gate)
                {
                    enabled = _enabled;
                    connected = _connected;
                    ambientStatus = _ambientStatus;
                    target = _targetLevel;
                    applied = _appliedLevel;
                }

                if (!enabled || !connected)
                {
                    ReleaseOverride();
                    continue;
                }

                if (ambientStatus != AmbientLightStatus.Valid || target is null) continue;

                try
                {
                    var current = applied ?? _provider.ReadCurrentLevel() ?? target.Value;
                    var difference = target.Value - current;
                    if (difference == 0) continue;
                    var next = current + Math.Clamp(difference, -3, 3);
                    var actual = _provider.SetLevel(next);
                    lock (_gate)
                    {
                        _overrideActive = true;
                        _appliedLevel = actual;
                        _status = DescribeInputLocked();
                    }
                    Changed?.Invoke();
                }
                catch
                {
                    lock (_gate)
                    {
                        _available = false;
                        _enabled = false;
                        _status = "Laptop brightness control failed";
                    }
                    Changed?.Invoke();
                    ReleaseOverride();
                }
            }
        }
        finally
        {
            ReleaseOverride();
            _provider.Dispose();
        }
    }

    private void ReleaseOverride()
    {
        bool shouldRelease;
        lock (_gate)
        {
            shouldRelease = _overrideActive;
            _overrideActive = false;
            _appliedLevel = null;
        }
        if (!shouldRelease || _provider is null) return;
        try { _provider.RestorePolicy(); } catch { }
        Changed?.Invoke();
    }

    private string DescribeInputLocked()
    {
        if (!_connected) return "Waiting for phone";
        if (_ambientStatus == AmbientLightStatus.Covered)
            return _appliedLevel is int coveredLevel ? $"Phone covered · holding {coveredLevel}%" : "Phone covered · waiting";
        if (_ambientStatus != AmbientLightStatus.Valid || _smoothedLux is not double lux)
            return _appliedLevel is int heldLevel ? $"Light unavailable · holding {heldLevel}%" : "Waiting for ambient light";
        var level = _appliedLevel ?? _targetLevel;
        return level is int value ? $"{value}% · {lux:0.#} lux" : $"{lux:0.#} lux";
    }

    private static int MapLuxToLevel(double lux)
    {
        var clamped = Math.Max(0, lux);
        for (var index = 1; index < BrightnessCurve.Length; index++)
        {
            var upper = BrightnessCurve[index];
            if (clamped > upper.Lux) continue;
            var lower = BrightnessCurve[index - 1];
            var lowLog = Math.Log10(lower.Lux + 1);
            var highLog = Math.Log10(upper.Lux + 1);
            var position = (Math.Log10(clamped + 1) - lowLog) / (highLog - lowLog);
            return (int)Math.Round(lower.Level + (upper.Level - lower.Level) * position);
        }
        return BrightnessCurve[^1].Level;
    }

    private void Wake()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        _lifetime.Cancel();
        Wake();
        try { await _worker; } catch { }
        _wake.Dispose();
        _lifetime.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
