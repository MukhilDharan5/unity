package com.unity.connect.android.core

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.Flow

/** Owns observer replacement and the latest state; old cleanup cannot clear a new collection. */
internal class PhoneStateCollection(
    private val scope: CoroutineScope,
    private val owner: PhoneSessionOwner,
    private val snapshots: Flow<PhoneSnapshot>
) {
    private var current: Job? = null
    private var cleanup: Job? = null
    var latest: PhoneSnapshot? = null
        private set
    val isCollecting get() = current?.isActive == true

    // Called on the service's Main dispatcher, as are the flow emissions.
    fun restart() {
        if (!owner.hasSessions) return
        val previous = current ?: cleanup
        val generation = owner.generation
        previous?.cancel()
        lateinit var next: Job
        next = scope.launch(start = CoroutineStart.LAZY) {
            // Rapid stop/restart must retain the chain back to the original observer cleanup.
            withContext(NonCancellable) { previous?.cancelAndJoin() }
            currentCoroutineContext().ensureActive()
            if (current !== next || !owner.isCurrentGeneration(generation) || !owner.hasSessions) return@launch
            snapshots.collect { snapshot ->
                if (current !== next || !owner.isCurrentGeneration(generation) || !owner.hasSessions) return@collect
                latest = snapshot
                owner.sendActive(MessageCodec.encodePhoneSnapshot(snapshot).toByteArray(Charsets.UTF_8))
            }
        }
        current = next; cleanup = null; next.start()
    }

    fun stop() {
        val previous = current
        current = null; latest = null
        if (previous != null) { cleanup = previous; previous.cancel() }
    }
}
