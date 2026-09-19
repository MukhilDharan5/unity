package com.unity.connect.android.core

import java.util.ArrayDeque
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Thread-safe ownership fence for one listener run at a time.
 *
 * Resources may be created by platform calls that do not observe coroutine cancellation.
 * Tracking a resource after stop closes it immediately. Callbacks use [perform] so stop
 * and stale callbacks have a single ordering, and finishing an old run cannot retire a
 * newer one.
 */
internal class ListenerLifecycle {
    internal class Lease internal constructor(val generation: Long) {
        internal val cleanups = ArrayDeque<Cleanup>()
        internal var retired = false
    }

    internal class Cleanup(private val action: () -> Unit) {
        private val completed = AtomicBoolean()
        fun run() { if (completed.compareAndSet(false, true)) action() }
    }

    private val lock = Any()
    private var generation = 0L
    private var current: Lease? = null

    fun begin(): Lease? = synchronized(lock) {
        if (current != null) null
        else Lease(++generation).also { current = it }
    }

    fun isCurrent(lease: Lease): Boolean = synchronized(lock) {
        current === lease && !lease.retired
    }

    fun isCurrent(generation: Long): Boolean = synchronized(lock) {
        current?.let { !it.retired && it.generation == generation } == true
    }

    /** Register newest-first cleanup, or clean up immediately when the run is stale. */
    fun track(lease: Lease, cleanup: () -> Unit): Boolean {
        val entry = Cleanup(cleanup)
        val retained = synchronized(lock) {
            if (current === lease && !lease.retired) {
                lease.cleanups.addFirst(entry)
                true
            } else false
        }
        if (!retained) runCleanup(entry)
        return retained
    }

    /** Execute a short callback atomically with respect to stop/restart. */
    fun perform(lease: Lease, action: () -> Unit): Boolean = synchronized(lock) {
        if (current !== lease || lease.retired) false
        else { action(); true }
    }

    fun stop() {
        val cleanups = synchronized(lock) {
            current?.let { retireLocked(it, clearCurrent = true) }.orEmpty()
        }
        runCleanups(cleanups)
    }

    fun finish(lease: Lease) {
        val cleanups = synchronized(lock) {
            retireLocked(lease, clearCurrent = current === lease)
        }
        runCleanups(cleanups)
    }

    private fun retireLocked(lease: Lease, clearCurrent: Boolean): List<Cleanup> {
        if (lease.retired) return emptyList()
        lease.retired = true
        if (clearCurrent) current = null
        return buildList {
            while (lease.cleanups.isNotEmpty()) add(lease.cleanups.removeFirst())
        }
    }

    private fun runCleanups(cleanups: List<Cleanup>) = cleanups.forEach(::runCleanup)
    private fun runCleanup(cleanup: Cleanup) { try { cleanup.run() } catch (_: Exception) { } }
}
