package com.unity.connect.android.ble

import android.annotation.SuppressLint
import android.bluetooth.*
import android.bluetooth.le.*
import android.content.Context
import android.os.Build
import android.os.ParcelUuid
import com.unity.connect.android.core.FramePipe
import com.unity.connect.android.core.SecureSession
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.util.UUID

@SuppressLint("MissingPermission")
class BleManager(context: Context, private val connected: (FramePipe) -> Unit, private val error: (String) -> Unit) {
    private val manager = context.getSystemService(BluetoothManager::class.java)
    private var server: BluetoothGattServer? = null
    private var device: BluetoothDevice? = null
    private var pipe: BlePipe? = null
    private var mtu = 23
    private val serviceId = UUID.fromString("8ec8b6c0-eb89-4b2a-881b-a5d5a7114b0b")
    private val rxId = UUID.fromString("8ec8b6c1-eb89-4b2a-881b-a5d5a7114b0b")
    private val txId = UUID.fromString("8ec8b6c2-eb89-4b2a-881b-a5d5a7114b0b")
    private val cccdId = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")
    private val appContext = context.applicationContext
    private val advertising = object : AdvertiseCallback() {
        override fun onStartFailure(code: Int) { error("Bluetooth discovery is unavailable. You can still connect over Wi-Fi.") }
    }
    private val callbacks = object : BluetoothGattServerCallback() {
        override fun onServiceAdded(status: Int, service: BluetoothGattService) {
            if (status == BluetoothGatt.GATT_SUCCESS) startAdvertising()
            else error("Bluetooth service could not start.")
        }
        override fun onConnectionStateChange(remote: BluetoothDevice, status: Int, newState: Int) {
            synchronized(this@BleManager) {
                if (newState == BluetoothProfile.STATE_CONNECTED) {
                    if (device != null && device != remote) server?.cancelConnection(remote)
                    else { device = remote; mtu = 23 }
                } else if (remote == device) {
                    val old = pipe; pipe = null; device = null; old?.received?.close(); old?.notification?.complete(false)
                }
                Unit
            }
        }
        override fun onMtuChanged(remote: BluetoothDevice, value: Int) { if (remote == device) mtu = value.coerceIn(23, 512) }
        override fun onDescriptorReadRequest(remote: BluetoothDevice, id: Int, offset: Int, descriptor: BluetoothGattDescriptor) {
            val valid = remote == device && descriptor.uuid == cccdId && offset == 0
            server?.sendResponse(remote, id, if (valid) BluetoothGatt.GATT_SUCCESS else BluetoothGatt.GATT_FAILURE, 0,
                if (valid) { if (pipe != null) byteArrayOf(1, 0) else byteArrayOf(0, 0) } else null)
        }
        override fun onDescriptorWriteRequest(remote: BluetoothDevice, id: Int, descriptor: BluetoothGattDescriptor,
            prepared: Boolean, response: Boolean, offset: Int, value: ByteArray) {
            val valid = remote == device && descriptor.uuid == cccdId && !prepared && offset == 0 &&
                (value.contentEquals(byteArrayOf(1, 0)) || value.contentEquals(byteArrayOf(0, 0)))
            if (response) server?.sendResponse(remote, id, if (valid) BluetoothGatt.GATT_SUCCESS else BluetoothGatt.GATT_FAILURE, 0, null)
            if (!valid) return
            if (value[0].toInt() == 0) pipe?.close()
            else if (pipe == null) { val channel = BlePipe(remote); pipe = channel; connected(channel) }
        }
        override fun onCharacteristicWriteRequest(remote: BluetoothDevice, id: Int, characteristic: BluetoothGattCharacteristic,
            prepared: Boolean, response: Boolean, offset: Int, value: ByteArray) {
            val valid = remote == device && characteristic.uuid == rxId && !prepared && offset == 0 && pipe != null
            val accepted = valid && runCatching { pipe!!.accept(value); true }.getOrDefault(false)
            if (response) server?.sendResponse(remote, id, if (accepted) BluetoothGatt.GATT_SUCCESS else BluetoothGatt.GATT_FAILURE, 0, null)
            if (!accepted && remote == device) pipe?.close()
        }
        override fun onNotificationSent(remote: BluetoothDevice, status: Int) {
            if (remote == device) pipe?.notification?.complete(status == BluetoothGatt.GATT_SUCCESS)
        }
    }
    fun start() {
        if (server != null) return
        try {
            check(manager.adapter?.isEnabled == true)
            server = manager.openGattServer(appContext, callbacks) ?: throw IOException("BLE unavailable")
            val service = BluetoothGattService(serviceId, BluetoothGattService.SERVICE_TYPE_PRIMARY)
            service.addCharacteristic(BluetoothGattCharacteristic(rxId, BluetoothGattCharacteristic.PROPERTY_WRITE,
                BluetoothGattCharacteristic.PERMISSION_WRITE))
            val tx = BluetoothGattCharacteristic(txId, BluetoothGattCharacteristic.PROPERTY_NOTIFY, BluetoothGattCharacteristic.PERMISSION_READ)
            tx.addDescriptor(BluetoothGattDescriptor(cccdId, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
            service.addCharacteristic(tx)
            check(server!!.addService(service))
        } catch (e: Exception) { stop(); error("Bluetooth is unavailable. Wi-Fi pairing is still available.") }
    }
    private fun startAdvertising() {
        try {
            val advertiser = manager.adapter?.bluetoothLeAdvertiser ?: throw IOException("No advertiser")
            val settings = AdvertiseSettings.Builder().setConnectable(true).setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_POWER).build()
            val data = AdvertiseData.Builder().addServiceUuid(ParcelUuid(serviceId)).build()
            val scan = AdvertiseData.Builder().setIncludeDeviceName(true).build()
            advertiser.startAdvertising(settings, data, scan, advertising)
        } catch (e: Exception) { error("Bluetooth discovery is unavailable. Use Wi-Fi to connect.") }
    }
    fun stop() {
        runCatching { manager.adapter?.bluetoothLeAdvertiser?.stopAdvertising(advertising) }
        pipe?.close(); pipe = null; device = null
        runCatching { server?.close() }; server = null
    }
    private inner class BlePipe(private val remote: BluetoothDevice) : FramePipe {
        val received = Channel<ByteArray>(16)
        @Volatile var notification: CompletableDeferred<Boolean>? = null
        private val writes = Mutex()
        private var buffer = ByteArrayOutputStream()
        private var active = false
        private var sequence = 0
        private var started = 0L
        @Synchronized fun accept(part: ByteArray) {
            require(part.size >= 2)
            val header = part[0].toInt() and 255
            if (header and 128 != 0) {
                require(!active); buffer.reset(); active = true; sequence = 0; started = android.os.SystemClock.elapsedRealtime()
            }
            require(active && android.os.SystemClock.elapsedRealtime() - started <= 15000 && (header and 63) == sequence &&
                buffer.size() + part.size - 1 <= SecureSession.MAX_WIRE_BYTES)
            buffer.write(part, 1, part.size - 1); sequence = (sequence + 1) and 63
            if (header and 64 != 0) {
                active = false
                check(received.trySend(buffer.toByteArray()).isSuccess)
            }
        }
        override suspend fun read() = received.receive()
        override suspend fun write(frame: ByteArray) = writes.withLock {
            require(frame.size in 1..SecureSession.MAX_WIRE_BYTES)
            var offset = 0; var seq = 0
            while (offset < frame.size) {
                check(device == remote && pipe === this)
                val count = minOf(mtu - 4, frame.size - offset)
                val header = seq or (if (offset == 0) 128 else 0) or (if (offset + count == frame.size) 64 else 0)
                val fragment = byteArrayOf(header.toByte()) + frame.copyOfRange(offset, offset + count)
                val gatt = server ?: throw IOException("Disconnected")
                val tx = gatt.getService(serviceId).getCharacteristic(txId)
                val ack = CompletableDeferred<Boolean>(); notification = ack
                val sent = if (Build.VERSION.SDK_INT >= 33) gatt.notifyCharacteristicChanged(remote, tx, false, fragment) == BluetoothStatusCodes.SUCCESS
                else {
                    @Suppress("DEPRECATION")
                    tx.value = fragment
                    @Suppress("DEPRECATION")
                    gatt.notifyCharacteristicChanged(remote, tx, false)
                }
                check(sent && withTimeout(5000) { ack.await() }) { "Bluetooth send failed" }
                notification = null; offset += count; seq = (seq + 1) and 63
            }
        }
        override fun close() {
            received.close(); notification?.complete(false)
            runCatching { server?.cancelConnection(remote) }
        }
    }
}
