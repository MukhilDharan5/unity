package com.unity.connect.android

import com.unity.connect.android.core.ListenerLifecycle
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.*
import org.junit.Test

class ListenerLifecycleTest {
    @Test fun lateStartupResourcesAndCallbacksCannotEscapeStop() {
        val lifecycle = ListenerLifecycle()
        val old = lifecycle.begin()!!
        var endpoint = 0
        val listenerCloses = AtomicInteger()

        lifecycle.stop()

        assertFalse(lifecycle.track(old) { listenerCloses.incrementAndGet() })
        assertEquals(1, listenerCloses.get())
        assertFalse(lifecycle.perform(old) { endpoint = 38471 })
        assertEquals(0, endpoint)
        assertFalse(lifecycle.isCurrent(old))
    }

    @Test fun oldCompletionCannotRetireOrOverwriteANewerRun() {
        val lifecycle = ListenerLifecycle()
        val old = lifecycle.begin()!!
        lifecycle.stop()
        val current = lifecycle.begin()!!
        var endpoint = 0
        val currentCloses = AtomicInteger()
        assertTrue(lifecycle.track(current) { currentCloses.incrementAndGet() })

        lifecycle.finish(old)

        assertTrue(lifecycle.perform(current) { endpoint = 38471 })
        assertEquals(38471, endpoint)
        assertTrue(lifecycle.isCurrent(current.generation))
        assertEquals(0, currentCloses.get())
        lifecycle.stop()
        assertEquals(1, currentCloses.get())
    }

    @Test fun duplicateStartIsRejectedUntilTheCurrentRunEnds() {
        val lifecycle = ListenerLifecycle()
        val first = lifecycle.begin()!!
        assertNull(lifecycle.begin())
        lifecycle.finish(first)
        val second = lifecycle.begin()
        assertNotNull(second)
        assertTrue(second!!.generation > first.generation)
        lifecycle.stop()
    }

    @Test fun stopAttemptsEveryCleanupOnceInNewestFirstOrder() {
        val lifecycle = ListenerLifecycle()
        val lease = lifecycle.begin()!!
        val order = mutableListOf<String>()
        assertTrue(lifecycle.track(lease) { order += "scope" })
        assertTrue(lifecycle.track(lease) { order += "socket"; throw IllegalStateException("fictional close failure") })
        assertTrue(lifecycle.track(lease) { order += "registration" })

        lifecycle.stop()
        lifecycle.stop()
        lifecycle.finish(lease)

        assertEquals(listOf("registration", "socket", "scope"), order)
    }
}
