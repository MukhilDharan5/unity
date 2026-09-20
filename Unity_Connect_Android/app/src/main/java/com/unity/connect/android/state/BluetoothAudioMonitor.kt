package com.unity.connect.android.state

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothA2dp
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.Closeable

data class BluetoothAudioState(val deviceName: String)

/** Observes the public A2DP profile without attempting privileged connect/disconnect operations. */
class BluetoothAudioMonitor(private val context: Context) : Closeable {
    private val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter
    private val mutableState = MutableStateFlow<BluetoothAudioState?>(null)
    private var profile: BluetoothA2dp? = null
    private var profileRequested = false
    private var receiverRegistered = false
    private var closed = false

    val state: StateFlow<BluetoothAudioState?> = mutableState.asStateFlow()

    private val serviceListener = object : BluetoothProfile.ServiceListener {
        override fun onServiceConnected(profileType: Int, proxy: BluetoothProfile?) {
            if (profileType != BluetoothProfile.A2DP) return
            if (closed) {
                (proxy as? BluetoothA2dp)?.let { adapter?.closeProfileProxy(BluetoothProfile.A2DP, it) }
                return
            }
            profileRequested = true
            profile = proxy as? BluetoothA2dp
            refresh()
        }

        override fun onServiceDisconnected(profileType: Int) {
            if (profileType != BluetoothProfile.A2DP) return
            profileRequested = false
            profile = null
            mutableState.value = null
        }
    }

    private val connectionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action == BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED) refresh()
        }
    }

    init {
        runCatching {
            ContextCompat.registerReceiver(
                context,
                connectionReceiver,
                IntentFilter(BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED),
                ContextCompat.RECEIVER_EXPORTED
            )
            receiverRegistered = true
        }
        ensureProfileProxy()
    }

    @SuppressLint("MissingPermission")
    fun refresh() {
        if (closed || ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.BLUETOOTH_CONNECT
            ) != PackageManager.PERMISSION_GRANTED
        ) {
            mutableState.value = null
            return
        }

        ensureProfileProxy()
        val device = runCatching { profile?.connectedDevices?.firstOrNull() }.getOrNull()
        mutableState.value = device?.let {
            val rawName = runCatching { it.alias ?: it.name }.getOrNull()
            BluetoothAudioState(readableName(rawName))
        }
    }

    override fun close() {
        if (closed) return
        closed = true
        if (receiverRegistered) runCatching { context.unregisterReceiver(connectionReceiver) }
        profile?.let { current -> runCatching { adapter?.closeProfileProxy(BluetoothProfile.A2DP, current) } }
        profileRequested = false
        profile = null
        mutableState.value = null
    }

    @SuppressLint("MissingPermission")
    private fun ensureProfileProxy() {
        if (closed || profile != null || profileRequested || ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.BLUETOOTH_CONNECT
            ) != PackageManager.PERMISSION_GRANTED
        ) return
        profileRequested = runCatching {
            adapter?.getProfileProxy(context, serviceListener, BluetoothProfile.A2DP) == true
        }.getOrDefault(false)
    }

    private fun readableName(value: String?): String {
        val cleaned = value.orEmpty()
            .map { character -> if (character.isISOControl()) ' ' else character }
            .joinToString("")
            .trim()
            .replace(Regex("\\s+"), " ")
        return cleaned.take(80).ifBlank { "Bluetooth audio device" }
    }
}
