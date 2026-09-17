# Cross-Device Companion --- Idea Dump

## Product Direction

Build a lightweight **Windows tray app + Android companion app** that
exposes useful cross-device controls and buried OEM/system functionality
without duplicating Windows Quick Settings. Keep the UI simple and hide
implementation details such as BLE, ADB, GATT, dBm, codecs, and system
APIs.

Use **BLE for discovery, presence, and tiny control/state packets**,
**Wi-Fi/LAN for richer communication**, and optionally **wireless ADB
for enhanced Android features**.

## Windows Tray / Control Center

-   Tray icon is the main entry point.
-   Hover shows quick status; click opens a compact flyout.
-   Full GUI is reserved for setup/advanced options.
-   Expose Lenovo/OEM Quiet, Balanced, Performance profiles rather than
    ordinary Windows power plans.
-   Show phone connection, battery, cellular status, media, DND, sound
    state, audio destination, clipboard status, and contextual actions
    such as **Open phone** or **Use phone internet**.

Example:

``` text
Phone                         Connected
68% · 5G · Good signal

Laptop Power                  Balanced
Audio                         Laptop

Now Playing
Song Title — Artist
[ Previous ] [ Play/Pause ] [ Next ]

Do Not Disturb                Synced
Phone Sound                   Vibrate
Clipboard                     Synced

[ Open phone ]
```

## Connection Architecture

-   BLE: discovery, trusted-device identification, presence, tiny state
    updates and commands.
-   Wi-Fi/LAN: richer/faster synchronization.
-   Wi-Fi Direct: possible future large-transfer path.
-   Wireless ADB: optional enhanced-control path, not the normal status
    transport.
-   Internet relay: possible future remote fallback.
-   Prefer event/change-driven updates instead of frequent polling.
-   Abstract transports so the rest of the app does not care whether a
    message came through BLE or Wi-Fi.

## Phone Status on Windows

Receive and display: - Battery percentage and charging state. -
Wi-Fi/mobile-data state. - LTE/5G generation and simplified cellular
signal. - Current DND state. - Sound mode: Normal / Vibrate / Silent. -
Connection/presence state.

## Phone Media on Windows

Show the phone's current media session: - Source/app. - Track/title. -
Artist. - Playing/paused state. - Previous, Play/Pause, Next controls.

## PC Media / Activity on Phone

Send Windows media state to the phone: - Source, title, artist,
playing/paused. - Play/Pause/Next/Previous controls.

Optionally send a privacy-friendly simplified PC activity category such
as: - Idle - Browsing - Watching video - Gaming - In a call - Locked -
Sleeping

Classify locally on Windows and send only the category rather than raw
process lists.

## Cross-Device DND

Synchronize DND between Windows and Android. Android can report its DND
state. For modern Android control, use an app-owned `AutomaticZenRule`
rather than taking ownership of the user's manual/global DND state.
Turning the companion rule off should not disable DND requested by the
user or another rule.

User-facing UI should simply say something like:

``` text
Do Not Disturb    Synced
```

## Cross-Device Clipboard

Synchronize clipboard content between trusted phone and laptop, with an
easy sync toggle and future privacy/history controls.

## Phone Screen / Remote Control

Use **scrcpy** as the phone-screen/control engine rather than
reinventing mirroring.

Desired UX:

``` text
Phone · Connected
[ Open phone ]
```

Wireless ADB can be authorized once where possible and reused. Hide
consoles and ADB details from the user. Do not use ADB for routine
battery/DND/media synchronization.

## Cross-Device Audio

Long-term UX:

``` text
Audio
○ Laptop
○ Phone
○ Headphones
```

Possible phone → laptop path: Android playback capture where permitted,
encode over Wi-Fi, play on Windows.

Possible laptop → phone path: capture Windows output, encode/stream,
play through the Android app.

A normal Android app cannot simply pretend to be an arbitrary physical
Bluetooth/USB audio endpoint, so hide the streaming implementation
behind the simple destination selector. Protected/DRM audio, calls, and
apps that prohibit capture may be unavailable.

## Move Bluetooth Headphones Between Phone and Laptop

Desired UX:

``` text
Galaxy Buds → Phone
[ Move to laptop ]
```

Flow:

``` text
Laptop requests transfer
        ↓
Phone disconnects Buds
        ↓
Laptop connects to Buds
```

Newer Android exposes a cleaner public Bluetooth-device disconnect
route. For versions where that is unavailable, investigate
Samsung-specific integration, optional Shizuku, or wireless
ADB/system-service methods. Accessibility automation is a last resort.
Keep this capability-detected.

## Automatic Phone Hotspot

Desired experience:

``` text
Laptop has no internet
[ Use phone internet ]
        ↓
BLE request
        ↓
Phone enables hotspot
        ↓
Laptop joins saved hotspot
```

BLE is only the control plane; Wi-Fi carries internet traffic.

A normal modern Android app generally cannot silently enable normal
internet tethering using the standard public API. `LocalOnlyHotspot` is
not internet tethering. Investigate Samsung Modes & Routines,
Samsung/Link-to-Windows integration, optional ADB/Shizuku, or a one-tap
system fallback.

## Samsung Modes & Routines

Potentially use Samsung's automation system as a bridge for
OEM-privileged actions. Example:

``` text
Laptop → BLE command → companion app → Samsung Routine → Hotspot ON
```

This needs device-specific testing; do not assume a public third-party
routine-trigger API exists.

## Phone Brightness

The Android companion can read: - Current system brightness. -
Adaptive-brightness mode.

With user-granted Modify System Settings access, it can also change
system brightness.

Possible Windows UI:

``` text
Phone brightness
──────●──── 62%   Auto
```

## Shared Adaptive Brightness

Use the phone's ambient-light sensor as an ambient sensor for a laptop
that lacks one:

``` text
Phone light sensor
       ↓ lux
BLE / Wi-Fi
       ↓
Windows companion
       ↓
Laptop brightness
```

Do not synchronize raw percentages because phone and laptop displays
have different brightness curves and maximum luminance. Map ambient lux
independently to a suitable phone brightness and laptop brightness. Use
smoothing and hysteresis.

## Pocket / Sensor-Block Detection

Do not trust ambient-light readings when the phone is covered.
Combine: - Proximity sensor. - Light sensor. -
Accelerometer/orientation. - Screen state. - Sudden lux changes.

Possible state machine:

``` text
VALID → BLOCKED → POCKET
  ↑                 ↓
  └──── uncovered ──┘
```

When invalid, Windows holds the last good brightness rather than
reacting to the bad lux value.

Example protocol:

``` json
{"type":"ambient_light","lux":184,"valid":true}
```

or:

``` json
{"type":"ambient_light","valid":false,"reason":"pocket"}
```

## Tiny Control Protocol

Potential messages/events include:

``` text
BATTERY_UPDATE
MEDIA_STATE
MEDIA_COMMAND
DND_STATE
SOUND_MODE
CELLULAR_STATE
BRIGHTNESS_STATE
AMBIENT_LIGHT
HOTSPOT_REQUEST
BLUETOOTH_DISCONNECT_REQUEST
DEVICE_LOCK_STATE
```

Version the protocol and keep serialization replaceable.

## Windows Lock / Phone-Assisted Unlock

Phone leaving BLE range can optionally trigger Windows locking.

For deliberate **phone → PC unlock**, do not use simple Bluetooth
presence. A normal tray app cannot directly unlock Windows. Treat this
as a later security module using a Windows Credential Provider plus a
background service.

Desired UX:

``` text
💻 IdeaPad
🔒 Locked
[ Unlock laptop ]
```

Use cryptographic challenge-response:

``` text
PC sends random challenge
        ↓
User taps Unlock on phone
        ↓
Phone optionally verifies fingerprint/face/PIN
        ↓
Hardware-backed phone key signs challenge
        ↓
PC verifies signature
        ↓
Credential Provider authenticates
```

Captured BLE packets should not be replayable, and mere phone proximity
should never equal authentication.

## High-Level Architecture

``` text
Windows
├── Tray UI
├── Phone State Manager
├── Media Manager
├── Activity Classifier
├── OEM Power Controller
├── Brightness Controller
├── Protocol Layer
├── Trusted Device / Security Layer
└── Transport Manager
    ├── BLE
    ├── Wi-Fi/LAN
    └── Optional ADB

Android
├── Companion Service
├── Media Integration
├── Battery / Network State
├── DND / Sound State
├── Brightness / Sensor Manager
├── Pocket Detection
├── Bluetooth Integration
├── Protocol Layer
├── Trusted Device / Security Layer
└── Transport Manager
    ├── BLE
    └── Wi-Fi/LAN
```

## Design Principles

-   Keep the default UI extremely simple.
-   Present capabilities, not protocols.
-   Hide BLE/Wi-Fi/ADB internals.
-   Prefer event-driven updates over polling.
-   Keep idle power consumption tiny.
-   Do not duplicate easy Windows controls.
-   Focus on cross-device actions and buried OEM controls.
-   Detect capabilities instead of assuming every Android/OEM version
    supports everything.
-   Keep ADB/Shizuku/privileged functionality optional.
-   Require explicit trust for sensitive actions.
-   Keep Credential Provider/authentication code separate from the
    normal tray application.
-   Build incrementally.

## Immediate Development Scope

For now, build **only the Windows side**: - BLE + Wi-Fi connection
architecture. - Phone media display/control. - Phone battery. - Cellular
state/signal. - DND state. - Normal/Vibrate/Silent state. - Mock phone
data and protocol placeholders until the Android companion is built.

Do not prematurely build the Android app or expand into the experimental
features unless explicitly requested.
