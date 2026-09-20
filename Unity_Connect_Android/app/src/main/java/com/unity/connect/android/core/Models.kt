package com.unity.connect.android.core

import kotlinx.serialization.Serializable

@Serializable
data class BatteryState(
    val level: Int,
    val charging: Boolean
)

@Serializable
data class MediaCapabilities(
    val playPause: Boolean,
    val nextTrack: Boolean,
    val previousTrack: Boolean
)

typealias Capabilities = MediaCapabilities

@Serializable
data class MediaState(
    val source: String?,
    val title: String?,
    val artist: String?,
    val isPlaying: Boolean,
    val capabilities: MediaCapabilities
)

@Serializable
data class BrightnessState(
    val level: Int,
    val adaptive: Boolean,
    val canControl: Boolean,
    val ambientLux: Double?,
    val ambientStatus: String
)

@Serializable
data class AudioOutputState(
    val deviceName: String,
    val canRelease: Boolean
)

@Serializable
data class CellularState(
    val isUsingCellularData: Boolean,
    val network: String,
    val signal: String,
    val isUsingWifi: Boolean?
)

/**
 * [enabled] is Android's effective DND state. [companionRuleActive] is only the
 * condition requested by this app's AutomaticZenRule; they are deliberately
 * separate so disabling the companion rule never claims that global DND is off.
 */
@Serializable
data class DndState(
    val enabled: Boolean,
    val companionRuleActive: Boolean? = null,
    val canControlCompanionRule: Boolean = false
)

@Serializable
data class PhoneSnapshot(
    val battery: BatteryState?,
    val media: MediaState?,
    val cellular: CellularState?,
    val dnd: DndState?,
    val sound: String?,
    val brightness: BrightnessState? = null,
    val audioOutput: AudioOutputState? = null,
    val version: Int = 1,
    val type: String = "snapshot"
)

data class ClipboardContent(val updateId: String, val text: String)

@Serializable
data class BatteryMessage(val version: Int, val type: String, val level: Int, val charging: Boolean)

@Serializable
data class MediaMessage(val version: Int, val type: String, val state: MediaState?)

@Serializable
data class PcMediaMessage(
    val state: MediaState?,
    val version: Int = 1,
    val type: String = "pc_media"
)

@Serializable
data class CellularMessage(
    val version: Int,
    val type: String,
    val isUsingCellularData: Boolean,
    val network: String,
    val signal: String,
    val isUsingWifi: Boolean? = null
)

@Serializable
data class DndMessage(
    val version: Int,
    val type: String,
    val enabled: Boolean,
    val companionRuleActive: Boolean? = null,
    val canControlCompanionRule: Boolean = false
)

@Serializable
data class SoundMessage(val version: Int, val type: String, val mode: String)

@Serializable
data class MediaCommand(
    val version: Int,
    val type: String,
    val command: String,
    val command_id: String? = null
)

@Serializable
data class PcMediaCommand(
    val command: String,
    val version: Int = 1,
    val type: String = "pc_media_command"
)

@Serializable
data class CommandAck(val version: Int, val type: String, val command_id: String, val status: String)

sealed interface IncomingMessage {
    data class MediaCommand(val command: String) : IncomingMessage
    data class PcMediaUpdate(val state: MediaState?) : IncomingMessage
    data class BrightnessCommand(val level: Int?, val adaptive: Boolean?) : IncomingMessage
    data class DndRuleCommand(val active: Boolean) : IncomingMessage
    data object HeadphoneHandoff : IncomingMessage
    data object HotspotRequest : IncomingMessage
    data class AudioStreamStart(
        val streamId: String,
        val key: ByteArray,
        val token: ByteArray,
        val sampleRate: Int,
        val channels: Int
    ) : IncomingMessage
    data class AudioStreamStop(val streamId: String) : IncomingMessage
    data class ClipboardUpdate(val content: ClipboardContent) : IncomingMessage
}
