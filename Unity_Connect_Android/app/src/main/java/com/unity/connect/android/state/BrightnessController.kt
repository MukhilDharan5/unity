package com.unity.connect.android.state

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.database.ContentObserver
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import androidx.core.content.ContextCompat
import com.unity.connect.android.core.BrightnessState
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.round
import kotlin.math.roundToInt
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged

class BrightnessController(private val context: Context) {
    val state: Flow<BrightnessState?> = callbackFlow {
        val resolver = context.contentResolver
        val sensors = context.getSystemService(SensorManager::class.java)
        val lightSensor = sensors.getDefaultSensor(Sensor.TYPE_LIGHT)
        val proximitySensor = sensors.getDefaultSensor(Sensor.TYPE_PROXIMITY)
        val power = context.getSystemService(PowerManager::class.java)
        var level: Int? = null
        var adaptive = false
        var filteredLux: Double? = null
        var publishedLux: Double? = null
        var proximityNear = false
        var screenOn = power.isInteractive

        fun readSettings(): Boolean = runCatching {
            val raw = Settings.System.getInt(resolver, Settings.System.SCREEN_BRIGHTNESS)
            level = ((raw.coerceIn(0, 255) / 255.0) * 100.0).roundToInt().coerceIn(0, 100)
            adaptive = Settings.System.getInt(
                resolver,
                Settings.System.SCREEN_BRIGHTNESS_MODE,
                Settings.System.SCREEN_BRIGHTNESS_MODE_MANUAL
            ) == Settings.System.SCREEN_BRIGHTNESS_MODE_AUTOMATIC
            true
        }.getOrDefault(false)

        fun emitState() {
            val currentLevel = level
            if (currentLevel == null) { trySend(null); return }
            val lux = publishedLux
            val covered = lightSensor != null &&
                ((proximityNear && (lux == null || lux < 5.0)) || (!screenOn && (lux == null || lux < 1.0)))
            val status = when {
                covered -> "covered"
                lightSensor == null || lux == null -> "unavailable"
                else -> "valid"
            }
            trySend(
                BrightnessState(
                    level = currentLevel,
                    adaptive = adaptive,
                    canControl = Settings.System.canWrite(context),
                    ambientLux = if (status == "valid") round(lux!! * 10.0) / 10.0 else null,
                    ambientStatus = status
                )
            )
        }

        val settingsObserver = object : ContentObserver(Handler(Looper.getMainLooper())) {
            override fun onChange(selfChange: Boolean) {
                readSettings()
                emitState()
            }
        }
        val sensorListener = object : SensorEventListener {
            override fun onSensorChanged(event: SensorEvent) {
                when (event.sensor.type) {
                    Sensor.TYPE_LIGHT -> {
                        val raw = event.values.firstOrNull()?.toDouble()?.coerceIn(0.0, 200000.0) ?: return
                        val next = filteredLux?.let { it * 0.8 + raw * 0.2 } ?: raw
                        filteredLux = next
                        val previous = publishedLux
                        if (previous == null || abs(next - previous) >= max(1.0, previous * 0.08)) {
                            publishedLux = next
                            emitState()
                        }
                    }
                    Sensor.TYPE_PROXIMITY -> {
                        val value = event.values.firstOrNull() ?: return
                        val near = value < event.sensor.maximumRange && value < 5f
                        if (near != proximityNear) {
                            proximityNear = near
                            emitState()
                        }
                    }
                }
            }
            override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit
        }
        val screenReceiver = object : BroadcastReceiver() {
            override fun onReceive(receiverContext: Context?, intent: Intent?) {
                screenOn = intent?.action != Intent.ACTION_SCREEN_OFF
                emitState()
            }
        }

        resolver.registerContentObserver(Settings.System.getUriFor(Settings.System.SCREEN_BRIGHTNESS), false, settingsObserver)
        resolver.registerContentObserver(Settings.System.getUriFor(Settings.System.SCREEN_BRIGHTNESS_MODE), false, settingsObserver)
        if (lightSensor != null) sensors.registerListener(sensorListener, lightSensor, SensorManager.SENSOR_DELAY_NORMAL)
        if (proximitySensor != null) sensors.registerListener(sensorListener, proximitySensor, SensorManager.SENSOR_DELAY_NORMAL)
        ContextCompat.registerReceiver(context, screenReceiver,
            IntentFilter().apply { addAction(Intent.ACTION_SCREEN_ON); addAction(Intent.ACTION_SCREEN_OFF) },
            ContextCompat.RECEIVER_EXPORTED)
        readSettings()
        emitState()

        awaitClose {
            runCatching { resolver.unregisterContentObserver(settingsObserver) }
            runCatching { sensors.unregisterListener(sensorListener) }
            runCatching { context.unregisterReceiver(screenReceiver) }
        }
    }.distinctUntilChanged()

    fun apply(level: Int?, adaptive: Boolean?): Boolean {
        if (!Settings.System.canWrite(context) || (level == null && adaptive == null)) return false
        return runCatching {
            var changed = true
            if (adaptive != null) changed = Settings.System.putInt(
                context.contentResolver,
                Settings.System.SCREEN_BRIGHTNESS_MODE,
                if (adaptive) Settings.System.SCREEN_BRIGHTNESS_MODE_AUTOMATIC
                else Settings.System.SCREEN_BRIGHTNESS_MODE_MANUAL
            ) && changed
            if (level != null) changed = Settings.System.putInt(
                context.contentResolver,
                Settings.System.SCREEN_BRIGHTNESS,
                ((level.coerceIn(1, 100) / 100.0) * 255.0).roundToInt().coerceIn(1, 255)
            ) && changed
            changed
        }.getOrDefault(false)
    }
}
