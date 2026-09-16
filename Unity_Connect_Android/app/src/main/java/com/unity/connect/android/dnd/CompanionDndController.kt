package com.unity.connect.android.dnd

import android.app.AutomaticZenRule
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import androidx.core.content.ContextCompat
import com.unity.connect.android.core.DndState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Owns only Unity Connect's automatic DND rule. It never changes global/manual DND. */
class CompanionDndController(context: Context) : AutoCloseable {
    private val appContext = context.applicationContext
    private val manager = appContext.getSystemService(NotificationManager::class.java)
    private val preferences = appContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(readState())
    val state: StateFlow<DndState?> = _state.asStateFlow()

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) = refresh()
    }

    init {
        val filter = IntentFilter().apply {
            addAction(NotificationManager.ACTION_INTERRUPTION_FILTER_CHANGED)
            addAction(NotificationManager.ACTION_NOTIFICATION_POLICY_CHANGED)
            addAction(NotificationManager.ACTION_NOTIFICATION_POLICY_ACCESS_GRANTED_CHANGED)
        }
        ContextCompat.registerReceiver(appContext, receiver, filter, ContextCompat.RECEIVER_EXPORTED)
    }

    fun refresh() {
        _state.value = readState()
    }

    fun setCompanionRuleActive(active: Boolean): Boolean {
        if (!manager.isNotificationPolicyAccessGranted) {
            refresh()
            return false
        }
        val ruleId = ensureRule() ?: return false
        if (active) {
            val rule = runCatching { manager.getAutomaticZenRule(ruleId) }.getOrNull() ?: return false
            if (!rule.isEnabled) {
                rule.isEnabled = true
                if (!runCatching { manager.updateAutomaticZenRule(ruleId, rule) }.getOrDefault(false)) return false
            }
        }
        preferences.edit().putBoolean(KEY_CONDITION_ACTIVE, active).apply()
        CompanionDndConditionProvider.publish(appContext, active)
        refresh()
        return true
    }

    private fun readState(): DndState? {
        val filter = runCatching { manager.currentInterruptionFilter }.getOrNull() ?: return null
        if (filter == NotificationManager.INTERRUPTION_FILTER_UNKNOWN) return null
        val effectiveDnd = filter != NotificationManager.INTERRUPTION_FILTER_ALL
        if (!manager.isNotificationPolicyAccessGranted) return DndState(effectiveDnd)

        val ruleId = ensureRule()
        val rule = ruleId?.let { runCatching { manager.getAutomaticZenRule(it) }.getOrNull() }
        val active = rule?.isEnabled == true && preferences.getBoolean(KEY_CONDITION_ACTIVE, false)
        return DndState(effectiveDnd, active, rule != null)
    }

    @Suppress("DEPRECATION")
    private fun ensureRule(): String? {
        if (!manager.isNotificationPolicyAccessGranted) return null
        val savedRuleId = preferences.getString(KEY_RULE_ID, null)
        if (savedRuleId != null && runCatching { manager.getAutomaticZenRule(savedRuleId) }.getOrNull() != null) {
            return savedRuleId
        }

        preferences.edit().remove(KEY_RULE_ID).putBoolean(KEY_CONDITION_ACTIVE, false).apply()
        val rule = AutomaticZenRule(
            RULE_NAME,
            ComponentName(appContext, CompanionDndConditionProvider::class.java),
            CONDITION_ID,
            NotificationManager.INTERRUPTION_FILTER_PRIORITY,
            true
        )
        val newRuleId = runCatching { manager.addAutomaticZenRule(rule) }.getOrNull() ?: return null
        preferences.edit().putString(KEY_RULE_ID, newRuleId).apply()
        CompanionDndConditionProvider.publish(appContext, false)
        return newRuleId
    }

    override fun close() {
        runCatching { appContext.unregisterReceiver(receiver) }
    }

    companion object {
        internal const val PREFERENCES = "unity_companion_dnd"
        internal const val KEY_CONDITION_ACTIVE = "condition_active"
        private const val KEY_RULE_ID = "rule_id"
        private const val RULE_NAME = "Unity Connect"
        internal val CONDITION_ID: Uri = Uri.parse("condition://com.unity.connect.android/companion_dnd")
    }
}
