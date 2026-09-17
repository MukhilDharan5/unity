package com.unity.connect.android

import com.unity.connect.android.core.*
import org.junit.Assert.*
import org.junit.Test

class MessageCodecTest {

    @Test
    fun testPhoneSnapshotSerialization() {
        val snapshot = PhoneSnapshot(
            version = 1,
            type = "snapshot",
            battery = BatteryState(level = 85, charging = true),
            media = MediaState(
                source = "com.spotify.music",
                title = "Test Song",
                artist = "Test Artist",
                isPlaying = true,
                capabilities = Capabilities(playPause = true, nextTrack = true, previousTrack = false)
            ),
            cellular = CellularState(
                isUsingCellularData = true,
                isUsingWifi = false,
                network = "5g",
                signal = "good"
            ),
            dnd = DndState(enabled = false),
            sound = "vibrate"
        )

        val json = MessageCodec.encodePhoneSnapshot(snapshot)
        assertTrue(json.contains("\"version\":1"))
        assertTrue(json.contains("\"type\":\"snapshot\""))
        assertTrue(json.contains("\"85\"") || json.contains("85"))

        val decoded = MessageCodec.decodePhoneSnapshot(json)
        assertEquals(1, decoded.version)
        assertEquals("snapshot", decoded.type)
        assertNotNull(decoded.battery)
        assertEquals(85, decoded.battery?.level)
        assertTrue(decoded.battery?.charging == true)
        assertEquals("com.spotify.music", decoded.media?.source)
        assertEquals("Test Song", decoded.media?.title)
        assertEquals("Test Artist", decoded.media?.artist)
        assertTrue(decoded.media?.capabilities?.playPause == true)
        assertFalse(decoded.media?.capabilities?.previousTrack == true)
        assertEquals("5g", decoded.cellular?.network)
        assertEquals("good", decoded.cellular?.signal)
        assertFalse(decoded.dnd?.enabled == true)
        assertEquals("vibrate", decoded.sound)
    }

    @Test
    fun testSnapshotNullSections() {
        val snapshot = PhoneSnapshot(
            version = 1,
            type = "snapshot",
            battery = null,
            media = null,
            cellular = null,
            dnd = null,
            sound = null
        )

        val json = MessageCodec.encodePhoneSnapshot(snapshot)
        val decoded = MessageCodec.decodePhoneSnapshot(json)
        assertNull(decoded.battery)
        assertNull(decoded.media)
        assertNull(decoded.cellular)
        assertNull(decoded.dnd)
        assertNull(decoded.sound)
    }

    @Test
    fun testUnicodeAndSpecialCharsInMedia() {
        val snapshot = PhoneSnapshot(
            version = 1,
            type = "snapshot",
            battery = BatteryState(50, false),
            media = MediaState(
                source = "Music",
                title = "🎵 Music with Emoji & 🎵 Symbols 𝄞",
                artist = "Artist · \", Special",
                isPlaying = false,
                capabilities = Capabilities(playPause = true, nextTrack = true, previousTrack = true)
            ),
            cellular = null,
            dnd = null,
            sound = null
        )

        val json = MessageCodec.encodePhoneSnapshot(snapshot)
        val decoded = MessageCodec.decodePhoneSnapshot(json)
        assertEquals("🎵 Music with Emoji & 🎵 Symbols 𝄞", decoded.media?.title)
        assertEquals("Artist · \", Special", decoded.media?.artist)
    }

    @Test
    fun testMediaCommandCodec() {
        val cmd = MediaCommand(version = 1, type = "media_command", command = "play_pause", command_id = "cmd-123")
        val json = MessageCodec.encodeMediaCommand(cmd)
        assertTrue(json.contains("play_pause"))
        assertTrue(json.contains("cmd-123"))

        val decoded = MessageCodec.decodeMediaCommand(json)
        assertEquals("play_pause", decoded.command)
        assertEquals("cmd-123", decoded.command_id)
    }

    @Test
    fun testCommandAckCodec() {
        val ack = CommandAck(version = 1, type = "command_ack", command_id = "cmd-123", status = "success")
        val json = MessageCodec.encodeCommandAck(ack)
        val decoded = MessageCodec.decodeCommandAck(json)
        assertEquals("cmd-123", decoded.command_id)
        assertEquals("success", decoded.status)
    }

    @Test
    fun testUnknownKeysIgnored() {
        val rawJson = """{"version":1,"type":"battery","level":75,"charging":true,"unknownField":"ignored","extraObj":{"a":1}}"""
        val decoded = MessageCodec.decode<BatteryMessage>(rawJson)
        assertEquals(75, decoded.level)
        assertTrue(decoded.charging)
    }

    @Test(expected = IllegalArgumentException::class)
    fun testFrameSizeLimitExceeded() {
        val hugeBytes = ByteArray(16385)
        MessageCodec.validateFrameSize(hugeBytes)
    }

    @Test(expected = IllegalArgumentException::class)
    fun testNestingDepthExceeded() {
        val deepJson = "{" + "[".repeat(15) + "]".repeat(15) + "}"
        MessageCodec.validateNestingDepth(deepJson, maxDepth = 12)
    }

    @Test
    fun testDndRuleCommandIsSeparateFromEffectiveDnd() {
        val decoded = MessageCodec.decodeIncoming(
            """{"version":1,"type":"dnd_rule_command","active":false}""".toByteArray()
        )
        assertEquals(IncomingMessage.DndRuleCommand(false), decoded)

        val state = DndState(
            enabled = true,
            companionRuleActive = false,
            canControlCompanionRule = true
        )
        assertTrue(state.enabled)
        assertFalse(state.companionRuleActive == true)
    }

    @Test
    fun testClipboardRoundTrip() {
        val original = ClipboardContent(
            updateId = "7d7fc709-84d2-4575-8b99-e262dc8cc78e",
            text = "Hello from Android 👋"
        )
        val encoded = MessageCodec.encodeClipboard(original)
        val decoded = MessageCodec.decodeIncoming(encoded.toByteArray())
        assertEquals(IncomingMessage.ClipboardUpdate(original), decoded)
    }

    @Test(expected = IllegalArgumentException::class)
    fun testOversizedClipboardIsRejected() {
        MessageCodec.encodeClipboard(
            ClipboardContent("7d7fc709-84d2-4575-8b99-e262dc8cc78e", "x".repeat(12289))
        )
    }
}
