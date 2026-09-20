package com.unity.connect.android

import android.Manifest
import android.app.*
import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.PermissionChecker
import androidx.core.content.ContextCompat
import com.unity.connect.android.ble.BleManager
import com.unity.connect.android.clipboard.ClipboardBridge
import com.unity.connect.android.core.*
import com.unity.connect.android.dnd.CompanionDndController
import com.unity.connect.android.lan.LanServer
import com.unity.connect.android.state.StateCollector
import com.unity.connect.android.state.BrightnessController
import com.unity.connect.android.state.BluetoothAudioMonitor
import com.unity.connect.android.state.HeadphoneReleaseResult
import com.unity.connect.android.state.HotspotSettings
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import kotlinx.serialization.json.*
import java.net.Inet4Address
import java.net.NetworkInterface

enum class ConnectionState { DISCONNECTED, CONNECTING, CONNECTED }
data class AccessState(
    val nearbyDevices: Boolean = false,
    val bluetoothEnabled: Boolean = false,
    val notifications: Boolean = false,
    val phoneState: Boolean = false,
    val mediaSessions: Boolean = false,
    val dndPolicy: Boolean = false,
    val brightnessControl: Boolean = false
)
data class UiState(
    val isPaired: Boolean = false,
    val connectionState: ConnectionState = ConnectionState.DISCONNECTED,
    val sasCode: String? = null,
    val pairedPcName: String? = null,
    val canControlCompanionDnd: Boolean = false,
    val companionDndActive: Boolean = false,
    val clipboardSyncEnabled: Boolean = false,
    val featureNotice: String? = null,
    val pcMedia: MediaState? = null,
    val bluetoothAudioName: String? = null,
    val bluetoothAudioCanRelease: Boolean = false,
    val wifiAddress: String? = null,
    val awaitingOtherDevice: Boolean = false,
    val access: AccessState = AccessState()
)

class ConnectionService : Service() {
    companion object {
        private val state = MutableStateFlow(UiState())
        val serviceState: StateFlow<UiState> = state.asStateFlow()
        private var instance: ConnectionService? = null
        fun startPairing() { instance?.startTransports(forceRestart = true) }
        fun acceptSasCode() { instance?.consent?.complete(true) }
        fun rejectPairing() { instance?.consent?.complete(false) }
        fun forgetDevice() { instance?.forget() }
        fun setClipboardSyncEnabled(enabled: Boolean) { instance?.apply {
            clipboard.updateEnabled(enabled); state.update { it.copy(clipboardSyncEnabled = enabled) }
        } }
        fun sendCurrentClipboard() { instance?.sendClipboard() }
        fun sendPcMediaCommand(command: String) { instance?.sendPcMediaCommand(command) }
        fun setCompanionDndActive(active: Boolean) { instance?.dnd?.setCompanionRuleActive(active) }
        fun refreshDndState() { instance?.apply {
            dnd.refresh(); bluetoothAudio.refresh(); refreshAccessState(); refreshAddress(); restartCollector()
            if (listenersWanted()) { ble.start(); lan.start() }
        } }
        fun associateCurrentAudioDevice(onPending: (android.content.IntentSender) -> Unit) {
            instance?.bluetoothAudio?.requestAssociation(onPending) { message ->
                state.update { it.copy(featureNotice = message) }
            }
        }
    }
    private val scope = CoroutineScope(Dispatchers.Main.immediate + SupervisorJob())
    private lateinit var ble: BleManager
    private lateinit var lan: LanServer
    private lateinit var dnd: CompanionDndController
    private lateinit var clipboard: ClipboardBridge
    private lateinit var collector: StateCollector
    private lateinit var brightness: BrightnessController
    private lateinit var bluetoothAudio: BluetoothAudioMonitor
    private val prefs by lazy { getSharedPreferences("secure_peers_v1", MODE_PRIVATE) }
    private val identity by lazy { IdentityStore.load() }
    private val owner = PhoneSessionOwner(scope) { scope.launch { onSessionsChanged() } }
    private var consent: CompletableDeferred<Boolean>? = null
    private var pairingUntil = 0L
    @Volatile private var port = 0
    private lateinit var collection: PhoneStateCollection
    private val latest get() = collection.latest
    private var pairingExpiry: Job? = null
    private var bleRetry: Job? = null
    private var lanRetry: Job? = null
    private var bleRetryAttempt = 0
    private var lanRetryAttempt = 0
    private var pcMediaCommand: Job? = null
    @Volatile private var bleEpoch = 0L
    @Volatile private var lanEpoch = 0L
    private var networkCallbackRegistered = false
    private var bluetoothReceiverRegistered = false
    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) = networkChanged()
        override fun onLost(network: Network) = networkChanged()
        override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) = networkChanged()
    }
    private val bluetoothReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != BluetoothAdapter.ACTION_STATE_CHANGED) return
            val enabled = intent.getIntExtra(BluetoothAdapter.EXTRA_STATE, BluetoothAdapter.ERROR) == BluetoothAdapter.STATE_ON
            scope.launch {
                refreshAccessState()
                if (enabled && listenersWanted()) {
                    bleRetry?.cancel(); bleRetry = null
                    bleRetryAttempt = 0
                    bleEpoch++
                    ble.stop()
                    ble.start()
                } else if (!enabled) {
                    bleEpoch++
                    bleRetry?.cancel(); bleRetry = null
                    ble.stop()
                }
            }
        }
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
        dnd = CompanionDndController(this)
        brightness = BrightnessController(this)
        bluetoothAudio = BluetoothAudioMonitor(this)
        clipboard = ClipboardBridge(this)
        collector = StateCollector(this, dnd, brightness, bluetoothAudio)
        collection = PhoneStateCollection(scope, owner, collector.phoneState)
        state.value = UiState(isPaired = prefs.contains("identity"), pairedPcName = prefs.getString("name", null),
            clipboardSyncEnabled = clipboard.enabled, access = readAccessState())
        dnd.state.onEach { value -> state.update { it.copy(canControlCompanionDnd = value?.canControlCompanionRule == true,
            companionDndActive = value?.companionRuleActive == true) } }.launchIn(scope)
        bluetoothAudio.state.onEach { value ->
            state.update { it.copy(
                bluetoothAudioName = value?.deviceName,
                bluetoothAudioCanRelease = value?.canRelease == true
            ) }
        }.launchIn(scope)
        ble = BleManager(this, { pipe -> owner.accept("ble", pipe, ::accept) },
            { text -> listenerError("ble", bleEpoch, text) })
        lan = LanServer(this, { pipe -> owner.accept("wifi", pipe, ::accept) },
            { value -> port = value; refreshAddress() },
            { text -> listenerError("wifi", lanEpoch, text) })
        registerRuntimeSignals()
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel("unity_service", "Phone connection", NotificationManager.IMPORTANCE_LOW))
        startForeground(1, notification("Ready to connect"))
        if (prefs.contains("identity")) startTransports()
        return START_STICKY
    }
    private fun startTransports(forceRestart: Boolean = false) {
        if (forceRestart) {
            cancelListenerRetries(resetAttempts = true)
            bleEpoch++; lanEpoch++
            ble.stop(); lan.stop(); port = 0
        }
        if (!prefs.contains("identity")) {
            pairingUntil = android.os.SystemClock.elapsedRealtime() + ConnectionPolicy.pairingWindowMs
            schedulePairingExpiry()
        }
        owner.enable()
        ble.start(); lan.start(); refreshAddress()
        state.update { it.copy(connectionState = if (!owner.hasSessions) ConnectionState.CONNECTING else ConnectionState.CONNECTED,
            featureNotice = if (!it.isPaired) "Open Connect phone on your laptop. Pairing is available for two minutes." else null) }
    }
    private fun listenersWanted(): Boolean = scope.isActive &&
        (prefs.contains("identity") || android.os.SystemClock.elapsedRealtime() <= pairingUntil)

    private fun schedulePairingExpiry() {
        pairingExpiry?.cancel()
        val deadline = pairingUntil
        pairingExpiry = scope.launch {
            delay((deadline - android.os.SystemClock.elapsedRealtime()).coerceAtLeast(0))
            if (prefs.contains("identity") || pairingUntil != deadline) return@launch
            pairingUntil = 0
            consent?.complete(false); consent = null
            owner.revoke()
            stopListeners()
            stopCollector()
            state.update { it.copy(connectionState = ConnectionState.DISCONNECTED, sasCode = null,
                awaitingOtherDevice = false, wifiAddress = null,
                featureNotice = "Pairing timed out. Tap Start pairing when your laptop is ready.") }
            notifyState()
        }
    }

    private fun listenerError(kind: String, epoch: Long, text: String) {
        scope.launch {
            val current = if (kind == "ble") bleEpoch else lanEpoch
            if (epoch != current || !listenersWanted()) return@launch
            state.update { it.copy(featureNotice = text) }
            scheduleListenerRetry(kind, epoch)
        }
    }

    private fun scheduleListenerRetry(kind: String, epoch: Long) {
        val pauses = ConnectionPolicy.listenerRetryMs
        if (kind == "ble") {
            bleRetry?.cancel()
            val wait = pauses[minOf(bleRetryAttempt++, pauses.lastIndex)]
            bleRetry = scope.launch {
                delay(wait)
                if (epoch != bleEpoch || !listenersWanted()) return@launch
                bleRetry = null; bleEpoch++
                ble.stop(); ble.start()
            }
        } else {
            lanRetry?.cancel()
            val wait = pauses[minOf(lanRetryAttempt++, pauses.lastIndex)]
            lanRetry = scope.launch {
                delay(wait)
                if (epoch != lanEpoch || !listenersWanted()) return@launch
                lanRetry = null; lanEpoch++
                lan.stop(); lan.start()
            }
        }
    }

    private fun cancelListenerRetries(resetAttempts: Boolean) {
        bleRetry?.cancel(); bleRetry = null
        lanRetry?.cancel(); lanRetry = null
        if (resetAttempts) { bleRetryAttempt = 0; lanRetryAttempt = 0 }
    }

    private fun stopListeners() {
        cancelListenerRetries(resetAttempts = true)
        bleEpoch++; lanEpoch++
        ble.stop(); lan.stop(); port = 0
    }

    private fun networkChanged() {
        scope.launch {
            refreshAddress()
            if (listenersWanted()) {
                if (lanRetry != null) {
                    lanRetry?.cancel(); lanRetry = null; lanRetryAttempt = 0
                    lanEpoch++
                    lan.stop()
                }
                lan.start()
            }
        }
    }

    private fun registerRuntimeSignals() {
        networkCallbackRegistered = runCatching {
            getSystemService(ConnectivityManager::class.java).registerDefaultNetworkCallback(networkCallback)
            true
        }.getOrDefault(false)
        bluetoothReceiverRegistered = runCatching {
            ContextCompat.registerReceiver(this, bluetoothReceiver,
                IntentFilter(BluetoothAdapter.ACTION_STATE_CHANGED), ContextCompat.RECEIVER_EXPORTED)
            true
        }.getOrDefault(false)
    }

    private fun unregisterRuntimeSignals() {
        if (networkCallbackRegistered) runCatching {
            getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(networkCallback)
        }
        if (bluetoothReceiverRegistered) runCatching { unregisterReceiver(bluetoothReceiver) }
        networkCallbackRegistered = false; bluetoothReceiverRegistered = false
    }
    private fun refreshAddress() {
        val network = getSystemService(ConnectivityManager::class.java)
        val wifiNetwork = network.activeNetwork?.takeIf { candidate ->
            network.getNetworkCapabilities(candidate)?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI) == true
        }
        val wifiAddress = wifiNetwork?.let(network::getLinkProperties)?.linkAddresses
            ?.firstOrNull { it.address is Inet4Address && !it.address.isLoopbackAddress }?.address?.hostAddress
        // Hotspot interfaces may not be Android's active/default network.
        val hotspotAddress = runCatching {
            NetworkInterface.getNetworkInterfaces().toList()
                .filter { it.isUp && !it.isLoopback && (it.name.startsWith("wlan") || it.name.startsWith("ap")) }
                .flatMap { it.inetAddresses.toList() }
                .firstOrNull { it is Inet4Address && !it.isLoopbackAddress }?.hostAddress
        }.getOrNull()
        val address = wifiAddress ?: hotspotAddress
        state.update { it.copy(wifiAddress = if (address != null && port > 0) "$address:$port" else null) }
    }
    private fun refreshAccessState() = state.update { it.copy(access = readAccessState()) }
    private fun readAccessState(): AccessState {
        fun granted(permission: String) = PermissionChecker.checkSelfPermission(this, permission) ==
            PermissionChecker.PERMISSION_GRANTED
        return AccessState(
            nearbyDevices = granted(Manifest.permission.BLUETOOTH_CONNECT) &&
                granted(Manifest.permission.BLUETOOTH_ADVERTISE),
            bluetoothEnabled = runCatching {
                getSystemService(android.bluetooth.BluetoothManager::class.java).adapter?.isEnabled == true
            }.getOrDefault(false),
            notifications = Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
                granted(Manifest.permission.POST_NOTIFICATIONS),
            phoneState = granted(Manifest.permission.READ_PHONE_STATE),
            mediaSessions = NotificationManagerCompat.getEnabledListenerPackages(this).contains(packageName),
            dndPolicy = getSystemService(NotificationManager::class.java).isNotificationPolicyAccessGranted,
            brightnessControl = android.provider.Settings.System.canWrite(this)
        )
    }
    @SuppressLint("ApplySharedPref") // Trust must reach disk before the UI exposes the paired session.
    private suspend fun accept(lease: PhoneSessionOwner.Lease) {
        val pipe = lease.pipe
        val sessionScope = CoroutineScope(currentCoroutineContext())
        var secure: SecureSession? = null
        var watchdog: Job? = null
        var heartbeat: Job? = null
        try {
            // Closing the socket also interrupts blocking JVM reads when the coroutine times out.
            val handshakeGuard = sessionScope.launch { delay(ConnectionPolicy.handshakeTimeoutMs); pipe.close() }
            try {
                secure = withTimeout(ConnectionPolicy.handshakeTimeoutMs) {
                    SecureSession.connect(pipe, identity, Build.MODEL) { peer ->
                        currentCoroutineContext().ensureActive()
                        check(owner.isCurrent(lease))
                        val known = prefs.getString("identity", null)
                        if (known != null) known == peer.publicKey
                        else if (android.os.SystemClock.elapsedRealtime() > pairingUntil || consent != null) false
                        else {
                            val choice = CompletableDeferred<Boolean>(); consent = choice
                            state.update { it.copy(sasCode = peer.code, pairedPcName = peer.name, awaitingOtherDevice = false) }
                            try {
                                val accepted = choice.await()
                                currentCoroutineContext().ensureActive()
                                check(owner.isCurrent(lease))
                                state.update { it.copy(awaitingOtherDevice = accepted) }
                                accepted
                            } finally { if (consent === choice) consent = null }
                        }
                    }
                }
            } finally { handshakeGuard.cancel() }
            val session = secure!!
            currentCoroutineContext().ensureActive()
            // Persist trust only after both devices confirm and complete the encrypted hello.
            if (!owner.publish(lease, session) {
                check(prefs.getString("identity", session.peer.publicKey) == session.peer.publicKey)
                prefs.edit().putString("identity", session.peer.publicKey).putString("name", session.peer.name).commit()
            }) return
            pairingExpiry?.cancel(); pairingExpiry = null; pairingUntil = 0
            cancelListenerRetries(resetAttempts = true)
            state.update { it.copy(isPaired = true, connectionState = ConnectionState.CONNECTED, sasCode = null,
                pairedPcName = session.peer.name, awaitingOtherDevice = false, featureNotice = null) }
            notifyState()
            if (!collection.isCollecting) restartCollector()
            latest?.let { if (owner.isActive(lease)) session.send(MessageCodec.encodePhoneSnapshot(it).toByteArray(Charsets.UTF_8)) }
            var lastReceived = android.os.SystemClock.elapsedRealtime()
            watchdog = sessionScope.launch {
                while (isActive) {
                    delay(ConnectionPolicy.watchdogPollMs)
                    if (android.os.SystemClock.elapsedRealtime() - lastReceived > ConnectionPolicy.peerTimeoutMs) {
                        pipe.close(); break
                    }
                }
            }
            heartbeat = sessionScope.launch {
                try { while (isActive) { delay(ConnectionPolicy.heartbeatMs); session.send(SecureSession.control("ping")) } }
                catch (_: Exception) { pipe.close() }
            }
            while (currentCoroutineContext().isActive && owner.isCurrent(lease)) {
                val payload = session.receive(); lastReceived = android.os.SystemClock.elapsedRealtime()
                currentCoroutineContext().ensureActive()
                if (!owner.isCurrent(lease)) break
                val root = SecureSession.parse(payload)
                require(root["version"]?.jsonPrimitive?.int == 1)
                when (root["type"]?.jsonPrimitive?.content) {
                    "ping" -> session.send(SecureSession.control("pong"))
                    "pong" -> Unit
                    "request_snapshot" -> latest?.let { session.send(MessageCodec.encodePhoneSnapshot(it).toByteArray(Charsets.UTF_8)) }
                    else -> if (owner.isActive(lease)) handleIncoming(payload)
                }
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            if (owner.isCurrent(lease) && !owner.hasSessions) state.update {
                it.copy(featureNotice = "Connection ended. Open Connect phone on your laptop to try again.")
            }
        } finally {
            watchdog?.cancel(); heartbeat?.cancel()
            secure?.close()
        }
    }
    private suspend fun onSessionsChanged() {
        if (!scope.isActive) return
        state.update { it.copy(connectionState = if (owner.hasSessions) ConnectionState.CONNECTED else ConnectionState.DISCONNECTED,
            sasCode = if (consent == null) null else it.sasCode,
            awaitingOtherDevice = if (consent == null) false else it.awaitingOtherDevice,
            pcMedia = if (owner.hasSessions) it.pcMedia else null) }
        if (!owner.hasSessions) stopCollector()
        else latest?.let { snapshot -> sendActive(MessageCodec.encodePhoneSnapshot(snapshot).toByteArray(Charsets.UTF_8)) }
        notifyState()
    }
    private fun stopCollector() = collection.stop()
    private fun restartCollector() = collection.restart()
    private suspend fun sendActive(frame: ByteArray) = owner.sendActive(frame)
    private fun handleIncoming(payload: ByteArray) {
        when (val message = MessageCodec.decodeIncoming(payload)) {
            is IncomingMessage.MediaCommand -> collector.dispatchMediaCommand(message.command)
            is IncomingMessage.PcMediaUpdate -> state.update { it.copy(pcMedia = message.state) }
            is IncomingMessage.BrightnessCommand -> if (!brightness.apply(message.level, message.adaptive)) state.update {
                it.copy(featureNotice = "Allow phone brightness control in Android settings first.")
            }
            is IncomingMessage.DndRuleCommand -> dnd.setCompanionRuleActive(message.active)
            IncomingMessage.HeadphoneHandoff -> handleHeadphoneHandoff()
            IncomingMessage.HotspotRequest -> handleHotspotRequest()
            is IncomingMessage.ClipboardUpdate -> if (clipboard.enabled) clipboard.applyIncoming(message.content)
            null -> Unit
        }
    }
    private fun handleHeadphoneHandoff() {
        val deviceName = bluetoothAudio.state.value?.deviceName ?: "Bluetooth headphones"
        when (bluetoothAudio.releaseForHandoff()) {
            HeadphoneReleaseResult.REQUESTED -> state.update {
                it.copy(featureNotice = "Releasing $deviceName for your laptop…")
            }
            HeadphoneReleaseResult.NEEDS_USER_ACTION -> {
                state.update { it.copy(featureNotice = "Tap the handoff notification to disconnect $deviceName.") }
                showHandoffNotification(deviceName)
            }
            HeadphoneReleaseResult.NO_DEVICE -> state.update {
                it.copy(featureNotice = "No Bluetooth audio device is connected to this phone.")
            }
        }
    }
    private fun showHandoffNotification(deviceName: String) {
        val settings = Intent(Settings.ACTION_BLUETOOTH_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val pending = PendingIntent.getActivity(this, 2, settings,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(this, "unity_service")
            .setContentTitle("Move $deviceName to laptop")
            .setContentText("Tap to open Bluetooth settings, then disconnect the device.")
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentIntent(pending)
            .setAutoCancel(true)
            .build()
        NotificationManagerCompat.from(this).notify(2, notification)
    }
    private fun handleHotspotRequest() {
        state.update { it.copy(featureNotice = "Tap the phone-internet notification to review hotspot settings.") }
        val pending = PendingIntent.getActivity(this, 3, HotspotSettings.intent(this),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(this, "unity_service")
            .setContentTitle("Use phone internet")
            .setContentText("Tap to turn on internet tethering in Android settings.")
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentIntent(pending)
            .setAutoCancel(true)
            .build()
        NotificationManagerCompat.from(this).notify(3, notification)
    }
    private fun sendPcMediaCommand(command: String) {
        val media = state.value.pcMedia
        val allowed = when (command) {
            "play_pause" -> media?.capabilities?.playPause == true
            "next_track" -> media?.capabilities?.nextTrack == true
            "previous_track" -> media?.capabilities?.previousTrack == true
            else -> false
        }
        if (!allowed || owner.active() == null || pcMediaCommand?.isActive == true) return
        val generation = owner.generation
        pcMediaCommand = scope.launch {
            try {
                val sent = sendActive(MessageCodec.encodePcMediaCommand(command).toByteArray(Charsets.UTF_8))
                if (!sent && owner.isCurrentGeneration(generation)) state.update {
                    it.copy(featureNotice = "The laptop media control could not be sent.")
                }
            } finally { pcMediaCommand = null }
        }
    }
    private fun sendClipboard() {
        if (owner.active() == null || !clipboard.enabled) {
            state.update { it.copy(featureNotice = "Connect your laptop and turn clipboard sync on first.") }; return
        }
        clipboard.captureCurrent().fold(onSuccess = { content ->
            val generation = owner.generation
            scope.launch {
                val sent = sendActive(MessageCodec.encodeClipboard(content).toByteArray(Charsets.UTF_8))
                if (owner.isCurrentGeneration(generation)) state.update {
                    it.copy(featureNotice = if (sent) "Clipboard sent to your laptop." else "Clipboard could not be sent.")
                }
            }
        }, onFailure = { error -> state.update { it.copy(featureNotice = error.message ?: "Clipboard unavailable.") } })
    }
    @SuppressLint("ApplySharedPref") // Revocation must reach disk before listeners can be restarted.
    private fun forget() {
        owner.revoke()
        pairingUntil = 0; consent?.complete(false); consent = null
        pairingExpiry?.cancel(); pairingExpiry = null
        pcMediaCommand?.cancel(); pcMediaCommand = null
        prefs.edit().clear().commit()
        clipboard.updateEnabled(false)
        stopListeners()
        stopCollector()
        state.value = UiState(
            bluetoothAudioName = bluetoothAudio.state.value?.deviceName,
            bluetoothAudioCanRelease = bluetoothAudio.state.value?.canRelease == true
        )
    }
    private fun notification(text: String): Notification {
        val intent = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        return NotificationCompat.Builder(this, "unity_service").setContentTitle("Unity Connect").setContentText(text)
            .setSmallIcon(android.R.drawable.ic_dialog_info).setContentIntent(intent).setOngoing(true).build()
    }
    private fun notifyState() {
        getSystemService(NotificationManager::class.java).notify(1, notification(
            if (!owner.hasSessions) "Waiting for your laptop" else "Connected to your laptop"))
    }
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onDestroy() {
        instance = null
        unregisterRuntimeSignals()
        owner.close(); stopCollector(); pcMediaCommand?.cancel(); pcMediaCommand = null
        bluetoothAudio.close()
        pairingExpiry?.cancel(); pairingExpiry = null
        stopListeners(); scope.cancel(); dnd.close()
        state.update { it.copy(connectionState = ConnectionState.DISCONNECTED, sasCode = null,
            awaitingOtherDevice = false, pcMedia = null) }
        super.onDestroy()
    }
}
