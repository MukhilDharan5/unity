# Phone message protocol — v1

Stage 1A update (17 September 2026): Android now enforces the strict application rules below and normalizes platform media metadata before encoding. Both runtimes pass 67 checked-in positive/negative cases in `tests/Fixtures/protocol-v1.json`; [testing.md](testing.md) preserves the historical regressions and current evidence. Secure handshake/control parsing is outside this application contract and remains follow-up work.

This versioned logical protocol is implemented by the Windows codec and the Android companion foundation. It defines application messages only; BLE/LAN framing, encryption, authentication, and route selection remain below this layer as described in [CONNECTION-CONTRACT.md](CONNECTION-CONTRACT.md).

## Envelope and limits

- One complete UTF-8 JSON object per logical frame. `version` is the integer `1`; `type` is a case-sensitive string.
- Maximum complete frame: **16,384 bytes**; JSON nesting depth: **12**. Adapters must enforce the byte limit *during assembly*, not only after allocating a frame.
- Unknown message types and unsupported versions are rejected without state changes. Unknown object fields are ignored to allow compatible additions. Missing required fields, duplicate keys (including nested ones), invalid values, malformed JSON and control characters in metadata are rejected.
- Optional metadata fields may be absent or `null`. Source is at most 80 characters; title/artist at most 256 each (.NET UTF-16 code units). Blank text becomes unavailable. Media control capabilities are required; they are never inferred from a source name or track.
- `null` means unavailable, never Off, Normal or 0%. For media, `null` also covers no active media; v1 does not distinguish permission denial from no session.
- The selected authenticated session supplies identity and connection state. A payload cannot mark a peer trusted or connected.
- There is no generic capability-negotiation envelope, request ID, application acknowledgment/error response or version-range negotiation in the live v1 path. Kotlin acknowledgment models are unused by live dispatch. Adding those semantics requires a reviewed protocol extension.
- Each session delivers frames in order. There is one active transport at a time. No cross-route merging, automatic retries, timestamps or sequencing behavior is assumed. A route switch resets state and rejects callbacks from the old route.

## Phone → Windows

Battery percentage is an integer from 0 through 100. Charging is a required Boolean.

```json
{"version":1,"type":"battery","level":68,"charging":false}
```

Media state includes explicit available controls. `isPlaying` is a Boolean. Source/title/artist are optional strings.

```json
{
  "version": 1,
  "type": "media",
  "state": {
    "source": "Music",
    "title": "Song Title",
    "artist": "Artist Name",
    "isPlaying": true,
    "capabilities": {"playPause":true,"nextTrack":true,"previousTrack":true}
  }
}
```

Clear active media with `{"version":1,"type":"media","state":null}`.

Connectivity status includes whether the active data connection is cellular or Wi-Fi, plus a friendly cellular generation and simple cellular signal category. No dBm or Windows-derived signal conversion is involved. `isUsingCellularData` is required. `isUsingWifi` is nullable and additive for version 1 compatibility: `true` or `false` means the phone explicitly reported the Wi-Fi state, while missing or `null` means unavailable. Both values cannot be `true` in the same message.

```json
{"version":1,"type":"cellular","isUsingCellularData":false,"isUsingWifi":true,"network":"5g","signal":"good"}
```

`network`: `unknown`, `cellular`, `2g`, `3g`, `4g`, `5g`. The Windows UI presents `4g` as **LTE**.

`signal`: `unknown`, `none`, `poor`, `fair`, `good`, `excellent`. The phone's mapping from its platform signal information needs agreement; Windows does not invent thresholds.

DND reports both Android's effective state and, when available, the app-owned companion rule:

```json
{"version":1,"type":"dnd","enabled":true,"companionRuleActive":false,"canControlCompanionRule":true}
```

`enabled` is the effective phone DND state and is never inferred from the companion rule. `companionRuleActive` may be absent or `null` when the app cannot inspect its rule. `canControlCompanionRule:true` requires a non-null companion state. Turning the companion rule off can therefore leave `enabled:true` when the user or another Android rule still requests DND.

```json
{"version":1,"type":"sound_mode","mode":"vibrate"}
```

Sound mode is `normal`, `vibrate` or `silent`.

A full snapshot replaces all five state sections atomically. **All five keys are required**, each may be `null`. This can initialize a new session or clear values that are no longer available. Ordinary updates change only their own section.

```json
{
  "version": 1,
  "type": "snapshot",
  "battery": {"level":68,"charging":false},
  "media": null,
  "cellular": {"isUsingCellularData":false,"isUsingWifi":true,"network":"5g","signal":"good"},
  "dnd": {"enabled":true,"companionRuleActive":false,"canControlCompanionRule":true},
  "sound": "vibrate"
}
```

New peers may add an optional `brightness` section to the snapshot. Its absence remains valid for older v1 peers:

```json
"brightness":{"level":62,"adaptive":true,"canControl":true,"ambientLux":184.2,"ambientStatus":"valid"}
```

`level` is 0–100. `ambientStatus` is `valid`, `covered`, or `unavailable`; only `valid` carries an `ambientLux` number from 0 through 200,000. `canControl` reflects Android's current special settings access and is never inferred on Windows.

The optional `audioOutput` section reports the first connected Android A2DP output. Its absence means no output is available or the older peer does not implement the field:

```json
"audioOutput":{"deviceName":"Galaxy Buds","canRelease":true}
```

`deviceName` is a nonblank display label of at most 80 characters. Bluetooth addresses never cross the companion connection. `canRelease` is true only when the running Android version exposes the public release operation and the user has associated this headset through Companion Device Manager.

For example, clearing battery availability in v1 requires a snapshot with `battery:null` and the current values (or `null`) for the other sections. There is no implicit expiry duration in v1. Each future transport's reviewed liveness mechanism must report disconnection; Windows then clears all values immediately.

## Windows → phone

Media controls use this message:

```json
{"version":1,"type":"media_command","command":"play_pause"}
```

`command`: `play_pause`, `next_track`, `previous_track`.

Windows sends media commands only when the current media capabilities allow them. It never executes an incoming `media_command` on Windows.

Windows media state uses the same bounded media shape in a direction-specific message:

```json
{"version":1,"type":"pc_media","state":{"source":"Music","title":"Song Title","artist":"Artist Name","isPlaying":true,"capabilities":{"playPause":true,"nextTrack":true,"previousTrack":true}}}
```

Windows sends `{"version":1,"type":"pc_media","state":null}` when no controllable current media session is available. Android displays this state but does not infer controls beyond the three advertised capability flags.

Android controls the advertised Windows session with the matching direction-specific command:

```json
{"version":1,"type":"pc_media_command","command":"play_pause"}
```

`command` is `play_pause`, `next_track`, or `previous_track`. Android drops unavailable or repeated in-flight commands. Windows executes the command only through the current Global System Media Transport Controls session and publishes the resulting state; writes are not application acknowledgments and are not retried across transports.

Phone brightness changes use:

```json
{"version":1,"type":"brightness_command","level":62,"adaptive":null}
```

`level` is optional but, when present, is 1–100. `adaptive` is an optional Boolean. At least one must be non-null. Android applies the command only while `Settings.System.canWrite` is true. Slider commands preserve the current adaptive setting; the separate adaptive toggle sends only `adaptive`.

Companion DND changes use a distinct command:

```json
{"version":1,"type":"dnd_rule_command","active":true}
```

Android applies this command only to Unity Connect's own `AutomaticZenRule` condition. It never calls a global/manual DND setter. The UI waits for a later DND state message rather than changing effective DND optimistically.

Headphone handoff is an explicit command with no payload fields:

```json
{"version":1,"type":"headphone_handoff"}
```

Android accepts it only on the authenticated active session. A capable associated phone requests release through the public platform API. Otherwise Android posts a user-action notification that opens Bluetooth settings. Windows may also use an optional authorized ADB executable to foreground that same settings screen, but ADB is outside this protocol and never becomes a companion transport. Windows opens its own Bluetooth settings so the user can select the released headset.

Phone-internet assistance is also an explicit command with no payload fields:

```json
{"version":1,"type":"hotspot_request"}
```

Windows sends it only from the visible **Use phone internet** action on an authenticated non-demo session. Android posts a user-action notification into the closest system-owned tethering/wireless settings screen; it does not silently toggle tethering and does not use `LocalOnlyHotspot`. Windows opens its Wi-Fi settings so an existing saved hotspot profile can reconnect or the user can choose the phone network. Optional ADB settings launch is local Windows behavior outside this message protocol.

Clipboard text can travel in either direction over an authenticated session:

```json
{"version":1,"type":"clipboard","updateId":"7d7fc709-84d2-4575-8b99-e262dc8cc78e","text":"Hello"}
```

`updateId` is a canonical UUID used for bounded loop suppression. `text` must be non-empty UTF-8 without NUL characters and at most 12,288 bytes. Clipboard sync is independently opt-in on each device, transfers only new text, and stores no history. Windows watches future clipboard changes while enabled. On Android 10 and later, ordinary background apps cannot continuously read clipboard data, so Android sends its current clipboard only after the user taps **Send current clipboard** while the app is visible. Incoming laptop text may be placed on the Android clipboard while sync is enabled.

Sending has a five-second cancellation deadline. Repeated clicks while a send is pending are dropped. A successful write means only **sent**, not that the phone performed the action. The UI changes only after a phone state message. A failed or ambiguous send is never retried automatically, including over another transport, because a toggle or skip could otherwise execute twice. Android targets the media session that produced its current state. Version 1 uses the following authoritative snapshot rather than a separate command acknowledgment.

## Serialization boundary

On Windows, `IPhoneMessageCodec` owns logical-message serialization, validation and frame-size limits; the phone view model has no JSON APIs. Android calls the JSON `MessageCodec` singleton directly and still needs an equivalent replaceable boundary. Secure-session implementations parse JSON control/envelope fields separately below this layer. Windows secure-envelope parsing has an 8-level depth limit, narrower than the logical codec's 12-level limit; therefore a codec-accepted unknown nested field is not necessarily accepted end-to-end. These boundaries and validation rules need consolidation.

Replacing JSON requires equivalent typed message semantics on both runtimes and an approved migration policy. A factory delivers complete authenticated frames; fragmentation, boundaries, encryption and authentication belong below the logical codec. See [ADR-002](decisions/ADR-002-protocol-format.md).
