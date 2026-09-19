package com.unity.connect.android

import android.app.*
import android.annotation.SuppressLint
import android.content.Intent
import android.net.ConnectivityManager
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.unity.connect.android.ble.BleManager
import com.unity.connect.android.clipboard.ClipboardBridge
import com.unity.connect.android.core.*
import com.unity.connect.android.dnd.CompanionDndController
import com.unity.connect.android.lan.LanServer
import com.unity.connect.android.state.StateCollector
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import kotlinx.serialization.json.*
import java.net.Inet4Address
import java.net.NetworkInterface

enum class ConnectionState { DISCONNECTED, CONNECTING, CONNECTED }
data class UiState(
    val isPaired: Boolean = false,
    val connectionState: ConnectionState = ConnectionState.DISCONNECTED,
    val sasCode: String? = null,
    val pairedPcName: String? = null,
    val canControlCompanionDnd: Boolean = false,
    val companionDndActive: Boolean = false,
    val clipboardSyncEnabled: Boolean = false,
    val featureNotice: String? = null,
    val wifiAddress: String? = null,
    val awaitingOtherDevice: Boolean = false
)

class ConnectionService : Service() {
    companion object {
        private val state = MutableStateFlow(UiState())
        val serviceState: StateFlow<UiState> = state.asStateFlow()
        private var instance: ConnectionService? = null
        fun startPairing() { instance?.startTransports() }
        fun acceptSasCode() { instance?.consent?.complete(true) }
        fun rejectPairing() { instance?.consent?.complete(false) }
        fun forgetDevice() { instance?.forget() }
        fun setClipboardSyncEnabled(enabled: Boolean) { instance?.apply {
            clipboard.updateEnabled(enabled); state.update { it.copy(clipboardSyncEnabled = enabled) }
        } }
        fun sendCurrentClipboard() { instance?.sendClipboard() }
        fun setCompanionDndActive(active: Boolean) { instance?.dnd?.setCompanionRuleActive(active) }
        fun refreshDndState() { instance?.apply { dnd.refresh(); refreshAddress(); restartCollector() } }
    }
    private val scope = CoroutineScope(Dispatchers.Main.immediate + SupervisorJob())
    private lateinit var ble: BleManager
    private lateinit var lan: LanServer
    private lateinit var dnd: CompanionDndController
    private lateinit var clipboard: ClipboardBridge
    private lateinit var collector: StateCollector
    private val prefs by lazy { getSharedPreferences("secure_peers_v1", MODE_PRIVATE) }
    private val identity by lazy { IdentityStore.load() }
    private val owner = PhoneSessionOwner(scope) { scope.launch { onSessionsChanged() } }
    private var consent: CompletableDeferred<Boolean>? = null
    private var pairingUntil = 0L
    @Volatile private var port = 0
    private lateinit var collection: PhoneStateCollection
    private val latest get() = collection.latest

    override fun onCreate() {
        super.onCreate()
        instance = this
        dnd = CompanionDndController(this)
        clipboard = ClipboardBridge(this)
        collector = StateCollector(this, dnd)
        collection = PhoneStateCollection(scope, owner, collector.phoneState)
        state.value = UiState(isPaired = prefs.contains("identity"), pairedPcName = prefs.getString("name", null),
            clipboardSyncEnabled = clipboard.enabled)
        dnd.state.onEach { value -> state.update { it.copy(canControlCompanionDnd = value?.canControlCompanionRule == true,
            companionDndActive = value?.companionRuleActive == true) } }.launchIn(scope)
        fun notice(text: String) { state.update { it.copy(featureNotice = text) } }
        ble = BleManager(this, { pipe -> owner.accept("ble", pipe, ::accept) }, ::notice)
        lan = LanServer(this, { pipe -> owner.accept("wifi", pipe, ::accept) },
            { value -> port = value; refreshAddress() }, ::notice)
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel("unity_service", "Phone connection", NotificationManager.IMPORTANCE_LOW))
        startForeground(1, notification("Ready to connect"))
        if (prefs.contains("identity")) startTransports()
        return START_STICKY
    }
    private fun startTransports() {
        if (!prefs.contains("identity")) pairingUntil = android.os.SystemClock.elapsedRealtime() + 120000
        owner.enable()
        ble.start(); lan.start(); refreshAddress()
        state.update { it.copy(connectionState = if (!owner.hasSessions) ConnectionState.CONNECTING else ConnectionState.CONNECTED,
            featureNotice = if (!it.isPaired) "Open Connect phone on your laptop. Pairing is available for two minutes." else null) }
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
    @SuppressLint("ApplySharedPref") // Trust must reach disk before the UI exposes the paired session.
    private suspend fun accept(lease: PhoneSessionOwner.Lease) {
        val pipe = lease.pipe
        val sessionScope = CoroutineScope(currentCoroutineContext())
        var secure: SecureSession? = null
        var watchdog: Job? = null
        var heartbeat: Job? = null
        try {
            // Closing the socket also interrupts blocking JVM reads when the coroutine times out.
            val handshakeGuard = sessionScope.launch { delay(120000); pipe.close() }
            try {
                secure = withTimeout(120000) {
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
            state.update { it.copy(isPaired = true, connectionState = ConnectionState.CONNECTED, sasCode = null,
                pairedPcName = session.peer.name, awaitingOtherDevice = false, featureNotice = null) }
            notifyState()
            if (!collection.isCollecting) restartCollector()
            latest?.let { if (owner.isActive(lease)) session.send(MessageCodec.encodePhoneSnapshot(it).toByteArray(Charsets.UTF_8)) }
            var lastReceived = android.os.SystemClock.elapsedRealtime()
            watchdog = sessionScope.launch {
                while (isActive) { delay(5000); if (android.os.SystemClock.elapsedRealtime() - lastReceived > 35000) { pipe.close(); break } }
            }
            heartbeat = sessionScope.launch {
                try { while (isActive) { delay(10000); session.send(SecureSession.control("ping")) } }
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
            awaitingOtherDevice = if (consent == null) false else it.awaitingOtherDevice) }
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
            is IncomingMessage.DndRuleCommand -> dnd.setCompanionRuleActive(message.active)
            is IncomingMessage.ClipboardUpdate -> if (clipboard.enabled) clipboard.applyIncoming(message.content)
            null -> Unit
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
        prefs.edit().clear().commit()
        clipboard.updateEnabled(false)
        ble.stop(); lan.stop(); port = 0
        stopCollector()
        state.value = UiState()
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
        owner.close(); stopCollector()
        ble.stop(); lan.stop(); scope.cancel(); dnd.close()
        state.update { it.copy(connectionState = ConnectionState.DISCONNECTED, sasCode = null, awaitingOtherDevice = false) }
        super.onDestroy()
    }
}
