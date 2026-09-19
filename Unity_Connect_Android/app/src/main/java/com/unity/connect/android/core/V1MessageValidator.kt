package com.unity.connect.android.core

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
            "dnd_rule_command" -> boolean(root, "active")
            "clipboard" -> clipboard(root)
            else -> throw IllegalArgumentException("Unknown application message")
        }
    }

    fun snapshot(root: JsonObject): PhoneSnapshot = PhoneSnapshot(
        battery = nullable(required(root, "battery")) { battery(objectValue(it)) },
        media = nullable(required(root, "media"), ::media),
        cellular = nullable(required(root, "cellular")) { cellular(objectValue(it)) },
        dnd = nullable(required(root, "dnd")) { dnd(objectValue(it)) },
        sound = nullable(required(root, "sound"), ::sound)
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
        "dnd_rule_command" -> IncomingMessage.DndRuleCommand(boolean(root, "active"))
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
}
