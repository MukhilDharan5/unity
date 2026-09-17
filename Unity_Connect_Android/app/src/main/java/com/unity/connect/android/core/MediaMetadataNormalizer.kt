package com.unity.connect.android.core

/** Normalizes platform-provided text before it enters a protocol state object. */
object MediaMetadataNormalizer {
    const val MAX_SOURCE_UNITS = 80
    const val MAX_METADATA_UNITS = 256

    fun normalize(value: String?, maxUnits: Int = MAX_METADATA_UNITS): String? {
        require(maxUnits > 0)
        if (value == null) return null
        // Work only within the bounded prefix; never split a UTF-16 surrogate pair at its end.
        var end = minOf(value.length, maxUnits)
        if (end < value.length && end > 0 && value[end - 1].isHighSurrogate() && value[end].isLowSurrogate()) end--
        return value.substring(0, end).map { if (Character.isISOControl(it)) ' ' else it }
            .joinToString("").trim().takeUnless(String::isBlank)
    }
}
