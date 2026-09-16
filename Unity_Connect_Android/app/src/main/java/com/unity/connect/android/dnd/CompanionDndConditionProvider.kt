package com.unity.connect.android.dnd

import android.content.ComponentName
import android.content.Context
import android.net.Uri
import android.service.notification.Condition
import android.service.notification.ConditionProviderService

/** Publishes the condition for the app-owned AutomaticZenRule. */
class CompanionDndConditionProvider : ConditionProviderService() {
    override fun onConnected() {
        instance = this
        publishStoredState()
    }

    override fun onDestroy() {
        if (instance === this) instance = null
        super.onDestroy()
    }

    override fun onSubscribe(conditionId: Uri?) {
        if (conditionId == CompanionDndController.CONDITION_ID) publishStoredState()
    }

    override fun onUnsubscribe(conditionId: Uri?) = Unit

    private fun publishStoredState() {
        val active = getSharedPreferences(CompanionDndController.PREFERENCES, Context.MODE_PRIVATE)
            .getBoolean(CompanionDndController.KEY_CONDITION_ACTIVE, false)
        notifyCondition(condition(active))
    }

    companion object {
        @Volatile private var instance: CompanionDndConditionProvider? = null

        fun publish(context: Context, active: Boolean) {
            val appContext = context.applicationContext
            appContext.getSharedPreferences(CompanionDndController.PREFERENCES, Context.MODE_PRIVATE)
                .edit().putBoolean(CompanionDndController.KEY_CONDITION_ACTIVE, active).apply()
            instance?.notifyCondition(condition(active))
                ?: requestRebind(ComponentName(appContext, CompanionDndConditionProvider::class.java))
        }

        private fun condition(active: Boolean) = Condition(
            CompanionDndController.CONDITION_ID,
            if (active) "Requested by Unity Connect" else "Not requested by Unity Connect",
            if (active) Condition.STATE_TRUE else Condition.STATE_FALSE
        )
    }
}
