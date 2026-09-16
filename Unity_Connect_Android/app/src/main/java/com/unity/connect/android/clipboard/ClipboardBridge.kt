package com.unity.connect.android.clipboard

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import com.unity.connect.android.core.ClipboardContent
import com.unity.connect.android.core.MessageCodec
import java.nio.charset.StandardCharsets
import java.util.ArrayDeque
import java.util.UUID

/**
 * Opt-in, text-only clipboard bridge. Android clipboard reads happen only from
 * [captureCurrent], which the foreground UI calls after a direct user tap.
 */
class ClipboardBridge(context: Context) {
    private val appContext = context.applicationContext
    private val manager = appContext.getSystemService(ClipboardManager::class.java)
    private val preferences = appContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
    private val seenIds = LinkedHashSet<String>()
    private val seenOrder = ArrayDeque<String>()

    var enabled: Boolean
        get() = preferences.getBoolean(KEY_ENABLED, false)
        private set(value) { preferences.edit().putBoolean(KEY_ENABLED, value).apply() }

    fun updateEnabled(value: Boolean) {
        enabled = value
        if (!value) {
            seenIds.clear()
            seenOrder.clear()
        }
    }

    /** Must be called only as the immediate result of a visible user action. */
    fun captureCurrent(): Result<ClipboardContent> = runCatching {
        check(enabled) { "Clipboard sync is off" }
        val clip = manager.primaryClip ?: error("The clipboard has no text")
        check(clip.itemCount > 0) { "The clipboard has no text" }
        val text = clip.getItemAt(0).coerceToText(appContext)?.toString().orEmpty()
        validateText(text)
        ClipboardContent(UUID.randomUUID().toString(), text).also { remember(it.updateId) }
    }

    fun applyIncoming(content: ClipboardContent): Boolean {
        if (!enabled || !remember(content.updateId)) return false
        return runCatching {
            validateText(content.text)
            manager.setPrimaryClip(ClipData.newPlainText("Synced from laptop", content.text))
            true
        }.getOrDefault(false)
    }

    private fun validateText(text: String) {
        require(text.isNotEmpty()) { "The clipboard has no text" }
        require('\u0000' !in text) { "The clipboard contains unsupported text" }
        require(text.toByteArray(StandardCharsets.UTF_8).size <= MessageCodec.MAX_CLIPBOARD_TEXT_BYTES) {
            "Clipboard text is too large"
        }
    }

    private fun remember(id: String): Boolean {
        if (!seenIds.add(id)) return false
        seenOrder.addLast(id)
        while (seenOrder.size > MAX_REMEMBERED_IDS) seenIds.remove(seenOrder.removeFirst())
        return true
    }

    companion object {
        private const val PREFERENCES = "unity_clipboard"
        private const val KEY_ENABLED = "enabled"
        private const val MAX_REMEMBERED_IDS = 64
    }
}
