package com.unity.connect.android

import com.unity.connect.android.core.*
import java.io.IOException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.selects.select
import org.junit.Assert.*
import org.junit.Test

class PhoneSessionOwnerTest {
    private fun scenario(block: suspend CoroutineScope.() -> Unit): Unit = runBlocking {
        withTimeout(5000) { supervisorScope(block) }
    }
    private fun snapshot(level: Int, sound: String = "normal") =
        PhoneSnapshot(BatteryState(level, false), null, null, null, sound)
    private val command = """{"version":1,"type":"clipboard","updateId":"7d7fc709-84d2-4575-8b99-e262dc8cc78e","text":"Fictional text"}""".toByteArray()

    @Test fun pendingRouteIsReservedAndDuplicatePipeClosesImmediately() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        val (first, _) = MemoryPipe.pair(); val (duplicate, _) = MemoryPipe.pair()
        val entered = CompletableDeferred<Unit>()
        try {
            val job = owner.accept("wifi", first) { lease -> entered.complete(Unit); lease.pipe.read() }!!
            assertNull(owner.accept("wifi", duplicate) { fail("Duplicate route was admitted") })
            assertEquals(1, duplicate.closes.get()); entered.await()
            owner.revoke(); owner.awaitIdle()
            assertTrue(job.isCancelled); assertFalse(owner.hasSessions); assertEquals(1, first.closes.get())
        } finally { owner.close() }
    }

    @Test fun cancellationBeforeDispatchAndAfterDestructionNeverLeaksAPipe() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        val (wire, _) = MemoryPipe.pair(); var started = false
        val job = owner.accept("ble", wire) { started = true }!!
        owner.close(); owner.awaitIdle()
        assertFalse(started); assertTrue(job.isCancelled); assertEquals(1, wire.closes.get())
        owner.enable()
        val (late, _) = MemoryPipe.pair()
        assertNull(owner.accept("ble", late) { fail("Destroyed owner admitted a callback") })
        assertEquals(1, late.closes.get())
    }

    @Test fun revokedHandshakeCannotPublishTrustOrRemoveANewerSameRoute() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        val (clientWire, phoneWire) = MemoryPipe.pair()
        val candidate = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        var trustWrites = 0; var published = true
        var newer: Route? = null
        try {
            val client = async {
                SecureSession.connect(clientWire, SecureSession.ephemeralKey(), "Laptop", client = true) { true }
            }
            val old = owner.accept("wifi", phoneWire) { lease ->
                val session = SecureSession.connect(lease.pipe, SecureSession.ephemeralKey(), "Phone") { true }
                try {
                    candidate.complete(Unit)
                    // Model a successful handshake continuation that returns despite cancellation.
                    withContext(NonCancellable) { release.await() }
                    published = owner.publish(lease, session) { trustWrites++ }
                } finally { session.close() }
            }!!
            candidate.await(); val oldClient = client.await()
            owner.revoke(); assertFalse(owner.hasSessions)
            owner.enable(); newer = connect(owner, "wifi")
            release.complete(Unit); old.join()
            assertFalse(published); assertEquals(0, trustWrites)
            assertSame(newer.server, owner.active()); assertTrue(owner.isActive(newer.lease))
            assertEquals(1, phoneWire.closes.get())
            oldClient.close()
        } finally { release.complete(Unit); owner.close(); newer?.client?.close() }
    }

    @Test fun wifiHasPriorityAndRemoteClosureLeavesAuthenticatedBleAvailable() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        var ble: Route? = null; var wifi: Route? = null
        try {
            ble = connect(owner, "ble"); wifi = connect(owner, "wifi")
            assertSame(wifi.server, owner.active()); assertFalse(owner.isActive(ble.lease))
            assertTrue(owner.sendActive(command)); assertArrayEquals(command, wifi.client.receive())
            assertTrue(ble.clientWire.pollIncoming().isFailure)
            wifi.client.close(); wifi.job.join()
            assertSame(ble.server, owner.active()); assertTrue(owner.isActive(ble.lease))
            assertEquals(1, wifi.wire.closes.get())
            assertTrue(owner.sendActive(command)); assertArrayEquals(command, ble.client.receive())
        } finally { owner.close(); ble?.client?.close(); wifi?.client?.close() }
    }

    @Test fun failedWifiSendClosesOnceWithoutRetryingOnBle() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        var ble: Route? = null; var wifi: Route? = null
        try {
            ble = connect(owner, "ble"); wifi = connect(owner, "wifi")
            val attempts = wifi.wire.writes.get(); wifi.wire.failWrites = true
            assertFalse(owner.sendActive(command)); wifi.job.join()
            assertEquals(attempts + 1, wifi.wire.writes.get()); assertEquals(1, wifi.wire.closes.get())
            assertSame(ble.server, owner.active()); assertTrue(ble.clientWire.pollIncoming().isFailure)
        } finally { owner.close(); ble?.client?.close(); wifi?.client?.close() }
    }

    @Test fun revokeUnblocksAnActiveEncryptedWriteAndInvalidatesItsResult() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        var route: Route? = null
        try {
            route = connect(owner, "wifi"); route.wire.blockWrites = CompletableDeferred()
            val generation = owner.generation
            val send = async { owner.sendActive(command) }; route.wire.writeEntered.await()
            owner.revoke(); assertFalse(send.await()); owner.awaitIdle()
            assertFalse(owner.isCurrentGeneration(generation)); assertFalse(owner.hasSessions)
            assertEquals(1, route.wire.closes.get()); assertTrue(route.clientWire.pollIncoming().isFailure)
        } finally { owner.close(); route?.client?.close() }
    }

    @Test fun parentCancellationClosesAPipeThatNeedsCloseToInterruptBlockingIo() = scenario {
        val parent = SupervisorJob(coroutineContext[Job])
        val serviceScope = CoroutineScope(coroutineContext + parent)
        val owner = PhoneSessionOwner(serviceScope) {}; owner.enable()
        val entered = CompletableDeferred<Unit>(); val interrupt = CountDownLatch(1); val closes = AtomicInteger()
        val pipe = object : FramePipe {
            override suspend fun read(): ByteArray = withContext(Dispatchers.IO) {
                entered.complete(Unit)
                check(interrupt.await(3, TimeUnit.SECONDS)) { "Pipe close did not unblock read" }
                throw IOException("Closed")
            }
            override suspend fun write(frame: ByteArray) = Unit
            override fun close() { closes.incrementAndGet(); interrupt.countDown() }
        }
        try {
            owner.accept("wifi", pipe) { lease ->
                try { lease.pipe.read() }
                catch (error: Exception) { if (error is CancellationException) throw error }
            }
            entered.await(); parent.cancel(); owner.awaitIdle(); parent.join()
            assertEquals(1, closes.get()); assertFalse(owner.hasSessions)
        } finally { owner.close(); parent.cancel(); interrupt.countDown() }
    }

    @Test fun simultaneousRouteClosureReleasesBothAndClearsActiveState() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        var ble: Route? = null; var wifi: Route? = null
        try {
            ble = connect(owner, "ble"); wifi = connect(owner, "wifi")
            ble.client.close(); wifi.client.close(); owner.awaitIdle()
            assertFalse(owner.hasSessions); assertNull(owner.active())
            assertEquals(1, ble.wire.closes.get()); assertEquals(1, wifi.wire.closes.get())
        } finally { owner.close(); ble?.client?.close(); wifi?.client?.close() }
    }

    @Test fun productionCollectionReceivesCombinedFlowEmissionsAndSendsSnapshots() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        val route = connect(owner, "wifi")
        val battery = MutableStateFlow(68); val sound = MutableStateFlow("normal")
        val collection = PhoneStateCollection(this, owner, combine(battery, sound) { b, s -> snapshot(b, s) })
        try {
            collection.restart()
            assertEquals(snapshot(68), MessageCodec.decodePhoneSnapshot(route.client.receive().toString(Charsets.UTF_8)))
            sound.value = "silent"
            val actual = MessageCodec.decodePhoneSnapshot(route.client.receive().toString(Charsets.UTF_8))
            assertEquals(snapshot(68, "silent"), actual); assertEquals(actual, collection.latest)
            collection.stop(); assertNull(collection.latest)
        } finally { collection.stop(); owner.close(); route.client.close() }
    }

    @Test fun rapidCollectorRestartsWaitForOriginalCleanupAndKeepNewState() = scenario {
        val owner = PhoneSessionOwner(this) {}; owner.enable()
        val route = connect(owner, "wifi")
        val cleanupStarted = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        var starts = 0
        val snapshots = flow {
            val index = ++starts
            try { emit(snapshot(index)); awaitCancellation() }
            finally {
                if (index == 1) withContext(NonCancellable) { cleanupStarted.complete(Unit); release.await() }
            }
        }
        val collection = PhoneStateCollection(this, owner, snapshots)
        try {
            collection.restart(); route.client.receive()
            collection.stop(); cleanupStarted.await()
            collection.restart(); yield(); collection.stop(); collection.restart(); yield()
            assertEquals(1, starts); assertNull(collection.latest)
            release.complete(Unit)
            val next = MessageCodec.decodePhoneSnapshot(route.client.receive().toString(Charsets.UTF_8))
            assertEquals(snapshot(2), next); assertEquals(next, collection.latest)
            assertEquals(2, starts); assertTrue(collection.isCollecting)
        } finally { release.complete(Unit); collection.stop(); owner.close(); route.client.close() }
    }

    private data class Route(val lease: PhoneSessionOwner.Lease, val server: SecureSession,
        val client: SecureSession, val wire: MemoryPipe, val clientWire: MemoryPipe, val job: Job)

    private suspend fun CoroutineScope.connect(owner: PhoneSessionOwner, kind: String): Route {
        val (clientWire, phoneWire) = MemoryPipe.pair()
        val ready = CompletableDeferred<Pair<PhoneSessionOwner.Lease, SecureSession>>()
        val client = async {
            SecureSession.connect(clientWire, SecureSession.ephemeralKey(), "Laptop", client = true) { true }
        }
        val job = owner.accept(kind, phoneWire) { lease ->
            var session: SecureSession? = null
            try {
                session = SecureSession.connect(lease.pipe, SecureSession.ephemeralKey(), "Phone") { true }
                check(owner.publish(lease, session) {})
                ready.complete(lease to session)
                while (currentCoroutineContext().isActive) session.receive()
            } catch (error: Exception) {
                ready.completeExceptionally(error)
                if (error is CancellationException) throw error
            } finally { session?.close() }
        }!!
        val clientSession = client.await()
        val (lease, server) = ready.await()
        assertEquals(clientSession.peer.code, server.peer.code)
        return Route(lease, server, clientSession, phoneWire, clientWire, job)
    }

    private class MemoryPipe(private val incoming: Channel<ByteArray>, private val outgoing: Channel<ByteArray>) : FramePipe {
        val closes = AtomicInteger(); val writes = AtomicInteger()
        var failWrites = false
        var blockWrites: CompletableDeferred<Unit>? = null
        val writeEntered = CompletableDeferred<Unit>(); private val ended = CompletableDeferred<Unit>()
        override suspend fun read() = incoming.receive()
        fun pollIncoming() = incoming.tryReceive()
        override suspend fun write(frame: ByteArray) {
            writes.incrementAndGet()
            if (failWrites) throw IOException("Fictional send failure")
            blockWrites?.let { gate ->
                writeEntered.complete(Unit)
                select<Unit> { gate.onAwait { }; ended.onAwait { throw IOException("Closed during write") } }
            }
            outgoing.send(frame.copyOf())
        }
        override fun close() { closes.incrementAndGet(); ended.complete(Unit); incoming.close(); outgoing.close() }
        companion object {
            fun pair(): Pair<MemoryPipe, MemoryPipe> {
                val left = Channel<ByteArray>(Channel.UNLIMITED); val right = Channel<ByteArray>(Channel.UNLIMITED)
                return MemoryPipe(left, right) to MemoryPipe(right, left)
            }
        }
    }
}
