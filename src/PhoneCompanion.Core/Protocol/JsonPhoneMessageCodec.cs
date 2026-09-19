using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Protocol;

// Transport-independent logical protocol v1, shared by the Windows and Android implementations.
public sealed class JsonPhoneMessageCodec : IPhoneMessageCodec
{
    public const int Version = 1;
    public const int MaxClipboardTextBytes = 12 * 1024;
    public int MaxFrameBytes => 16 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 12 };

    public byte[] Encode(PhoneMessage message)
    {
        var root = new JsonObject { ["version"] = Version };
        switch (message)
        {
            case BatteryUpdate m: root["type"] = "battery"; Copy(Battery(m.State), root); break;
            case MediaUpdate m: root["type"] = "media"; root["state"] = Media(m.State); break;
            case CellularUpdate m: root["type"] = "cellular"; Copy(Cellular(m.State), root); break;
            case DndUpdate m: root["type"] = "dnd"; Copy(Dnd(m.State), root); break;
            case SoundModeUpdate m: root["type"] = "sound_mode"; root["mode"] = Sound(m.State); break;
            case StateSnapshot m:
                root["type"] = "snapshot";
                root["battery"] = m.Battery is null ? null : Battery(m.Battery);
                root["media"] = Media(m.Media);
                root["cellular"] = m.Cellular is null ? null : Cellular(m.Cellular);
                root["dnd"] = m.Dnd is null ? null : Dnd(m.Dnd);
                root["sound"] = m.Sound is null ? null : JsonValue.Create(Sound(m.Sound.Value));
                break;
            case MediaCommandMessage m:
                root["type"] = "media_command";
                root["command"] = m.Command switch
                {
                    MediaCommand.PlayPause => "play_pause", MediaCommand.NextTrack => "next_track",
                    MediaCommand.PreviousTrack => "previous_track", _ => throw new ArgumentOutOfRangeException(nameof(message))
                };
                break;
            case PcMediaUpdate m:
                root["type"] = "pc_media";
                root["state"] = Media(m.State);
                break;
            case PcMediaCommandMessage m:
                root["type"] = "pc_media_command";
                root["command"] = MediaCommandText(m.Command);
                break;
            case DndRuleCommandMessage m:
                root["type"] = "dnd_rule_command";
                root["active"] = m.Active;
                break;
            case ClipboardUpdate m:
                root["type"] = "clipboard";
                root["updateId"] = m.Content.UpdateId.ToString("D");
                root["text"] = m.Content.Text;
                break;
            default: throw new ArgumentException("Unsupported message.", nameof(message));
        }
        var frame = JsonSerializer.SerializeToUtf8Bytes(root, Options);
        if (frame.Length > MaxFrameBytes || !Decode(frame).Success)
            throw new ArgumentException("Message is outside the v1 protocol limits.", nameof(message));
        return frame;
    }

    public DecodeResult Decode(ReadOnlyMemory<byte> frame)
    {
        if (frame.IsEmpty) return new(null, DecodeError.Empty);
        if (frame.Length > MaxFrameBytes) return new(null, DecodeError.TooLarge);
        try
        {
            using var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 12 });
            var r = document.RootElement;
            ValidateObject(r);
            if (Int(r, "version") != Version) return new(null, DecodeError.UnsupportedVersion);
            var type = Text(r, "type", 32, required: true);
            PhoneMessage? message = type switch
            {
                "battery" => new BatteryUpdate(ReadBattery(r)),
                "media" => new MediaUpdate(ReadNullable(r.GetProperty("state"), ReadMedia)),
                "cellular" => new CellularUpdate(ReadCellular(r)),
                "dnd" => new DndUpdate(ReadDnd(r)),
                "sound_mode" => new SoundModeUpdate(ReadSound(r.GetProperty("mode"))),
                "snapshot" => ReadSnapshot(r),
                "media_command" => new MediaCommandMessage(Text(r, "command", 32, true) switch
                {
                    "play_pause" => MediaCommand.PlayPause, "next_track" => MediaCommand.NextTrack,
                    "previous_track" => MediaCommand.PreviousTrack, _ => throw new FormatException()
                }),
                "pc_media" => new PcMediaUpdate(ReadNullable(r.GetProperty("state"), ReadMedia)),
                "pc_media_command" => new PcMediaCommandMessage(ReadMediaCommand(r)),
                "dnd_rule_command" => new DndRuleCommandMessage(Bool(r, "active")),
                "clipboard" => new ClipboardUpdate(ReadClipboard(r)),
                _ => null
            };
            return message is null ? new(null, DecodeError.UnknownType) : new(message, DecodeError.None);
        }
        catch (JsonException) { return new(null, DecodeError.Malformed); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return new(null, DecodeError.InvalidPayload); }
    }

    private static void ValidateObject(JsonElement element)
    {
        // Reject ambiguous duplicate keys, including nested objects. Unknown fields remain forward-compatible.
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            {
                if (!keys.Add(p.Name)) throw new FormatException();
                ValidateObject(p.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateObject(child);
    }
    private static int Int(JsonElement r, string name) => r.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement r, string name) => r.GetProperty(name).GetBoolean();
    private static bool? NullableBool(JsonElement r, string name)
    {
        if (!r.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.GetBoolean();
    }
    private static string? Text(JsonElement r, string name, int max = 256, bool required = false)
    {
        if (!r.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new FormatException();
            return null;
        }
        var s = v.GetString() ?? throw new FormatException();
        if (s.Length > max || s.Any(char.IsControl) || (required && string.IsNullOrWhiteSpace(s))) throw new FormatException();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
    private static T? ReadNullable<T>(JsonElement e, Func<JsonElement, T> read) where T : class =>
        e.ValueKind == JsonValueKind.Null ? null : read(e);
    private static BatteryState ReadBattery(JsonElement r)
    {
        var level = Int(r, "level");
        if (level is < 0 or > 100) throw new FormatException();
        return new(level, Bool(r, "charging"));
    }
    private static MediaState ReadMedia(JsonElement r)
    {
        var c = r.GetProperty("capabilities");
        return new(Text(r, "source", 80), Text(r, "title"), Text(r, "artist"), Bool(r, "isPlaying"),
            new(Bool(c, "playPause"), Bool(c, "nextTrack"), Bool(c, "previousTrack")));
    }
    private static CellularState ReadCellular(JsonElement r)
    {
        var isUsingCellularData = Bool(r, "isUsingCellularData");
        var isUsingWifi = NullableBool(r, "isUsingWifi");
        if (isUsingCellularData && isUsingWifi == true) throw new FormatException();
        return new(isUsingCellularData, Text(r, "network", 16, true) switch
        {
            "unknown" => CellularNetwork.Unknown, "cellular" => CellularNetwork.Cellular,
            "2g" => CellularNetwork.TwoG, "3g" => CellularNetwork.ThreeG,
            "4g" => CellularNetwork.FourG, "5g" => CellularNetwork.FiveG, _ => throw new FormatException()
        }, Text(r, "signal", 16, true) switch
        {
            "unknown" => SignalStrength.Unknown, "none" => SignalStrength.None,
            "poor" => SignalStrength.Poor, "fair" => SignalStrength.Fair,
            "good" => SignalStrength.Good, "excellent" => SignalStrength.Excellent, _ => throw new FormatException()
        }, isUsingWifi);
    }
    private static SoundMode ReadSound(JsonElement v) => v.GetString() switch
    {
        "normal" => SoundMode.Normal, "vibrate" => SoundMode.Vibrate,
        "silent" => SoundMode.Silent, _ => throw new FormatException()
    };
    private static DndState ReadDnd(JsonElement r)
    {
        var companionRuleActive = NullableBool(r, "companionRuleActive");
        var canControl = NullableBool(r, "canControlCompanionRule") ?? false;
        if (canControl && companionRuleActive is null) throw new FormatException();
        return new(Bool(r, "enabled"), companionRuleActive, canControl);
    }
    private static ClipboardContent ReadClipboard(JsonElement r)
    {
        var updateIdText = Text(r, "updateId", 36, required: true);
        if (!Guid.TryParseExact(updateIdText, "D", out var updateId)) throw new FormatException();
        var text = r.GetProperty("text").GetString() ?? throw new FormatException();
        if (text.Length == 0 || text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > MaxClipboardTextBytes)
            throw new FormatException();
        return new(updateId, text);
    }
    private static StateSnapshot ReadSnapshot(JsonElement r) => new(
        ReadNullable(r.GetProperty("battery"), ReadBattery), ReadNullable(r.GetProperty("media"), ReadMedia),
        ReadNullable(r.GetProperty("cellular"), ReadCellular),
        ReadNullable(r.GetProperty("dnd"), ReadDnd),
        r.GetProperty("sound").ValueKind == JsonValueKind.Null ? null : ReadSound(r.GetProperty("sound")));
    private static JsonObject Battery(BatteryState s) => new() { ["level"] = s.Level, ["charging"] = s.Charging };
    private static JsonObject? Media(MediaState? s) => s is null ? null : new()
    {
        ["source"] = s.Source, ["title"] = s.Title, ["artist"] = s.Artist, ["isPlaying"] = s.IsPlaying,
        ["capabilities"] = new JsonObject { ["playPause"] = s.Capabilities.PlayPause,
            ["nextTrack"] = s.Capabilities.NextTrack, ["previousTrack"] = s.Capabilities.PreviousTrack }
    };
    private static JsonObject Dnd(DndState s) => new()
    {
        ["enabled"] = s.Enabled,
        ["companionRuleActive"] = s.CompanionRuleActive is bool active ? JsonValue.Create(active) : null,
        ["canControlCompanionRule"] = s.CanControlCompanionRule
    };
    private static JsonObject Cellular(CellularState s) => new()
    {
        ["isUsingCellularData"] = s.IsUsingCellularData,
        ["isUsingWifi"] = s.IsUsingWifi is bool wifi ? JsonValue.Create(wifi) : null,
        ["network"] = s.Network switch { CellularNetwork.Unknown => "unknown", CellularNetwork.Cellular => "cellular",
            CellularNetwork.TwoG => "2g", CellularNetwork.ThreeG => "3g", CellularNetwork.FourG => "4g",
            CellularNetwork.FiveG => "5g", _ => throw new ArgumentOutOfRangeException(nameof(s)) },
        ["signal"] = s.Signal.ToString().ToLowerInvariant()
    };
    private static string Sound(SoundMode s) => s.ToString().ToLowerInvariant();
    private static MediaCommand ReadMediaCommand(JsonElement r) => Text(r, "command", 32, true) switch
    {
        "play_pause" => MediaCommand.PlayPause,
        "next_track" => MediaCommand.NextTrack,
        "previous_track" => MediaCommand.PreviousTrack,
        _ => throw new FormatException()
    };
    private static string MediaCommandText(MediaCommand command) => command switch
    {
        MediaCommand.PlayPause => "play_pause",
        MediaCommand.NextTrack => "next_track",
        MediaCommand.PreviousTrack => "previous_track",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
    private static void Copy(JsonObject from, JsonObject to)
    {
        foreach (var (key, value) in from) to[key] = value?.DeepClone();
    }
}
