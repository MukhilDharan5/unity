using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Protocol;

public abstract record PhoneMessage;
public sealed record BatteryUpdate(BatteryState State) : PhoneMessage;
public sealed record MediaUpdate(MediaState? State) : PhoneMessage;
public sealed record CellularUpdate(CellularState State) : PhoneMessage;
public sealed record DndUpdate(DndState State) : PhoneMessage;
public sealed record SoundModeUpdate(SoundMode State) : PhoneMessage;
public sealed record StateSnapshot(BatteryState? Battery, MediaState? Media,
    CellularState? Cellular, DndState? Dnd, SoundMode? Sound) : PhoneMessage;
public sealed record MediaCommandMessage(MediaCommand Command) : PhoneMessage;
public sealed record PcMediaUpdate(MediaState? State) : PhoneMessage;
public sealed record PcMediaCommandMessage(MediaCommand Command) : PhoneMessage;
public sealed record DndRuleCommandMessage(bool Active) : PhoneMessage;
public sealed record ClipboardUpdate(ClipboardContent Content) : PhoneMessage;

public enum DecodeError { None, Empty, TooLarge, Malformed, UnsupportedVersion, UnknownType, InvalidPayload }
public readonly record struct DecodeResult(PhoneMessage? Message, DecodeError Error)
{
    public bool Success => Message is not null && Error == DecodeError.None;
}

public interface IPhoneMessageCodec
{
    int MaxFrameBytes { get; }
    byte[] Encode(PhoneMessage message);
    DecodeResult Decode(ReadOnlyMemory<byte> frame);
}
