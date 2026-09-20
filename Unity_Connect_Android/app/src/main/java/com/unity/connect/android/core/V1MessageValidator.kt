package com.unity.connect.android.core

import java.util.Base64
import java.util.UUID
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.*

/** Strict application-v1 rules shared with the Windows contract, independent of Android APIs. */
internal object V1MessageValidator {
    fun parse(data: String): JsonObject {
        MessageCodec.validateFrameSize(data.toByteArray(Charsets.UTF_8))
        MessageCodec.validateNestingDepth(data)
        val root = MessageCodec.json.parseToJsonElement(data) as? JsonObject
            ?: throw IllegalArgumentException("Message must be an object")
        rejectDuplicateKeys(data)
        return root
    }

    fun validate(root: JsonObject) {
        require(integer(root, "version") == MessageCodec.VERSION) { "Unsupported version" }
        when (text(root, "type", 32, required = true)) {
            "battery" -> battery(root)
            "media" -> nullable(required(root, "state"), ::media)
            "cellular" -> cellular(root)
            "dnd" -> dnd(root)
            "sound_mode" -> sound(required(root, "mode"))
            "snapshot" -> snapshot(root)
            "media_command" -> require(text(root, "command", 32, true) in MEDIA_COMMANDS)
            "pc_media" -> nullable(required(root, "state"), ::media)
            "pc_media_command" -> require(text(root, "command", 32, true) in MEDIA_COMMANDS)
            "brightness_command" -> brightnessCommand(root)
            "dnd_rule_command" -> boolean(root, "active")
            "headphone_handoff" -> Unit
            "hotspot_request" -> Unit
            "audio_stream_start" -> audioStreamStart(root)
            "audio_stream_stop" -> streamId(root)
            "audio_sink_ready" -> audioSinkReady(root)
            "clipboard" -> clipboard(root)
            else -> throw IllegalArgumentException("Unknown application message")
        }
    }

    fun snapshot(root: JsonObject): PhoneSnapshot = PhoneSnapshot(
        battery = nullable(required(root, "battery")) { battery(objectValue(it)) },
        media = nullable(required(root, "media"), ::media),
        cellular = nullable(required(root, "cellular")) { cellular(objectValue(it)) },
        dnd = nullable(required(root, "dnd")) { dnd(objectValue(it)) },
        sound = nullable(required(root, "sound"), ::sound),
        brightness = root["brightness"]?.let { nullable(it, ::brightness) },
        audioOutput = root["audioOutput"]?.let { nullable(it, ::audioOutput) }
    )

    fun clipboard(root: JsonObject): ClipboardContent {
        val id = text(root, "updateId", 36, true)!!
        val canonical = UUID.fromString(id).toString()
        require(canonical.equals(id, ignoreCase = true)) { "Noncanonical clipboard ID" }
        val value = stringValue(required(root, "text"))
        require(value.isNotEmpty() && '\u0000' !in value &&
            value.toByteArray(Charsets.UTF_8).size <= MessageCodec.MAX_CLIPBOARD_TEXT_BYTES) {
            "Invalid clipboard text"
        }
        return ClipboardContent(canonical, value)
    }

    fun incoming(root: JsonObject): IncomingMessage? = when (text(root, "type", 32, true)) {
        "media_command" -> IncomingMessage.MediaCommand(text(root, "command", 32, true)!!)
        "pc_media" -> IncomingMessage.PcMediaUpdate(nullable(required(root, "state"), ::media))
        "brightness_command" -> brightnessCommand(root)
        "dnd_rule_command" -> IncomingMessage.DndRuleCommand(boolean(root, "active"))
        "headphone_handoff" -> IncomingMessage.HeadphoneHandoff
        "hotspot_request" -> IncomingMessage.HotspotRequest
        "audio_stream_start" -> audioStreamStart(root)
        "audio_stream_stop" -> IncomingMessage.AudioStreamStop(streamId(root))
        "clipboard" -> IncomingMessage.ClipboardUpdate(clipboard(root))
        else -> null // Valid phone-state messages are not commands for Android.
    }

    private fun battery(root: JsonObject): BatteryState {
        val level = integer(root, "level")
        require(level in 0..100) { "Invalid battery level" }
        return BatteryState(level, boolean(root, "charging"))
    }

    private fun media(value: JsonElement): MediaState {
        val root = objectValue(value)
        val capabilities = objectValue(required(root, "capabilities"))
        return MediaState(
            text(root, "source", 80), text(root, "title", 256), text(root, "artist", 256),
            boolean(root, "isPlaying"),
            MediaCapabilities(boolean(capabilities, "playPause"), boolean(capabilities, "nextTrack"),
                boolean(capabilities, "previousTrack"))
        )
    }

    private fun cellular(root: JsonObject): CellularState {
        val mobile = boolean(root, "isUsingCellularData")
        val wifi = nullableBoolean(root, "isUsingWifi")
        val network = text(root, "network", 16, true)!!
        val signal = text(root, "signal", 16, true)!!
        require(!(mobile && wifi == true)) { "Conflicting data routes" }
        require(network in NETWORKS && signal in SIGNALS) { "Invalid cellular category" }
        return CellularState(mobile, network, signal, wifi)
    }

    private fun dnd(root: JsonObject): DndState {
        val enabled = boolean(root, "enabled")
        val active = nullableBoolean(root, "companionRuleActive")
        val controllable = nullableBoolean(root, "canControlCompanionRule") ?: false
        require(!controllable || active != null) { "Missing companion rule state" }
        return DndState(enabled, active, controllable)
    }

    private fun brightness(value: JsonElement): BrightnessState {
        val root = objectValue(value)
        val level = integer(root, "level")
        val lux = nullableNumber(root, "ambientLux")
        val status = text(root, "ambientStatus", 16, true)!!
        require(level in 0..100 && (lux == null || lux.isFinite() && lux in 0.0..200000.0)) {
            "Invalid brightness state"
        }
        require(status in AMBIENT_STATES && ((status == "valid") == (lux != null))) {
            "Invalid ambient state"
        }
        return BrightnessState(level, boolean(root, "adaptive"), boolean(root, "canControl"), lux, status)
    }

    private fun audioOutput(value: JsonElement): AudioOutputState {
        val root = objectValue(value)
        return AudioOutputState(
            deviceName = text(root, "deviceName", 80, true)!!,
            canRelease = boolean(root, "canRelease")
        )
    }

    private fun brightnessCommand(root: JsonObject): IncomingMessage.BrightnessCommand {
        val level = nullableInteger(root, "level")
        val adaptive = nullableBoolean(root, "adaptive")
        require((level == null || level in 1..100) && (level != null || adaptive != null)) {
            "Invalid brightness command"
        }
        return IncomingMessage.BrightnessCommand(level, adaptive)
    }

    private fun audioStreamStart(root: JsonObject): IncomingMessage.AudioStreamStart {
        val key = Base64.getDecoder().decode(text(root, "key", 44, true)!!)
        val token = Base64.getDecoder().decode(text(root, "token", 24, true)!!)
        val sampleRate = integer(root, "sampleRate")
        val channels = integer(root, "channels")
        require(key.size == 32 && token.size == 16 && sampleRate in 8000..96000 && channels in 1..2) {
            "Invalid audio stream"
        }
        return IncomingMessage.AudioStreamStart(streamId(root), key, token, sampleRate, channels)
    }

    private fun audioSinkReady(root: JsonObject) {
        streamId(root)
        require(integer(root, "port") in 1..65535) { "Invalid audio sink port" }
    }

    private fun streamId(root: JsonObject): String {
        val value = text(root, "streamId", 36, true)!!
        require(UUID.fromString(value).toString().equals(value, ignoreCase = true)) { "Invalid stream ID" }
        return value.lowercase()
    }

    private fun sound(value: JsonElement): String = stringValue(value).also {
        require(it in SOUND_MODES) { "Invalid sound mode" }
    }

    private fun required(root: JsonObject, name: String): JsonElement =
        root[name] ?: throw IllegalArgumentException("Missing $name")

    private fun objectValue(value: JsonElement): JsonObject = value as? JsonObject
        ?: throw IllegalArgumentException("Expected object")

    private fun stringValue(value: JsonElement): String {
        val primitive = value as? JsonPrimitive
        require(primitive != null && primitive.isString) { "Expected string" }
        return primitive.content
    }

    private fun integer(root: JsonObject, name: String): Int {
        val primitive = required(root, name) as? JsonPrimitive
        require(primitive != null && !primitive.isString) { "Expected integer" }
        return primitive.intOrNull ?: throw IllegalArgumentException("Expected integer")
    }

    private fun boolean(root: JsonObject, name: String): Boolean {
        val primitive = required(root, name) as? JsonPrimitive
        require(primitive != null && !primitive.isString) { "Expected boolean" }
        return primitive.booleanOrNull ?: throw IllegalArgumentException("Expected boolean")
    }

    private fun nullableBoolean(root: JsonObject, name: String): Boolean? =
        if (root[name] == null || root[name] == JsonNull) null else boolean(root, name)

    private fun nullableInteger(root: JsonObject, name: String): Int? =
        if (root[name] == null || root[name] == JsonNull) null else integer(root, name)

    private fun nullableNumber(root: JsonObject, name: String): Double? {
        val primitive = root[name] as? JsonPrimitive ?: return null
        require(!primitive.isString) { "Expected number" }
        return primitive.doubleOrNull ?: throw IllegalArgumentException("Expected number")
    }

    private fun text(root: JsonObject, name: String, max: Int, required: Boolean = false): String? {
        val value = root[name]
        if (value == null || value == JsonNull) {
            require(!required) { "Missing $name" }
            return null
        }
        val string = stringValue(value)
        require(string.length <= max && string.none(Character::isISOControl)) { "Invalid $name" }
        require(!required || string.isNotBlank()) { "Missing $name" }
        return string.takeUnless(String::isBlank)
    }

    private fun <T> nullable(value: JsonElement, read: (JsonElement) -> T): T? =
        if (value == JsonNull) null else read(value)

    /**
     * kotlinx JSON maps overwrite duplicate keys. After standard syntax/depth validation,
     * inspect original string tokens to reject duplicates in every object, including unknown
     * fields/arrays. Decode key escapes with the library so "active" and "\\u0061ctive" match.
     * This guard does not implement a second JSON grammar or interpret value tokens.
     */
    private fun rejectDuplicateKeys(data: String) {
        val scopes = mutableListOf<MutableSet<String>?>()
        var offset = 0
        while (offset < data.length) {
            when (data[offset]) {
                '{' -> { scopes.add(mutableSetOf()); offset++ }
                '[' -> { scopes.add(null); offset++ }
                '}', ']' -> { scopes.removeAt(scopes.lastIndex); offset++ }
                '"' -> {
                    val start = offset++
                    while (data[offset] != '"') {
                        offset += if (data[offset] == '\\') 2 else 1
                    }
                    offset++
                    var next = offset
                    while (next < data.length && data[next].isWhitespace()) next++
                    if (next < data.length && data[next] == ':') {
                        val key = MessageCodec.json.decodeFromString<String>(data.substring(start, offset))
                        require(scopes.last()!!.add(key)) { "Duplicate JSON field" }
                    }
                }
                else -> offset++
            }
        }
    }

    private val MEDIA_COMMANDS = setOf("play_pause", "next_track", "previous_track")
    private val NETWORKS = setOf("unknown", "cellular", "2g", "3g", "4g", "5g")
    private val SIGNALS = setOf("unknown", "none", "poor", "fair", "good", "excellent")
    private val SOUND_MODES = setOf("normal", "vibrate", "silent")
    private val AMBIENT_STATES = setOf("valid", "covered", "unavailable")
}
