package com.unity.connect.android.core

import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.util.UUID
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/** Serialization, validation, and command decoding for v1 JSON frames. */
@OptIn(ExperimentalSerializationApi::class)
object MessageCodec {
    const val VERSION = 1
    const val MAX_FRAME_BYTES = 16 * 1024
    const val MAX_CLIPBOARD_TEXT_BYTES = 12 * 1024
    const val MAX_NESTING_DEPTH = 12

    val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = true
        isLenient = false
    }

    fun validateFrameSize(bytes: ByteArray) {
        require(bytes.isNotEmpty()) { "Frame is empty" }
        require(bytes.size <= MAX_FRAME_BYTES) { "Frame exceeds $MAX_FRAME_BYTES bytes" }
    }

    fun validateNestingDepth(jsonString: String, maxDepth: Int = MAX_NESTING_DEPTH) {
        var depth = 0
        var inString = false
        var escaped = false
        for (character in jsonString) {
            if (inString) {
                if (escaped) escaped = false
                else if (character == '\\') escaped = true
                else if (character == '"') inString = false
                continue
            }
            if (character == '"') inString = true
            else if (character == '{' || character == '[') {
                depth++
                require(depth <= maxDepth) { "JSON nesting exceeds $maxDepth" }
            } else if (character == '}' || character == ']') {
                depth--
                require(depth >= 0) { "Malformed JSON nesting" }
            }
        }
        require(depth == 0 && !inString) { "Malformed JSON nesting" }
    }

    inline fun <reified T> encode(payload: T): String = json.encodeToString(payload).also {
        validateFrameSize(it.toByteArray(StandardCharsets.UTF_8))
    }

    inline fun <reified T> decode(data: String): T {
        validateFrameSize(data.toByteArray(StandardCharsets.UTF_8))
        validateNestingDepth(data)
        return json.decodeFromString(data)
    }

    fun getMessageType(data: String): String? = parseObject(data)?.get("type")?.jsonPrimitive?.contentOrNull
    fun getVersion(data: String): Int? = parseObject(data)?.get("version")?.jsonPrimitive?.intOrNull

    fun encodePhoneSnapshot(snapshot: PhoneSnapshot): String = encode(snapshot)
    fun decodePhoneSnapshot(data: String): PhoneSnapshot = decode(data)
    fun encodeBatteryMessage(message: BatteryMessage): String = encode(message)
    fun encodeMediaMessage(message: MediaMessage): String = encode(message)
    fun encodeCellularMessage(message: CellularMessage): String = encode(message)
    fun encodeDndMessage(message: DndMessage): String = encode(message)
    fun encodeSoundMessage(message: SoundMessage): String = encode(message)
    fun encodeMediaCommand(command: MediaCommand): String = encode(command)
    fun decodeMediaCommand(data: String): MediaCommand = decode(data)
    fun encodeCommandAck(ack: CommandAck): String = encode(ack)
    fun decodeCommandAck(data: String): CommandAck = decode(data)

    fun encodeClipboard(content: ClipboardContent): String {
        require(isValidClipboard(content)) { "Invalid clipboard content" }
        return buildJsonObject {
            put("version", VERSION)
            put("type", "clipboard")
            put("updateId", content.updateId)
            put("text", content.text)
        }.toString().also { validateFrameSize(it.toByteArray(StandardCharsets.UTF_8)) }
    }

    fun decodeIncoming(frame: ByteArray): IncomingMessage? {
        val text = decodeUtf8(frame) ?: return null
        val root = parseObject(text) ?: return null
        if (root["version"]?.jsonPrimitive?.intOrNull != VERSION) return null
        return when (root["type"]?.jsonPrimitive?.contentOrNull) {
            "media_command" -> when (val command = root["command"]?.jsonPrimitive?.contentOrNull) {
                "play_pause", "next_track", "previous_track" -> IncomingMessage.MediaCommand(command)
                else -> null
            }
            "dnd_rule_command" -> root["active"]?.jsonPrimitive?.booleanOrNull?.let {
                IncomingMessage.DndRuleCommand(it)
            }
            "clipboard" -> decodeClipboard(root)?.let(IncomingMessage::ClipboardUpdate)
            else -> null
        }
    }

    private fun parseObject(data: String): JsonObject? = runCatching {
        validateFrameSize(data.toByteArray(StandardCharsets.UTF_8))
        validateNestingDepth(data)
        json.parseToJsonElement(data).jsonObject
    }.getOrNull()

    private fun decodeClipboard(root: JsonObject): ClipboardContent? {
        val updateId = root["updateId"]?.jsonPrimitive?.contentOrNull ?: return null
        val text = root["text"]?.jsonPrimitive?.contentOrNull ?: return null
        val canonicalId = runCatching { UUID.fromString(updateId).toString() }.getOrNull() ?: return null
        return ClipboardContent(canonicalId, text).takeIf(::isValidClipboard)
    }

    private fun isValidClipboard(content: ClipboardContent): Boolean =
        content.text.isNotEmpty() && '\u0000' !in content.text &&
            content.text.toByteArray(StandardCharsets.UTF_8).size <= MAX_CLIPBOARD_TEXT_BYTES

    private fun decodeUtf8(bytes: ByteArray): String? = runCatching {
        validateFrameSize(bytes)
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes)).toString()
    }.getOrNull()
}
