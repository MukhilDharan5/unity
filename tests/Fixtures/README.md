# Shared application protocol fixtures

`protocol-v1.json` is consumed by the .NET core executable and Android JVM suite. It contains fictional positive/negative cases; no sensitive phone data or keys. `payload` is a raw JSON string so duplicate keys, escapes and malformed syntax survive fixture loading.

`valid` describes strict logical application-v1 acceptance, independent of transport direction. `direction` distinguishes Android state -> Windows, Windows commands -> Android, and bidirectional clipboard; Android must not execute valid state messages as commands. Both suites must check every case and report the failing name. Accepted typed snapshots/messages must retain round-trip semantics.

The corpus does not specify secure handshake/control records, prove radio interoperability, or add acknowledgments/negotiation. Depth limits are logical-codec limits; the existing secure parser's narrower depth limit remains a separate follow-up. Add cross-runtime regression cases here when the agreed application contract changes.
