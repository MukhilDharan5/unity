package com.unity.connect.android

import com.unity.connect.android.core.BatteryState
import com.unity.connect.android.core.Capabilities
import com.unity.connect.android.core.CellularState
import org.junit.Assert.*
import org.junit.Test

class NormalizationTest {

    @Test
    fun testBatteryClamping() {
        val levelOver = 150.coerceIn(0, 100)
        assertEquals(100, levelOver)

        val levelUnder = (-20).coerceIn(0, 100)
        assertEquals(0, levelUnder)

        val validLevel = 72.coerceIn(0, 100)
        assertEquals(72, validLevel)
    }

    @Test
    fun testCellularCategoryMapping() {
        // Valid categories
        val validNetworks = listOf("unknown", "cellular", "2g", "3g", "4g", "5g")
        val validSignals = listOf("unknown", "none", "poor", "fair", "good", "excellent")

        val state = CellularState(
            isUsingCellularData = true,
            isUsingWifi = false,
            network = "5g",
            signal = "excellent"
        )

        assertTrue(validNetworks.contains(state.network))
        assertTrue(validSignals.contains(state.signal))
        assertTrue(state.isUsingCellularData)
        assertFalse(state.isUsingWifi == true)
    }

    @Test
    fun testCapabilitiesMapping() {
        val caps = Capabilities(playPause = true, nextTrack = true, previousTrack = false)
        assertTrue(caps.playPause)
        assertTrue(caps.nextTrack)
        assertFalse(caps.previousTrack)
    }
}
