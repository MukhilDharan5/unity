package com.unity.connect.android

import com.unity.connect.android.core.FramePipe
import com.unity.connect.android.core.SecureSession
import java.security.KeyPairGenerator
import java.security.spec.ECGenParameterSpec
import kotlinx.coroutines.async
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class SecureSessionTest {
    private class MemoryPipe(private val incoming: Channel<ByteArray>, private val outgoing: Channel<ByteArray>) : FramePipe {
        override suspend fun read() = incoming.receive()
        override suspend fun write(frame: ByteArray) = outgoing.send(frame.copyOf())
        override fun close() { outgoing.close() }
    }
    private fun pair(): Pair<MemoryPipe, MemoryPipe> {
        val left = Channel<ByteArray>(Channel.UNLIMITED); val right = Channel<ByteArray>(Channel.UNLIMITED)
        return MemoryPipe(left, right) to MemoryPipe(right, left)
    }
    private fun identity() = KeyPairGenerator.getInstance("EC").run {
        initialize(ECGenParameterSpec("secp256r1")); generateKeyPair()
    }

    @Test fun handshakeAndEncryptedMessagesWorkBothWays() = runBlocking {
        val (clientWire, serverWire) = pair()
        val client = async { SecureSession.connect(clientWire, identity(), "Laptop", client = true) { true } }
        val server = async { SecureSession.connect(serverWire, identity(), "Phone") { true } }
        val clientSession = client.await(); val serverSession = server.await()
        assertEquals(clientSession.peer.code, serverSession.peer.code)
        val first = "phone state 🎵".toByteArray()
        clientSession.send(first); assertArrayEquals(first, serverSession.receive())
        val second = "media command".toByteArray()
        serverSession.send(second); assertArrayEquals(second, clientSession.receive())
        clientSession.close(); serverSession.close()
    }
}
