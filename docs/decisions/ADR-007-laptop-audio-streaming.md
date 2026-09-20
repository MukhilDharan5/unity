# ADR-007 — Laptop audio streaming MVP

## Status

Accepted and implemented for the Stage 15 MVP on 20 September 2026. Physical-device latency, battery and interruption testing is deferred.

## Context

The product direction includes phone-to-laptop and laptop-to-phone audio, but the two directions have different platform costs. Windows supports system-render loopback capture. Android can play application-supplied PCM through `AudioTrack`; capturing other Android apps would instead require `MediaProjection` consent, `RECORD_AUDIO`, and cooperation from the source application. Protected output may be excluded from capture.

The companion control session is bandwidth-limited and also carries state and commands. Sending continuous audio through that ordered path could delay normal controls, especially when BLE is the active route.

## Decision

Implement laptop-to-phone first. The explicit, capability-gated **Play on phone** action starts capture and the in-app status reports progress; no separate confirmation dialog interrupts the main app flow. Windows uses NAudio 3.1 WASAPI render-loopback capture and converts the current mono/stereo mix to PCM16. Android opens an ephemeral TCP listener and plays the received PCM with `AudioTrack` in streaming mode.

The existing authenticated session carries only `audio_stream_start`, `audio_sink_ready`, and `audio_stream_stop`. Each start creates a random 256-bit AES key and 128-bit connection token. The key and token travel inside the existing encrypted session. The separate Wi-Fi socket authenticates its one client with the token, then protects every ordered PCM record with AES-256-GCM and a sequence-derived nonce. Either app can stop the matching stream. Keys and tokens are not persisted.

The prototype accepts 8–96 kHz mono or stereo from the current Windows output. It uses raw PCM to keep the first implementation small and debuggable; codec negotiation, resampling, congestion control, jitter adaptation and route migration are deferred. Audio never falls back to BLE.

## Consequences

Both devices must be on a mutually reachable local network even if their control session currently uses BLE. Raw PCM uses substantially more bandwidth and power than a compressed codec. The current buffering favors continuity over a low-latency claim. Output-device changes, phone calls, audio-focus loss, sleep, network changes and app interruptions end the stream rather than migrate it.

DRM-protected output may be silent or unavailable. This stage does not capture phone audio, call audio, microphones, or individual Windows applications. It adds no Android permission. Device testing must establish latency, underrun behavior, battery cost and OEM audio-focus behavior before this can be described as production-ready.
