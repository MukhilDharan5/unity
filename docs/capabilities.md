# Capability inventory

Status: observed implementation, 17 September 2026. “Implemented” means code exists; physical-device readiness is not established. There is no general capability-negotiation message in v1.

## Current capability facts

| Capability | Current direction/state | Gating today | Gap |
| --- | --- | --- | --- |
| Presence | Connected/connecting/disconnected on both apps | Authenticated route status | No separate BLE presence model; no reasoned reconnect state |
| Battery | Android -> Windows | Nullable snapshot section | Phone broadcast/hardware validation |
| Network/cellular | Android -> Windows | Permission-aware friendly categories and event callbacks | Physical-device and multi-SIM policy validation absent |
| Sound mode | Android -> Windows, read-only | Nullable snapshot section | No sound-mode command/provider |
| Media control | State and commands in both directions | Per-session `playPause`, `nextTrack`, `previousTrack` flags | Windows current-session selection and both platform action behavior need device/app validation |
| Companion DND | Windows/local Android -> app-owned Android rule | `canControlCompanionRule`, nullable rule state | No Windows DND provider or cross-device automation policy |
| Clipboard | Both directions, text only | Independent opt-in and connected real route | No negotiated remote enablement/support or route suitability; Android send is manual |
| Screen/control | Optional scrcpy launcher on Windows | Local executable and Android debugging authorization | No bundled executable or automated wireless-debugging setup |
| OEM laptop profile | Absent | None | Requires provider discovery and hardware research |
| Brightness/ambient/pocket | Android -> Windows state; Windows -> Android control | Special Android settings access for control; light/proximity sensor availability | Device filtering, pocket classification and OEM brightness behavior need validation |
| Laptop adaptive brightness | Phone ambient light -> Windows integrated display, opt-in per app session | Valid non-demo phone state plus `WmiMonitorBrightness` support | Laptop-panel comfort/power validation; external DDC/CI monitors are not supported |
| Bluetooth audio observation | Local Android UI | Existing `BLUETOOTH_CONNECT` access and public A2DP callbacks | Physical-device validation; only the first connected output is shown |
| Headphone handoff | Provider decision pending | Public API 37 association, assisted fallback, or optional privileged provider | No cross-device state/command or disconnect action until ADR-005 is decided |
| Hotspot/audio streaming | Absent | None | Public/OEM/optional privileged feasibility unresolved |
| PC activity/lock/unlock | Absent | None | Privacy policy; lock safeguards; separate unlock security project |

Nullable data is distinct from unsupported functionality. Battery unavailable is not 0%; no media session and missing notification access currently share `media:null`. Missing phone-state permission produces unknown cellular fields, not a known lack of cellular hardware.

## Proposed foundation model

Treat these as separate facts: platform supports a feature; permission is granted; user enabled it; current authenticated peer negotiated it; selected route is suitable; current device state makes the command applicable. The UI should receive a friendly actionable availability reason instead of interpreting raw API failures.

Advertise feature support at authenticated session establishment and update availability after permission changes. Do not advertise unimplemented features or infer support from brand/model, Bluetooth name, package name or ADB availability. The exact wire addition, version policy and downgrade behavior require approval through [ADR-002](decisions/ADR-002-protocol-format.md).

Future identifiers may cover `MEDIA_CONTROL`, `DND_SYNC`, `CLIPBOARD_SYNC`, `BRIGHTNESS_CONTROL`, `AMBIENT_LIGHT`, `HOTSPOT_CONTROL`, `BT_DEVICE_DISCONNECT`, `SCREEN_MIRRORING` and `AUDIO_STREAMING`. They are roadmap names, not v1 messages or active capabilities. Enhanced/privileged providers must remain optional and isolated.
