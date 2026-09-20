using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Protocol;

public abstract record PhoneMessage;
public sealed record BatteryUpdate(BatteryState State) : PhoneMessage;
public sealed record MediaUpdate(MediaState? State) : PhoneMessage;
public sealed record CellularUpdate(CellularState State) : PhoneMessage;
public sealed record DndUpdate(DndState State) : PhoneMessage;
public sealed record SoundModeUpdate(SoundMode State) : PhoneMessage;
public sealed record StateSnapshot(BatteryState? Battery, MediaState? Media,
    CellularState? Cellular, DndState? Dnd, SoundMode? Sound,
    PhoneBrightnessState? Brightness = null,
    PhoneAudioOutputState? AudioOutput = null) : PhoneMessage;
public sealed record MediaCommandMessage(MediaCommand Command) : PhoneMessage;
public sealed record PcMediaUpdate(MediaState? State) : PhoneMessage;
public sealed record PcMediaCommandMessage(MediaCommand Command) : PhoneMessage;
public sealed record PhoneBrightnessCommandMessage(int? Level, bool? Adaptive) : PhoneMessage;
public sealed record DndRuleCommandMessage(bool Active) : PhoneMessage;
public sealed record HeadphoneHandoffCommandMessage : PhoneMessage;
public sealed record HotspotRequestCommandMessage : PhoneMessage;
public sealed record PhoneLockRequestCommandMessage : PhoneMessage;
public sealed record PcLockRequestMessage : PhoneMessage;
public sealed record AudioStreamStartCommandMessage(
    Guid StreamId, byte[] Key, byte[] Token, int SampleRate, int Channels) : PhoneMessage;
public sealed record AudioStreamStopCommandMessage(Guid StreamId) : PhoneMessage;
public sealed record AudioSinkReadyMessage(Guid StreamId, int Port) : PhoneMessage;
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
