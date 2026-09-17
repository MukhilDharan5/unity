package com.unity.connect.android.core

import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
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
        validateJsonStructure(data)
        return json.decodeFromString(data)
    }

    /** Structural helper for legacy typed serializers, including unused acknowledgment models. */
    fun validateJsonStructure(data: String) { V1MessageValidator.parse(data) }

    fun getMessageType(data: String): String? = runCatching {
        val value = V1MessageValidator.parse(data)["type"] as? JsonPrimitive
        value?.takeIf { it.isString }?.content
    }.getOrNull()
    fun getVersion(data: String): Int? = runCatching {
        val value = V1MessageValidator.parse(data)["version"] as? JsonPrimitive
        value?.takeUnless { it.isString }?.intOrNull
    }.getOrNull()

    private inline fun <reified T> encodeApplication(payload: T): String = encode(payload).also {
        V1MessageValidator.validate(V1MessageValidator.parse(it))
    }

    fun encodePhoneSnapshot(snapshot: PhoneSnapshot): String = encodeApplication(snapshot)
    fun decodePhoneSnapshot(data: String): PhoneSnapshot {
        val root = V1MessageValidator.parse(data)
        V1MessageValidator.validate(root)
        require((root["type"] as? JsonPrimitive)?.content == "snapshot") { "Expected snapshot" }
        return V1MessageValidator.snapshot(root)
    }
    fun encodeBatteryMessage(message: BatteryMessage): String = encodeApplication(message)
    fun encodeMediaMessage(message: MediaMessage): String = encodeApplication(message)
    fun encodeCellularMessage(message: CellularMessage): String = encodeApplication(message)
    fun encodeDndMessage(message: DndMessage): String = encodeApplication(message)
    fun encodeSoundMessage(message: SoundMessage): String = encodeApplication(message)
    fun encodeMediaCommand(command: MediaCommand): String = encodeApplication(command)
    fun decodeMediaCommand(data: String): MediaCommand {
        val root = V1MessageValidator.parse(data)
        V1MessageValidator.validate(root)
        require((root["type"] as? JsonPrimitive)?.content == "media_command") { "Expected media command" }
        return decode(data)
    }
    fun encodeCommandAck(ack: CommandAck): String = encode(ack)
    fun decodeCommandAck(data: String): CommandAck = decode(data)

    fun encodeClipboard(content: ClipboardContent): String {
        return buildJsonObject {
            put("version", VERSION)
            put("type", "clipboard")
            put("updateId", content.updateId)
            put("text", content.text)
        }.toString().also { V1MessageValidator.validate(V1MessageValidator.parse(it)) }
    }

    /** Reject malformed/unexpected application input without unwinding the connection coroutine. */
    fun decodeIncoming(frame: ByteArray): IncomingMessage? = runCatching {
        val text = decodeUtf8(frame) ?: return null
        val root = V1MessageValidator.parse(text)
        V1MessageValidator.validate(root)
        V1MessageValidator.incoming(root)
    }.getOrNull()

    fun validateApplicationFrame(frame: ByteArray): Boolean = runCatching {
        val text = decodeUtf8(frame) ?: return false
        V1MessageValidator.validate(V1MessageValidator.parse(text))
        true
    }.getOrDefault(false)

    private fun decodeUtf8(bytes: ByteArray): String? = runCatching {
        validateFrameSize(bytes)
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes)).toString()
    }.getOrNull()
}
