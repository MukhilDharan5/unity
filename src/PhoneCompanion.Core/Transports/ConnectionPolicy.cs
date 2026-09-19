namespace PhoneCompanion.Core.Transports;

public static class ConnectionPolicy
{
    private static readonly TimeSpan[] ReconnectPauses =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
         TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)];

    public static TimeSpan PairingTimeout { get; } = TimeSpan.FromMinutes(2);
    public static TimeSpan WifiConnectTimeout { get; } = TimeSpan.FromSeconds(8);
    public static TimeSpan BleConnectTimeout { get; } = TimeSpan.FromSeconds(15);
    public static TimeSpan BleDiscoveryTimeout { get; } = TimeSpan.FromSeconds(20);
    public static TimeSpan LanDiscoveryTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan CommandTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan PeerTimeout { get; } = TimeSpan.FromSeconds(35);
    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromSeconds(10);
    public const int BleFragmentTimeoutMilliseconds = 15_000;

    public static TimeSpan ReconnectDelay(int attempt) =>
        ReconnectPauses[Math.Clamp(attempt, 0, ReconnectPauses.Length - 1)];
}
