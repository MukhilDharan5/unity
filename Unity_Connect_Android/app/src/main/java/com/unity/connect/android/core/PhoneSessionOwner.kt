package com.unity.connect.android.core

import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.*

/**
 * Service-owned route reservations and jobs. No Android APIs or security decisions.
 * A pipe must close promptly and unblock pending reads/writes; admission is thread-safe
 * because BLE/TCP callbacks may arrive outside the service's Main dispatcher.
 */
internal class PhoneSessionOwner(
    private val scope: CoroutineScope,
    private val onReleased: () -> Unit
) {
    private val lock = Any()
    private val entries = mutableMapOf<String, Lease>()
    private val jobs = mutableSetOf<Job>()
    private var epoch = 0L
    private var accepting = false
    private var destroyed = false
    private val lifetime = scope.launch(start = CoroutineStart.UNDISPATCHED) {
        try { awaitCancellation() }
        finally { invalidate(terminal = true) }
    }

    internal class Lease internal constructor(val kind: String, val epoch: Long, val pipe: FramePipe) {
        internal var job: Job? = null
        internal var session: SecureSession? = null
        internal fun stop() {
            try { session?.close() ?: pipe.close() }
            finally { job?.cancel() }
        }
    }

    val generation: Long get() = synchronized(lock) { epoch }
    val hasSessions: Boolean get() = synchronized(lock) {
        entries.values.any { current(it) && it.job?.isActive == true && it.session != null }
    }
    private fun current(lease: Lease) =
        !destroyed && accepting && scope.isActive && lease.epoch == epoch && entries[lease.kind] === lease
    fun isCurrent(lease: Lease): Boolean = synchronized(lock) { current(lease) }
    fun isCurrentGeneration(value: Long): Boolean = synchronized(lock) {
        !destroyed && accepting && scope.isActive && epoch == value
    }
    fun enable() = synchronized(lock) { if (!destroyed && scope.isActive) accepting = true }

    /** Reserve before dispatching so duplicates and callbacks queued before Forget are fenced. */
    fun accept(kind: String, pipe: FramePipe, block: suspend (Lease) -> Unit): Job? {
        require(kind == "ble" || kind == "wifi")
        val guarded = CloseOncePipe(pipe)
        val lease = synchronized(lock) {
            if (destroyed || !accepting || !scope.isActive || entries.containsKey(kind)) null
            else Lease(kind, epoch, guarded).also { entries[kind] = it }
        }
        if (lease == null) { guarded.close(); return null }
        val job = scope.launch(start = CoroutineStart.LAZY) {
            try { if (isCurrent(lease)) block(lease) }
            finally { finish(lease) }
        }
        synchronized(lock) {
            lease.job = job
            jobs.add(job)
            if (!current(lease)) job.cancel()
        }
        // Also runs when cancellation prevents a lazy coroutine body from ever starting.
        job.invokeOnCompletion {
            try { finish(lease) }
            finally { synchronized(lock) { jobs.remove(job) } }
        }
        job.start()
        return job
    }

    /** The non-suspending hook keeps trust persistence inside the revocation fence. */
    fun publish(lease: Lease, session: SecureSession, beforePublish: () -> Unit): Boolean = synchronized(lock) {
        if (!current(lease) || lease.job?.isActive != true) return false
        check(lease.session == null)
        beforePublish()
        if (!current(lease) || lease.job?.isActive != true) return false
        lease.session = session
        true
    }

    private fun activeLease() = entries["wifi"]?.takeIf { current(it) && it.job?.isActive == true && it.session != null }
        ?: entries["ble"]?.takeIf { current(it) && it.job?.isActive == true && it.session != null }
    fun active(): SecureSession? = synchronized(lock) { activeLease()?.session }
    fun isActive(lease: Lease): Boolean = synchronized(lock) { current(lease) && activeLease() === lease }

    suspend fun sendActive(frame: ByteArray): Boolean {
        val target = synchronized(lock) { activeLease() } ?: return false
        return try {
            withTimeout(10000) {
                val session = synchronized(lock) {
                    check(current(target)) { "Route ended" }
                    target.session!!
                }
                session.send(frame)
            }
            isCurrent(target)
        } catch (error: Exception) {
            finish(target) // Ambiguous send: close the selected route; never retry on another.
            if (error is CancellationException && !currentCoroutineContext().isActive) throw error
            false
        }
    }

    fun finish(lease: Lease) {
        val removed = synchronized(lock) {
            if (entries[lease.kind] !== lease) false else { entries.remove(lease.kind); true }
        }
        try { lease.stop() }
        finally { if (removed) onReleased() }
    }
    fun revoke() = invalidate(terminal = false)
    fun close() {
        try { invalidate(terminal = true) }
        finally { lifetime.cancel() }
    }
    private fun invalidate(terminal: Boolean) {
        val old = synchronized(lock) {
            if (destroyed) return
            epoch++; accepting = false
            if (terminal) destroyed = true
            entries.values.toList().also { entries.clear() }
        }
        var failure: Exception? = null
        try { old.forEach { lease ->
            try { lease.stop() }
            catch (error: Exception) { if (failure == null) failure = error else failure!!.addSuppressed(error) }
        } }
        finally { onReleased() }
        failure?.let { throw it }
    }
    suspend fun awaitIdle() {
        while (true) {
            val pending = synchronized(lock) { jobs.toList() }
            if (pending.isEmpty()) return
            pending.joinAll()
        }
    }

    private class CloseOncePipe(private val delegate: FramePipe) : FramePipe {
        private val closed = AtomicBoolean()
        override suspend fun read(): ByteArray {
            check(!closed.get()) { "Pipe closed" }
            return delegate.read()
        }
        override suspend fun write(frame: ByteArray) {
            check(!closed.get()) { "Pipe closed" }
            delegate.write(frame)
        }
        override fun close() { if (closed.compareAndSet(false, true)) delegate.close() }
    }
}
