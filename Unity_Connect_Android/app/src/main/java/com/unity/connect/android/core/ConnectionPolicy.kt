package com.unity.connect.android.core

/** Runtime timing policy kept separate from transport and UI implementation. */
internal object ConnectionPolicy {
    const val pairingWindowMs = 120_000L
    const val handshakeTimeoutMs = 120_000L
    const val socketReadTimeoutMs = 120_000
    const val heartbeatMs = 10_000L
    const val peerTimeoutMs = 35_000L
    const val watchdogPollMs = 5_000L
    const val sendTimeoutMs = 10_000L
    const val bleFragmentTimeoutMs = 15_000L
    const val bleNotificationTimeoutMs = 5_000L
    val listenerRetryMs = longArrayOf(2_000L, 5_000L, 10_000L, 30_000L)
}
