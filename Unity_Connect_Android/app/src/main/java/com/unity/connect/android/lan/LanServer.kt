package com.unity.connect.android.lan

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import com.unity.connect.android.core.FramePipe
import com.unity.connect.android.core.SecureSession
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket

class SocketFramePipe(private val socket: Socket) : FramePipe {
    private val input = DataInputStream(socket.getInputStream())
    private val output = DataOutputStream(socket.getOutputStream())
    private val writes = Mutex()
    init { socket.tcpNoDelay = true; socket.keepAlive = true; socket.soTimeout = 120000 }
    override suspend fun read(): ByteArray = withContext(Dispatchers.IO) {
        val length = input.readInt()
        require(length in 1..SecureSession.MAX_WIRE_BYTES) { "Invalid packet size" }
        ByteArray(length).also { input.readFully(it) }
    }
    override suspend fun write(frame: ByteArray) = writes.withLock {
        require(frame.size in 1..SecureSession.MAX_WIRE_BYTES)
        withContext(Dispatchers.IO) { output.writeInt(frame.size); output.write(frame); output.flush() }
    }
    override fun close() { runCatching { socket.close() } }
}

class LanServer(private val context: Context,
    private val connected: (FramePipe) -> Unit,
    private val endpoint: (Int) -> Unit,
    private val error: (String) -> Unit) {
    private var server: ServerSocket? = null
    private var scope: CoroutineScope? = null
    private var client: Socket? = null
    private val nsd = context.getSystemService(NsdManager::class.java)
    private var registration: NsdManager.RegistrationListener? = null
    private val companionPort = 38471

    @Synchronized fun start() {
        if (scope != null) return
        scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
        scope!!.launch {
            try {
                val listener = ServerSocket().apply {
                    reuseAddress = true
                    bind(InetSocketAddress(companionPort))
                }.also { server = it }
                endpoint(listener.localPort)
                val info = NsdServiceInfo().apply {
                    serviceName = "UnityPhone"; serviceType = "_phonecomp._tcp."; port = listener.localPort
                }
                registration = object : NsdManager.RegistrationListener {
                    override fun onServiceRegistered(info: NsdServiceInfo) = Unit
                    override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) { error("Automatic discovery is unavailable. Use the phone address shown below.") }
                    override fun onServiceUnregistered(info: NsdServiceInfo) = Unit
                    override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) = Unit
                }
                nsd.registerService(info, NsdManager.PROTOCOL_DNS_SD, registration)
                while (isActive) {
                    val socket = listener.accept()
                    synchronized(this@LanServer) {
                        if (client?.isClosed == false) socket.close()
                        else { client = socket; connected(SocketFramePipe(socket)) }
                    }
                }
            } catch (e: Exception) { if (isActive) { error("Wi-Fi connection could not start. Tap reconnect to try again."); stop() } }
        }
    }
    @Synchronized fun stop() {
        runCatching { registration?.let { nsd.unregisterService(it) } }; registration = null
        runCatching { server?.close() }; server = null
        runCatching { client?.close() }; client = null
        scope?.cancel(); scope = null
    }
}
