package com.unity.connect.android.lan

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import com.unity.connect.android.core.ConnectionPolicy
import com.unity.connect.android.core.FramePipe
import com.unity.connect.android.core.ListenerLifecycle
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
    init { socket.tcpNoDelay = true; socket.keepAlive = true; socket.soTimeout = ConnectionPolicy.socketReadTimeoutMs }
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
    private val nsd = context.getSystemService(NsdManager::class.java)
    private val lifecycle = ListenerLifecycle()
    private val companionPort = 38471

    private class Run(val lease: ListenerLifecycle.Lease, val scope: CoroutineScope) {
        var client: Socket? = null
    }

    fun start(): Boolean {
        val lease = lifecycle.begin() ?: return false
        val run = Run(lease, CoroutineScope(Dispatchers.IO + SupervisorJob()))
        check(lifecycle.track(lease) { run.scope.cancel() })
        run.scope.launch {
            try { serve(run) }
            catch (failure: Exception) {
                if (failure is CancellationException) throw failure
                lifecycle.perform(lease) {
                    endpoint(0)
                    error("Wi-Fi connection could not start. Tap reconnect to try again.")
                }
            } finally { lifecycle.finish(lease) }
        }
        return true
    }

    private fun serve(run: Run) {
        val listener = ServerSocket().apply {
            reuseAddress = true
            bind(InetSocketAddress(companionPort))
        }
        if (!lifecycle.track(run.lease) { runCatching { listener.close() } }) return
        if (!lifecycle.perform(run.lease) { endpoint(listener.localPort) }) return

        val info = NsdServiceInfo().apply {
            serviceName = "UnityPhone"; serviceType = "_phonecomp._tcp."; port = listener.localPort
        }
        val registration = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(info: NsdServiceInfo) = Unit
            override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) {
                lifecycle.perform(run.lease) {
                    error("Automatic discovery is unavailable. Use the phone address shown below.")
                }
            }
            override fun onServiceUnregistered(info: NsdServiceInfo) = Unit
            override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) = Unit
        }
        if (!lifecycle.track(run.lease) { runCatching { nsd.unregisterService(registration) } }) return
        if (!lifecycle.perform(run.lease) {
            nsd.registerService(info, NsdManager.PROTOCOL_DNS_SD, registration)
        }) return

        while (run.scope.isActive && lifecycle.isCurrent(run.lease)) {
            val socket = listener.accept()
            if (!lifecycle.track(run.lease) { runCatching { socket.close() } }) continue
            lifecycle.perform(run.lease) {
                if (run.client?.isClosed == false) socket.close()
                else {
                    run.client = socket
                    connected(SocketFramePipe(socket))
                }
            }
        }
    }

    fun stop() = lifecycle.stop()
}
