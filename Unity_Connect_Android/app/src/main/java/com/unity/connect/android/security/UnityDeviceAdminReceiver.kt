package com.unity.connect.android.security

import android.app.admin.DeviceAdminReceiver

/** System-owned activation grants only the force-lock policy declared in device_admin.xml. */
class UnityDeviceAdminReceiver : DeviceAdminReceiver()
