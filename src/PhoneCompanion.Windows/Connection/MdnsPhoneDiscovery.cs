using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhoneCompanion.Core.Transports;

namespace PhoneCompanion.Windows.Connection;

public sealed record DiscoveredPhone(string Name, string Endpoint);

public static class MdnsPhoneDiscovery
{
    private const string ServiceName = "_phonecomp._tcp.local";
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");

    public static async Task<IReadOnlyList<DiscoveredPhone>> FindAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionPolicy.LanDiscoveryTimeout);
        using var client = CreateClient(out var multicast);

        var query = BuildQuery(unicastResponse: !multicast);
        await client.SendAsync(query, query.Length, new IPEndPoint(MulticastAddress, 5353));
        var services = new Dictionary<string, ServiceRecord>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, HashSet<IPAddress>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var packet = await client.ReceiveAsync(timeout.Token);
                Parse(packet.Buffer, services, addresses);
                var found = Materialize(services, addresses);
                if (found.Count > 0) return found;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return Array.Empty<DiscoveredPhone>();
    }

    private static UdpClient CreateClient(out bool multicast)
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            client.JoinMulticastGroup(MulticastAddress);
            multicast = true;
            return client;
        }
        catch (Exception error) when (error is SocketException or InvalidOperationException)
        {
            client.Dispose();
            client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            multicast = false;
            return client;
        }
    }

    private static byte[] BuildQuery(bool unicastResponse)
    {
        var labels = ServiceName.Split('.');
        var size = 12 + labels.Sum(label => 1 + Encoding.ASCII.GetByteCount(label)) + 1 + 4;
        var query = new byte[size];
        BinaryPrimitives.WriteUInt16BigEndian(query.AsSpan(4), 1);
        var offset = 12;
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query[offset++] = checked((byte)bytes.Length);
            bytes.CopyTo(query, offset); offset += bytes.Length;
        }
        query[offset++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(query.AsSpan(offset), 12); offset += 2; // PTR
        BinaryPrimitives.WriteUInt16BigEndian(query.AsSpan(offset), unicastResponse ? (ushort)0x8001 : (ushort)1);
        return query;
    }

    private static void Parse(byte[] packet, Dictionary<string, ServiceRecord> services,
        Dictionary<string, HashSet<IPAddress>> addresses)
    {
        try
        {
            if (packet.Length < 12) return;
            var span = packet.AsSpan();
            var questions = ReadUInt16(span, 4);
            var records = ReadUInt16(span, 6) + ReadUInt16(span, 8) + ReadUInt16(span, 10);
            var offset = 12;
            for (var index = 0; index < questions; index++)
            {
                ReadName(span, ref offset);
                offset = checked(offset + 4);
                if (offset > span.Length) return;
            }
            for (var index = 0; index < records; index++)
            {
                var owner = Normalize(ReadName(span, ref offset));
                if (offset + 10 > span.Length) return;
                var type = ReadUInt16(span, offset); offset += 8;
                var length = ReadUInt16(span, offset); offset += 2;
                var data = offset;
                var end = checked(data + length);
                if (end > span.Length) return;
                if (type == 33 && length >= 7 && owner.EndsWith('.' + ServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    var port = ReadUInt16(span, data + 4);
                    var targetOffset = data + 6;
                    var target = Normalize(ReadName(span, ref targetOffset));
                    if (port > 0 && target.Length > 0) services[owner] = new ServiceRecord(owner, target, port);
                }
                else if (type == 1 && length == 4)
                    AddAddress(addresses, owner, new IPAddress(packet.AsSpan(data, 4)));
                else if (type == 28 && length == 16)
                    AddAddress(addresses, owner, new IPAddress(packet.AsSpan(data, 16)));
                offset = end;
            }
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or IndexOutOfRangeException) { }
    }

    private static List<DiscoveredPhone> Materialize(Dictionary<string, ServiceRecord> services,
        Dictionary<string, HashSet<IPAddress>> addresses) => services.Values
        .SelectMany(service => addresses.TryGetValue(service.Target, out var values)
            ? values.Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6Multicast && !address.IsIPv6LinkLocal)
                .Select(address => new DiscoveredPhone(DisplayName(service.Owner), Endpoint(address, service.Port)))
            : Enumerable.Empty<DiscoveredPhone>())
        .DistinctBy(phone => phone.Endpoint, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static void AddAddress(Dictionary<string, HashSet<IPAddress>> addresses, string owner, IPAddress address)
    {
        if (!addresses.TryGetValue(owner, out var values)) addresses[owner] = values = [];
        values.Add(address);
    }

    private static string Endpoint(IPAddress address, int port) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]:{port}" : $"{address}:{port}";
    private static string DisplayName(string owner)
    {
        var suffix = "." + ServiceName;
        return owner.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? owner[..^suffix.Length] : owner;
    }
    private static string Normalize(string value) => value.TrimEnd('.');
    private static ushort ReadUInt16(ReadOnlySpan<byte> packet, int offset)
    {
        if (offset < 0 || offset + 2 > packet.Length) throw new IndexOutOfRangeException();
        return BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);
    }
    private static string ReadName(ReadOnlySpan<byte> packet, ref int offset)
    {
        var labels = new List<string>();
        var cursor = offset;
        var jumped = false;
        var jumps = 0;
        while (true)
        {
            if ((uint)cursor >= (uint)packet.Length || jumps++ > 32) throw new IndexOutOfRangeException();
            var length = packet[cursor];
            if ((length & 0xc0) == 0xc0)
            {
                if (cursor + 1 >= packet.Length) throw new IndexOutOfRangeException();
                var pointer = ((length & 0x3f) << 8) | packet[cursor + 1];
                if (!jumped) offset = cursor + 2;
                cursor = pointer; jumped = true; continue;
            }
            if (length == 0)
            {
                if (!jumped) offset = cursor + 1;
                return string.Join('.', labels);
            }
            if ((length & 0xc0) != 0 || cursor + 1 + length > packet.Length) throw new IndexOutOfRangeException();
            labels.Add(Encoding.UTF8.GetString(packet.Slice(cursor + 1, length)));
            cursor += 1 + length;
        }
    }

    private sealed record ServiceRecord(string Owner, string Target, int Port);
}
