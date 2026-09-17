package com.unity.connect.android

import com.unity.connect.android.core.*
import java.util.Random
import kotlinx.serialization.json.*
import org.junit.Assert.*
import org.junit.Test

class V1ContractTest {
    @Test fun sharedFixturesMatchApplicationAcceptanceAndCommandDirection() {
        val stream = requireNotNull(javaClass.classLoader!!.getResourceAsStream("protocol-v1.json")) {
            "Shared protocol fixtures missing from test resources"
        }
        val root = stream.bufferedReader(Charsets.UTF_8).use { Json.parseToJsonElement(it.readText()).jsonObject }
        val fixtures = root.getValue("fixtures").jsonArray
        assertTrue("Fixture source unexpectedly empty", fixtures.size >= 25)
        for (element in fixtures) {
            val fixture = element.jsonObject
            val name = fixture.getValue("name").jsonPrimitive.content
            val valid = fixture.getValue("valid").jsonPrimitive.boolean
            val direction = fixture.getValue("direction").jsonPrimitive.content
            val payload = fixture.getValue("payload").jsonPrimitive.content
            val bytes = payload.toByteArray(Charsets.UTF_8)
            assertEquals("Application fixture $name", valid, MessageCodec.validateApplicationFrame(bytes))
            // This call must never throw, even for object/array fields and malformed input.
            val command = MessageCodec.decodeIncoming(bytes)
            if (valid && direction != "toWindows") assertNotNull("Incoming command fixture $name", command)
            else assertNull("Unexpected Android command fixture $name", command)
            if (valid && MessageCodec.getMessageType(payload) == "snapshot") {
                val snapshot = MessageCodec.decodePhoneSnapshot(payload)
                val encoded = MessageCodec.encodePhoneSnapshot(snapshot)
                assertTrue("Snapshot round-trip $name", MessageCodec.validateApplicationFrame(encoded.toByteArray()))
                assertEquals("Snapshot semantics $name", snapshot, MessageCodec.decodePhoneSnapshot(encoded))
            }
        }
        println("Verified ${fixtures.size} shared v1 fixtures.")
    }

    @Test fun malformedUtf8AndRandomInputNeverExecuteOrThrow() {
        val prefix = "{\"version\":1,\"type\":\"clipboard\",\"updateId\":\"7d7fc709-84d2-4575-8b99-e262dc8cc78e\",\"text\":\"".toByteArray()
        val malformed = prefix + byteArrayOf(0xC3.toByte(), 0x28) + "\"}".toByteArray()
        assertFalse(MessageCodec.validateApplicationFrame(malformed))
        assertNull(MessageCodec.decodeIncoming(malformed))
        val random = Random(9182)
        repeat(2000) {
            val bytes = ByteArray(random.nextInt(2048) + 1).also(random::nextBytes)
            assertNull(MessageCodec.decodeIncoming(bytes))
        }
        assertNull(MessageCodec.decodeIncoming(byteArrayOf()))
        assertNull(MessageCodec.decodeIncoming(ByteArray(MessageCodec.MAX_FRAME_BYTES + 1)))
    }

    @Test fun snapshotPreservesUnavailableAndLegacyOptionalState() {
        val snapshot = MessageCodec.decodePhoneSnapshot("""{
            "version":1,"type":"snapshot","battery":null,
            "media":{"source":" ","title":"\u00a0","isPlaying":false,
                "capabilities":{"playPause":true,"nextTrack":false,"previousTrack":false}},
            "cellular":{"isUsingCellularData":false,"network":"unknown","signal":"unknown"},
            "dnd":{"enabled":true},"sound":null
        }""")
        assertNull(snapshot.battery)
        assertNull(snapshot.media?.source)
        assertNull(snapshot.media?.title)
        assertNull(snapshot.media?.artist)
        assertNull(snapshot.cellular?.isUsingWifi)
        assertEquals(DndState(true), snapshot.dnd)
        assertNull(snapshot.sound)
    }

    @Test fun namedApplicationEncodersRejectInvalidPlatformState() {
        val capabilities = MediaCapabilities(true, false, false)
        val snapshot = PhoneSnapshot(null, MediaState("Audit", "Fixture", "Line\nBreak", true, capabilities), null, null, null)
        assertRejected { MessageCodec.encodePhoneSnapshot(snapshot) }
        assertRejected { MessageCodec.encodeMediaMessage(MediaMessage(1, "media", snapshot.media!!.copy(title = "x".repeat(257)))) }
        assertRejected { MessageCodec.encodeBatteryMessage(BatteryMessage(1, "battery", 101, false)) }
        assertRejected { MessageCodec.encodeDndMessage(DndMessage(1, "dnd", true, null, true)) }
        assertRejected { MessageCodec.encodeClipboard(ClipboardContent("1-1-1-1-1", "fixture")) }
        assertRejected { MessageCodec.decodePhoneSnapshot("""{"version":1,"type":"battery","level":1,"charging":false}""") }
    }

    @Test fun metadataNormalizationProducesWindowsCompatibleUnicodeState() {
        assertEquals("Line Break 🎵", MediaMetadataNormalizer.normalize("Line\nBreak\t🎵"))
        assertNull(MediaMetadataNormalizer.normalize(null))
        assertNull(MediaMetadataNormalizer.normalize(" \n\t\u0085 "))
        val metadata = MediaState(
            MediaMetadataNormalizer.normalize("s".repeat(100), MediaMetadataNormalizer.MAX_SOURCE_UNITS),
            MediaMetadataNormalizer.normalize("a".repeat(255) + "🎵tail"),
            MediaMetadataNormalizer.normalize("Artist\nName"), true, MediaCapabilities(true, false, false)
        )
        assertEquals(80, metadata.source!!.length)
        assertEquals("a".repeat(255), metadata.title)
        val snapshot = PhoneSnapshot(BatteryState(68, false), metadata, null, DndState(false), "normal")
        val encoded = MessageCodec.encodePhoneSnapshot(snapshot)
        assertTrue(MessageCodec.validateApplicationFrame(encoded.toByteArray()))
        assertEquals(snapshot, MessageCodec.decodePhoneSnapshot(encoded))
        assertEquals("a🎵", MediaMetadataNormalizer.normalize("a🎵tail", 3))
        assertEquals("a", MediaMetadataNormalizer.normalize("a🎵tail", 2))
    }

    private fun assertRejected(action: () -> Any?) {
        try { action(); fail("Expected invalid application input rejection") }
        catch (_: IllegalArgumentException) { }
    }
}
