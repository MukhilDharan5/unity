using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PhoneCompanion.Core.Transports;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace PhoneCompanion.Windows.Connection;

public static class ConnectionFactories
{
    public static async Task<IFrameConnection> OpenWifiAsync(string endpoint, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Enter the Wi-Fi address shown on your phone.");
        var separator = endpoint.LastIndexOf(':');
        if (separator < 1 || !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("Use the address shown on your phone, including the port.");
        var host = endpoint[..separator].Trim('[', ']');
        var client = new TcpClient { NoDelay = true };
        try { await client.ConnectAsync(host, port, token); return new TcpFrameConnection(client); }
        catch { client.Dispose(); throw; }
    }

    public static async Task<IFrameConnection> OpenBleAsync(Action<string>? progress, CancellationToken token)
    {
        var serviceId = Guid.Parse("8ec8b6c0-eb89-4b2a-881b-a5d5a7114b0b");
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => found.TrySetCanceled(token));
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(serviceId);
        watcher.Received += OnFound;
        try
        {
            progress?.Invoke("Looking for your phone over Bluetooth…"); watcher.Start();
            var address = await found.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            watcher.Stop(); progress?.Invoke("Opening a secure Bluetooth connection…");
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(token)
                ?? throw new IOException("The phone stopped advertising.");
            var result = await device.GetGattServicesForUuidAsync(serviceId, BluetoothCacheMode.Uncached).AsTask(token);
            if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
            { device.Dispose(); throw new IOException("The phone's Bluetooth service is unavailable."); }
            try { return await BleFrameConnection.CreateAsync(device, result.Services[0], token); }
            catch { result.Services[0].Dispose(); device.Dispose(); throw; }
        }
        finally { watcher.Received -= OnFound; if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) watcher.Stop(); }
        void OnFound(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs e) => found.TrySetResult(e.BluetoothAddress);
    }
}

internal sealed class BleFrameConnection : IFrameConnection
{
    private static readonly Guid RxId = Guid.Parse("8ec8b6c1-eb89-4b2a-881b-a5d5a7114b0b");
    private static readonly Guid TxId = Guid.Parse("8ec8b6c2-eb89-4b2a-881b-a5d5a7114b0b");
    private readonly BluetoothLEDevice _device;
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _rx;
    private readonly GattCharacteristic _tx;
    private readonly GattSession _gattSession;
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(16);
    private readonly BleReassembler _reassembler = new();
    private readonly SemaphoreSlim _writes = new(1);
    private bool _disposed;
    private BleFrameConnection(BluetoothLEDevice device, GattDeviceService service, GattCharacteristic rx,
        GattCharacteristic tx, GattSession session)
    { _device = device; _service = service; _rx = rx; _tx = tx; _gattSession = session; }

    public static async Task<BleFrameConnection> CreateAsync(BluetoothLEDevice device, GattDeviceService service, CancellationToken token)
    {
        var rxResult = await service.GetCharacteristicsForUuidAsync(RxId, BluetoothCacheMode.Uncached).AsTask(token);
        var txResult = await service.GetCharacteristicsForUuidAsync(TxId, BluetoothCacheMode.Uncached).AsTask(token);
        if (rxResult.Status != GattCommunicationStatus.Success || txResult.Status != GattCommunicationStatus.Success ||
            rxResult.Characteristics.Count == 0 || txResult.Characteristics.Count == 0) throw new IOException("Bluetooth channels are unavailable.");
        var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(token)
            ?? throw new IOException("Bluetooth session is unavailable.");
        session.MaintainConnection = true;
        var connection = new BleFrameConnection(device, service, rxResult.Characteristics[0], txResult.Characteristics[0], session);
        connection._tx.ValueChanged += connection.OnValueChanged;
        var enabled = await connection._tx.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token);
        if (enabled != GattCommunicationStatus.Success) { await connection.DisposeAsync(); throw new IOException("Bluetooth notifications could not start."); }
        return connection;
    }
    private void OnValueChanged(GattCharacteristic _, GattValueChangedEventArgs e)
    {
        try
        {
            var bytes = e.CharacteristicValue.ToArray();
            var frame = _reassembler.Accept(bytes);
            if (frame is not null && !_frames.Writer.TryWrite(frame)) throw new IOException("Bluetooth receive queue is full.");
        }
        catch (Exception error) { _frames.Writer.TryComplete(error); }
    }
    public async ValueTask<byte[]> ReadAsync(CancellationToken token) => await _frames.Reader.ReadAsync(token);
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        await _writes.WaitAsync(token);
        try
        {
            foreach (var fragment in BleFraming.Fragment(frame.ToArray(), Math.Max(23, (int)_gattSession.MaxPduSize)))
            {
                using var writer = new DataWriter(); writer.WriteBytes(fragment);
                var result = await _rx.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse).AsTask(token);
                if (result.Status != GattCommunicationStatus.Success) throw new IOException("Bluetooth send failed.");
            }
        }
        finally { _writes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        _frames.Writer.TryComplete(); _tx.ValueChanged -= OnValueChanged;
        try { await _tx.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None); } catch { }
        _gattSession.Dispose(); _service.Dispose(); _device.Dispose();
    }
}
