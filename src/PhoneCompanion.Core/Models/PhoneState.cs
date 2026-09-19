namespace PhoneCompanion.Core.Models;

public enum ConnectionState { Disconnected, Connecting, Connected }
public enum TransportKind { Mock, Ble, Wifi }
public enum SoundMode { Normal, Vibrate, Silent }
public enum SignalStrength { Unknown, None, Poor, Fair, Good, Excellent }
public enum CellularNetwork { Unknown, Cellular, TwoG, ThreeG, FourG, FiveG }
public enum AmbientLightStatus { Valid, Covered, Unavailable }

public sealed record BatteryState(int Level, bool Charging);
// Enabled is the phone's effective DND state. CompanionRuleActive only represents
// the app-owned Android rule; turning it off must not imply effective DND is off.
public sealed record DndState(
    bool Enabled,
    bool? CompanionRuleActive = null,
    bool CanControlCompanionRule = false);
// IsUsingWifi is nullable for compatibility with v1 peers that only report cellular-data use.
// True/false means explicitly reported; null means the data connection is unavailable/unknown.
public sealed record CellularState(
    bool IsUsingCellularData,
    CellularNetwork Network,
    SignalStrength Signal,
    bool? IsUsingWifi = null);
public sealed record MediaCapabilities(bool PlayPause, bool NextTrack, bool PreviousTrack);
public sealed record MediaState(
    string? Source, string? Title, string? Artist, bool IsPlaying, MediaCapabilities Capabilities);
public sealed record PhoneBrightnessState(
    int Level,
    bool Adaptive,
    bool CanControl,
    double? AmbientLux,
    AmbientLightStatus AmbientStatus);
public sealed record ClipboardContent(Guid UpdateId, string Text);

// Null means the phone has not supplied this state. Never infer an Off/Normal/0% value.
public sealed record PhoneState(
    ConnectionState Connection,
    TransportKind? Transport = null,
    BatteryState? Battery = null,
    MediaState? Media = null,
    CellularState? Cellular = null,
    DndState? Dnd = null,
    SoundMode? Sound = null,
    PhoneBrightnessState? Brightness = null)
{
    public static PhoneState Empty { get; } = new(ConnectionState.Disconnected);
    public bool IsDemo => Transport == TransportKind.Mock;
}
