package com.unity.connect.android

import android.Manifest
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.net.Uri
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.result.IntentSenderRequest
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.unity.connect.android.state.HotspotSettings
import com.unity.connect.android.security.DeviceLockController

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val serviceIntent = Intent(this, ConnectionService::class.java)
        startForegroundService(serviceIntent)

        setContent {
            val colors = if (isSystemInDarkTheme()) {
                darkColorScheme(
                    primary = Color(0xFF8CCBFF),
                    background = Color(0xFF101214),
                    surface = Color(0xFF181B1F)
                )
            } else {
                lightColorScheme(
                    primary = Color(0xFF1677C8),
                    background = Color.White,
                    surface = Color(0xFFF5F8FB)
                )
            }
            MaterialTheme(colorScheme = colors) {
                Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    AppScreen()
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        ConnectionService.refreshDndState()
    }
}

@Composable
fun AppScreen(viewModel: AppViewModel = viewModel()) {
    val uiState by viewModel.uiState.collectAsState()
    val context = LocalContext.current
    val runtimePermissions = remember {
        buildList {
            add(Manifest.permission.BLUETOOTH_CONNECT)
            add(Manifest.permission.BLUETOOTH_ADVERTISE)
            add(Manifest.permission.READ_PHONE_STATE)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) add(Manifest.permission.POST_NOTIFICATIONS)
        }
    }
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { ConnectionService.refreshDndState() }
    val associationLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartIntentSenderForResult()
    ) { ConnectionService.refreshDndState() }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Text("Unity Connect", style = MaterialTheme.typography.headlineMedium)
        Text("Your phone and laptop, connected.", color = MaterialTheme.colorScheme.onSurfaceVariant)

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                Text("Connection", style = MaterialTheme.typography.titleMedium)
                Text(
                    when (uiState.connectionState) {
                        ConnectionState.CONNECTED -> when {
                            uiState.wifiRouteConnected && uiState.bleRouteConnected -> "Connected over Wi-Fi + Bluetooth"
                            uiState.wifiRouteConnected -> "Connected over Wi-Fi"
                            uiState.bleRouteConnected -> "Connected over Bluetooth"
                            else -> "Connected"
                        }
                        ConnectionState.CONNECTING -> "Looking for your laptop"
                        ConnectionState.DISCONNECTED -> "Not connected"
                    }
                )
                if (uiState.pairedPcName != null) Text(uiState.pairedPcName!!, color = MaterialTheme.colorScheme.onSurfaceVariant)
                uiState.wifiAddress?.let { Text("Wi-Fi address: $it", style = MaterialTheme.typography.bodySmall) }
            }
        }

        AccessContent(uiState.access,
            requestRuntime = { permissionLauncher.launch(runtimePermissions.toTypedArray()) }, context = context)

        if (!uiState.isPaired) {
            PairingContent(uiState, viewModel)
        } else {
            if (uiState.connectionState != ConnectionState.CONNECTED) {
                Button(onClick = viewModel::startPairing) { Text("Reconnect") }
            }
            CompanionControls(uiState, viewModel) {
                viewModel.associateCurrentAudioDevice { sender ->
                    associationLauncher.launch(IntentSenderRequest.Builder(sender).build())
                }
            }
            Button(
                onClick = viewModel::forgetDevice,
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
            ) { Text("Forget laptop") }
        }

        uiState.featureNotice?.let {
            Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable
private fun PairingContent(uiState: UiState, viewModel: AppViewModel) {
    if (uiState.sasCode == null) {
        Button(onClick = viewModel::startPairing) { Text("Start pairing") }
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(18.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("Confirm this code on both devices", style = MaterialTheme.typography.titleMedium)
                Text(uiState.sasCode, style = MaterialTheme.typography.displayMedium)
                if (uiState.awaitingOtherDevice) Text("Waiting for confirmation on your laptop…")
                else {
                    Button(onClick = viewModel::acceptSasCode) { Text("Codes match") }
                    Button(onClick = { ConnectionService.rejectPairing() }) { Text("Cancel") }
                }
            }
        }
    }
}

@Composable
private fun CompanionControls(uiState: UiState, viewModel: AppViewModel, associateHeadphones: () -> Unit) {
    val context = LocalContext.current
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Text("Device security", style = MaterialTheme.typography.titleMedium)
            Text(
                "Lock your trusted laptop from this phone. Unlock always uses the laptop's normal sign-in.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            Button(
                onClick = viewModel::lockLaptop,
                enabled = uiState.connectionState == ConnectionState.CONNECTED
            ) { Text("Lock laptop") }
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
            Text("Audio output", style = MaterialTheme.typography.titleMedium)
            Text(uiState.bluetoothAudioName ?: "Phone")
            Text(
                when {
                    uiState.bluetoothAudioName == null -> "No Bluetooth audio device connected"
                    uiState.bluetoothAudioCanRelease -> "One-tap laptop handoff is ready"
                    else -> "Guided laptop handoff is available"
                },
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            if (Build.VERSION.SDK_INT >= 37 && uiState.bluetoothAudioName != null &&
                !uiState.bluetoothAudioCanRelease) {
                Button(onClick = associateHeadphones) { Text("Enable one-tap handoff") }
            }
        }
    }

    if (uiState.laptopAudioStreaming || uiState.laptopAudioNotice != null) {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                Text("Laptop audio", style = MaterialTheme.typography.titleMedium)
                Text(
                    uiState.laptopAudioNotice ?: "Playing laptop audio",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.bodySmall
                )
                if (uiState.laptopAudioStreaming) {
                    Button(onClick = viewModel::stopLaptopAudio) { Text("Stop") }
                }
            }
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Text("Phone internet", style = MaterialTheme.typography.titleMedium)
            Text(
                "Turn on Android internet tethering when your laptop asks. A local-only hotspot is not used.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            Button(onClick = { context.startActivity(HotspotSettings.intent(context)) }) {
                Text("Open tethering settings")
            }
        }
    }

    val media = uiState.pcMedia
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Text("Laptop media", style = MaterialTheme.typography.titleMedium)
            Text(
                media?.source ?: "Windows media",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            Text(media?.title ?: "Nothing playing", style = MaterialTheme.typography.titleLarge)
            Text(
                media?.artist ?: if (uiState.connectionState == ConnectionState.CONNECTED)
                    "Play something on your laptop" else "Connect your laptop to see media",
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = { viewModel.sendPcMediaCommand("previous_track") },
                    enabled = media?.capabilities?.previousTrack == true,
                    modifier = Modifier.weight(1f)
                ) { Text("Previous") }
                Button(
                    onClick = { viewModel.sendPcMediaCommand("play_pause") },
                    enabled = media?.capabilities?.playPause == true,
                    modifier = Modifier.weight(1f)
                ) { Text(if (media?.isPlaying == true) "Pause" else "Play") }
                Button(
                    onClick = { viewModel.sendPcMediaCommand("next_track") },
                    enabled = media?.capabilities?.nextTrack == true,
                    modifier = Modifier.weight(1f)
                ) { Text("Next") }
            }
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("Companion DND", style = MaterialTheme.typography.titleMedium)
                    Text(
                        "Controls only Unity Connect’s automatic rule.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
                Switch(
                    checked = uiState.companionDndActive,
                    onCheckedChange = viewModel::setCompanionDndActive,
                    enabled = uiState.canControlCompanionDnd
                )
            }
            Text(
                "Turning this off leaves manual DND and other rules alone.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("Clipboard sync", style = MaterialTheme.typography.titleMedium)
                    Text(
                        "New text only · No history",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
                Switch(
                    checked = uiState.clipboardSyncEnabled,
                    onCheckedChange = viewModel::setClipboardSyncEnabled
                )
            }
            Text(
                "Text from your laptop is copied while sync is on. Android reads your clipboard only when you tap send.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall
            )
            Button(
                onClick = viewModel::sendCurrentClipboard,
                enabled = uiState.clipboardSyncEnabled && uiState.connectionState == ConnectionState.CONNECTED
            ) { Text("Send current clipboard") }
        }
    }

    Spacer(modifier = Modifier.height(2.dp))
}

@Composable
private fun AccessContent(access: AccessState, requestRuntime: () -> Unit, context: Context) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(9.dp)) {
            Text("Access", style = MaterialTheme.typography.titleMedium)
            AccessRow("Nearby devices", access.nearbyDevices, "Needed for Bluetooth")
            AccessRow("Bluetooth", access.bluetoothEnabled, "Turned off")
            AccessRow("Notifications", access.notifications, "Needed for connection status")
            AccessRow("Phone status", access.phoneState, "Optional")
            AccessRow("Media sessions", access.mediaSessions, "Optional")
            AccessRow("Do Not Disturb", access.dndPolicy, "Optional")
            AccessRow("Phone brightness control", access.brightnessControl, "Optional")
            AccessRow("Device lock", access.deviceLock, "Optional")
            if (!access.nearbyDevices || !access.notifications || !access.phoneState) {
                Button(onClick = requestRuntime) { Text("Review device permissions") }
            }
            if (!access.mediaSessions) {
                Button(onClick = {
                    context.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
                }) { Text("Allow media access") }
            }
            if (!access.dndPolicy) {
                Button(onClick = {
                    context.startActivity(Intent(Settings.ACTION_NOTIFICATION_POLICY_ACCESS_SETTINGS))
                }) { Text("Allow DND access") }
            }
            if (!access.brightnessControl) {
                Button(onClick = {
                    context.startActivity(Intent(
                        Settings.ACTION_MANAGE_WRITE_SETTINGS,
                        Uri.parse("package:${context.packageName}")
                    ))
                }) { Text("Allow brightness control") }
            }
            if (!access.deviceLock) {
                Button(onClick = {
                    context.startActivity(DeviceLockController(context).activationIntent())
                }) { Text("Allow device lock") }
            }
        }
    }
}

@Composable
private fun AccessRow(label: String, granted: Boolean, missingLabel: String) {
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label)
        Text(if (granted) "Available" else missingLabel,
            color = if (granted) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
            style = MaterialTheme.typography.bodySmall)
    }
}
