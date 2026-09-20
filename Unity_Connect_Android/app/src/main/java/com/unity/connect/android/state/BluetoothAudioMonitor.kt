package com.unity.connect.android.state

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothA2dp
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.companion.AssociationInfo
import android.companion.AssociationRequest
import android.companion.BluetoothDeviceFilter
import android.companion.CompanionDeviceManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.IntentSender
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import com.unity.connect.android.core.AudioOutputState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.Closeable

enum class HeadphoneReleaseResult { REQUESTED, NEEDS_USER_ACTION, NO_DEVICE }

/** Observes A2DP and uses the public API 37 release operation only for a user-associated device. */
class BluetoothAudioMonitor(private val context: Context) : Closeable {
    private val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter
    private val companion = context.getSystemService(CompanionDeviceManager::class.java)
    private val mutableState = MutableStateFlow<AudioOutputState?>(null)
    private var profile: BluetoothA2dp? = null
    private var connectedDevice: BluetoothDevice? = null
    private var profileRequested = false
    private var receiverRegistered = false
    private var closed = false

    val state: StateFlow<AudioOutputState?> = mutableState.asStateFlow()

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
            connectedDevice = null
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
            connectedDevice = null
            mutableState.value = null
            return
        }

        ensureProfileProxy()
        val device = runCatching { profile?.connectedDevices?.firstOrNull() }.getOrNull()
        connectedDevice = device
        mutableState.value = device?.let {
            val rawName = runCatching { it.alias ?: it.name }.getOrNull()
            AudioOutputState(readableName(rawName), canRelease(it))
        }
    }

    @SuppressLint("MissingPermission")
    fun requestAssociation(onPending: (IntentSender) -> Unit, onFailure: (String) -> Unit) {
        val device = connectedDevice
        if (Build.VERSION.SDK_INT < DIRECT_RELEASE_API || device == null) {
            onFailure("Connect Bluetooth headphones before enabling one-tap handoff.")
            return
        }
        if (!hasConnectPermission()) {
            onFailure("Allow nearby-device access before enabling one-tap handoff.")
            return
        }
        if (canRelease(device)) {
            refresh()
            return
        }
        runCatching {
            val filter = BluetoothDeviceFilter.Builder().setAddress(device.address).build()
            val request = AssociationRequest.Builder().addDeviceFilter(filter).setSingleDevice(true).build()
            companion.associate(request, context.mainExecutor, object : CompanionDeviceManager.Callback() {
                @Deprecated("Required for older Companion Device Manager callbacks")
                override fun onDeviceFound(chooserLauncher: IntentSender) = onPending(chooserLauncher)
                override fun onAssociationPending(intentSender: IntentSender) = onPending(intentSender)
                override fun onAssociationCreated(associationInfo: AssociationInfo) = refresh()
                override fun onFailure(error: CharSequence?) {
                    onFailure(error?.toString()?.take(160) ?: "Headphone association was not completed.")
                }
            })
        }.onFailure {
            onFailure("Headphone association could not be started on this phone.")
        }
    }

    @SuppressLint("MissingPermission")
    fun releaseForHandoff(): HeadphoneReleaseResult {
        val device = connectedDevice ?: return HeadphoneReleaseResult.NO_DEVICE
        if (!canRelease(device)) return HeadphoneReleaseResult.NEEDS_USER_ACTION
        val released = runCatching {
            // compileSdk 34: call the public API 37 method only after its runtime/association gates pass.
            val status = device.javaClass.getMethod("disconnect").invoke(device) as? Int
            status == 0
        }.getOrDefault(false)
        return if (released) HeadphoneReleaseResult.REQUESTED else HeadphoneReleaseResult.NEEDS_USER_ACTION
    }

    override fun close() {
        if (closed) return
        closed = true
        if (receiverRegistered) runCatching { context.unregisterReceiver(connectionReceiver) }
        profile?.let { current -> runCatching { adapter?.closeProfileProxy(BluetoothProfile.A2DP, current) } }
        profileRequested = false
        profile = null
        connectedDevice = null
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

    @SuppressLint("MissingPermission")
    private fun canRelease(device: BluetoothDevice): Boolean =
        Build.VERSION.SDK_INT >= DIRECT_RELEASE_API && hasConnectPermission() && runCatching {
            companion.myAssociations.any { association ->
                association.deviceMacAddress?.toString()?.equals(device.address, ignoreCase = true) == true
            }
        }.getOrDefault(false)

    private fun hasConnectPermission(): Boolean = ContextCompat.checkSelfPermission(
        context,
        Manifest.permission.BLUETOOTH_CONNECT
    ) == PackageManager.PERMISSION_GRANTED

    private companion object { const val DIRECT_RELEASE_API = 37 }
}
