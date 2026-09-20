package com.unity.connect.android.state

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.provider.Settings

/** Resolves the closest system-owned tethering screen, with public settings fallbacks. */
object HotspotSettings {
    fun intent(context: Context): Intent {
        val actions = listOf(
            "android.settings.TETHER_SETTINGS",
            Settings.ACTION_WIRELESS_SETTINGS,
            Settings.ACTION_SETTINGS
        )
        for (action in actions) {
            val candidate = Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            val resolved = runCatching {
                context.packageManager.queryIntentActivities(candidate, PackageManager.MATCH_SYSTEM_ONLY)
                    .maxByOrNull { it.priority }
            }.getOrNull() ?: continue
            val activity = resolved.activityInfo ?: continue
            return candidate.setClassName(activity.packageName, activity.name)
        }
        return Intent(Settings.ACTION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}
