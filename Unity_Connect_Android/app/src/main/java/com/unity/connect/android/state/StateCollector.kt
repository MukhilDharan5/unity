package com.unity.connect.android.state

import android.Manifest
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.media.AudioManager
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.BatteryManager
import android.telephony.SignalStrength
import android.telephony.TelephonyCallback
import android.telephony.TelephonyDisplayInfo
import android.telephony.TelephonyManager
import androidx.core.content.ContextCompat
import com.unity.connect.android.core.BatteryState
import com.unity.connect.android.core.CellularState
import com.unity.connect.android.core.MediaCapabilities
import com.unity.connect.android.core.MediaState
import com.unity.connect.android.core.MediaMetadataNormalizer
import com.unity.connect.android.core.PhoneSnapshot
import com.unity.connect.android.dnd.CompanionDndController
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged

class StateCollector(
    private val context: Context,
    dndController: CompanionDndController,
    brightnessController: BrightnessController
) {
    private var currentController: MediaController? = null

    val phoneState: Flow<PhoneSnapshot> = combine(
        batteryFlow(),
        cellularFlow(),
        mediaFlow(),
        dndController.state,
        soundFlow()
    ) { battery, cellular, media, dnd, sound ->
        PhoneSnapshot(battery, media, cellular, dnd, sound)
    }.combine(brightnessController.state) { snapshot, brightness ->
        snapshot.copy(brightness = brightness)
    }

    fun dispatchMediaCommand(command: String): Boolean {
        val controller = currentController ?: return false
        val controls = controller.transportControls
        return runCatching {
            when (command) {
                "play_pause" -> if (controller.playbackState?.state == PlaybackState.STATE_PLAYING) {
                    controls.pause()
                } else {
                    controls.play()
                }
                "next_track" -> controls.skipToNext()
                "previous_track" -> controls.skipToPrevious()
                else -> return false
            }
            true
        }.getOrDefault(false)
    }

    private fun batteryFlow(): Flow<BatteryState?> = callbackFlow {
        fun read(intent: Intent?): BatteryState? {
            if (intent == null) return null
            val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
            if (level < 0 || scale <= 0) return null
            val status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, BatteryManager.BATTERY_STATUS_UNKNOWN)
            return BatteryState(
                level = (level * 100 / scale).coerceIn(0, 100),
                charging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
            )
        }

        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context?, intent: Intent?) {
                trySend(read(intent))
            }
        }
        trySend(read(context.registerReceiver(receiver, IntentFilter(Intent.ACTION_BATTERY_CHANGED))))
        awaitClose { runCatching { context.unregisterReceiver(receiver) } }
    }.distinctUntilChanged()

    private fun cellularFlow(): Flow<CellularState?> = callbackFlow {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        val telephony = context.getSystemService(TelephonyManager::class.java)
        val canReadPhoneState = ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.READ_PHONE_STATE
        ) == PackageManager.PERMISSION_GRANTED
        val stateLock = Any()
        var networkType = if (canReadPhoneState) {
            runCatching { telephony.dataNetworkType }.getOrDefault(TelephonyManager.NETWORK_TYPE_UNKNOWN)
        } else {
            TelephonyManager.NETWORK_TYPE_UNKNOWN
        }
        var displayOverride = TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NONE
        var signalLevel = if (canReadPhoneState) runCatching { telephony.signalStrength?.level }.getOrNull() else null

        fun sendState() {
            try {
                val capabilities = connectivity.getNetworkCapabilities(connectivity.activeNetwork)
                val cellularData = capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) == true
                val wifi = capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true
                val radio = synchronized(stateLock) { Triple(networkType, displayOverride, signalLevel) }
                trySend(
                    CellularState(
                        isUsingCellularData = cellularData,
                        isUsingWifi = wifi,
                        network = networkGeneration(radio.first, radio.second),
                        signal = signalLevel(radio.third)
                    )
                )
            } catch (_: SecurityException) {
                trySend(null)
            } catch (_: Exception) {
                trySend(null)
            }
        }

        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onCapabilitiesChanged(network: Network, networkCapabilities: NetworkCapabilities) = sendState()
            override fun onAvailable(network: Network) = sendState()
            override fun onLost(network: Network) = sendState()
        }
        val telephonyCallback = object : TelephonyCallback(),
            TelephonyCallback.DataConnectionStateListener,
            TelephonyCallback.DisplayInfoListener,
            TelephonyCallback.SignalStrengthsListener {
            override fun onDataConnectionStateChanged(state: Int, updatedNetworkType: Int) {
                synchronized(stateLock) { networkType = updatedNetworkType }
                sendState()
            }

            override fun onDisplayInfoChanged(info: TelephonyDisplayInfo) {
                synchronized(stateLock) { displayOverride = info.overrideNetworkType }
                sendState()
            }

            override fun onSignalStrengthsChanged(signalStrength: SignalStrength) {
                synchronized(stateLock) { signalLevel = signalStrength.level }
                sendState()
            }
        }
        var networkCallbackRegistered = false
        var telephonyCallbackRegistered = false
        runCatching {
            connectivity.registerDefaultNetworkCallback(callback)
            networkCallbackRegistered = true
        }.onFailure { trySend(null) }
        if (canReadPhoneState) {
            runCatching {
                telephony.registerTelephonyCallback(context.mainExecutor, telephonyCallback)
                telephonyCallbackRegistered = true
            }
        }
        sendState()
        awaitClose {
            if (networkCallbackRegistered) runCatching { connectivity.unregisterNetworkCallback(callback) }
            if (telephonyCallbackRegistered) runCatching { telephony.unregisterTelephonyCallback(telephonyCallback) }
        }
    }.distinctUntilChanged()

    private fun networkGeneration(networkType: Int, displayOverride: Int): String {
        if (displayOverride == TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_NSA ||
            displayOverride == TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_ADVANCED) return "5g"
        return when (networkType) {
            TelephonyManager.NETWORK_TYPE_NR -> "5g"
            TelephonyManager.NETWORK_TYPE_LTE -> "4g"
            TelephonyManager.NETWORK_TYPE_UMTS,
            TelephonyManager.NETWORK_TYPE_HSDPA,
            TelephonyManager.NETWORK_TYPE_HSUPA,
            TelephonyManager.NETWORK_TYPE_HSPA,
            TelephonyManager.NETWORK_TYPE_HSPAP -> "3g"
            TelephonyManager.NETWORK_TYPE_GPRS,
            TelephonyManager.NETWORK_TYPE_EDGE,
            TelephonyManager.NETWORK_TYPE_CDMA,
            TelephonyManager.NETWORK_TYPE_1xRTT,
            TelephonyManager.NETWORK_TYPE_IDEN -> "2g"
            else -> "unknown"
        }
    }

    private fun signalLevel(level: Int?): String = when (level) {
        0 -> "none"
        1 -> "poor"
        2 -> "fair"
        3 -> "good"
        4 -> "excellent"
        else -> "unknown"
    }

    private fun mediaFlow(): Flow<MediaState?> = callbackFlow {
        val sessions = context.getSystemService(MediaSessionManager::class.java)
        val listenerComponent = ComponentName(context, MediaSessionAccessService::class.java)

        fun relevantController(controllers: List<MediaController>): MediaController? =
            controllers.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING }
                ?: controllers.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PAUSED }

        fun readableSource(controller: MediaController): String? = runCatching {
            val info = context.packageManager.getApplicationInfo(controller.packageName, 0)
            context.packageManager.getApplicationLabel(info).toString()
        }.getOrNull()

        fun sendState() {
            try {
                val controllers = sessions.getActiveSessions(listenerComponent)
                val selected = relevantController(controllers)
                if (selected !== currentController) {
                    currentController?.unregisterCallback(mediaCallback)
                    currentController = selected
                    currentController?.registerCallback(mediaCallback)
                }
                if (selected == null) {
                    trySend(null)
                    return
                }
                val playback = selected.playbackState
                val metadata = selected.metadata
                val actions = playback?.actions ?: 0L
                trySend(
                    MediaState(
                        source = MediaMetadataNormalizer.normalize(readableSource(selected), MediaMetadataNormalizer.MAX_SOURCE_UNITS),
                        title = MediaMetadataNormalizer.normalize(metadata?.getString(MediaMetadata.METADATA_KEY_TITLE)),
                        artist = MediaMetadataNormalizer.normalize(metadata?.getString(MediaMetadata.METADATA_KEY_ARTIST)
                            ?: metadata?.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST)),
                        isPlaying = playback?.state == PlaybackState.STATE_PLAYING,
                        capabilities = MediaCapabilities(
                            playPause = actions and (PlaybackState.ACTION_PLAY_PAUSE or PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE) != 0L,
                            nextTrack = actions and PlaybackState.ACTION_SKIP_TO_NEXT != 0L,
                            previousTrack = actions and PlaybackState.ACTION_SKIP_TO_PREVIOUS != 0L
                        )
                    )
                )
            } catch (_: SecurityException) {
                currentController = null
                trySend(null)
            } catch (_: Exception) {
                currentController = null
                trySend(null)
            }
        }

        val activeSessionsListener = MediaSessionManager.OnActiveSessionsChangedListener { sendState() }
        mediaCallback = object : MediaController.Callback() {
            override fun onPlaybackStateChanged(state: PlaybackState?) = sendState()
            override fun onMetadataChanged(metadata: MediaMetadata?) = sendState()
            override fun onSessionDestroyed() = sendState()
        }

        runCatching {
            sessions.addOnActiveSessionsChangedListener(activeSessionsListener, listenerComponent)
            sendState()
        }.onFailure { trySend(null) }

        awaitClose {
            runCatching { sessions.removeOnActiveSessionsChangedListener(activeSessionsListener) }
            runCatching { currentController?.unregisterCallback(mediaCallback) }
            currentController = null
        }
    }.distinctUntilChanged()

    private lateinit var mediaCallback: MediaController.Callback

    private fun soundFlow(): Flow<String?> = callbackFlow {
        val audio = context.getSystemService(AudioManager::class.java)
        fun current(): String? = runCatching {
            when (audio.ringerMode) {
                AudioManager.RINGER_MODE_NORMAL -> "normal"
                AudioManager.RINGER_MODE_VIBRATE -> "vibrate"
                AudioManager.RINGER_MODE_SILENT -> "silent"
                else -> null
            }
        }.getOrNull()

        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context?, intent: Intent?) { trySend(current()) }
        }
        context.registerReceiver(receiver, IntentFilter(AudioManager.RINGER_MODE_CHANGED_ACTION))
        trySend(current())
        awaitClose { runCatching { context.unregisterReceiver(receiver) } }
    }.distinctUntilChanged()
}
