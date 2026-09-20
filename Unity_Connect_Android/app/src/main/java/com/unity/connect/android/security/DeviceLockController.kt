package com.unity.connect.android.security

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent

class DeviceLockController(private val context: Context) {
    private val policy = context.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(context, UnityDeviceAdminReceiver::class.java)

    val enabled: Boolean get() = policy.isAdminActive(admin)

    fun activationIntent(): Intent = Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN).apply {
        putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, admin)
        putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION,
            "Allows Unity Connect to lock this phone when you choose Lock phone on your trusted laptop.")
    }

    fun lockNow(): Boolean {
        if (!enabled) return false
        return runCatching { policy.lockNow(); true }.getOrDefault(false)
    }
}
