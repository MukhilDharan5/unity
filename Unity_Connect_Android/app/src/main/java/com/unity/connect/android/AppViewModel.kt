package com.unity.connect.android

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn

class AppViewModel : ViewModel() {

    // Expose the static state flow from ConnectionService
    val uiState: StateFlow<UiState> = ConnectionService.serviceState.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = ConnectionService.serviceState.value
    )

    fun startPairing() {
        ConnectionService.startPairing()
    }

    fun acceptSasCode() {
        ConnectionService.acceptSasCode()
    }

    fun forgetDevice() {
        ConnectionService.forgetDevice()
    }

    fun setClipboardSyncEnabled(enabled: Boolean) {
        ConnectionService.setClipboardSyncEnabled(enabled)
    }

    fun sendCurrentClipboard() {
        ConnectionService.sendCurrentClipboard()
    }

    fun sendPcMediaCommand(command: String) {
        ConnectionService.sendPcMediaCommand(command)
    }

    fun setCompanionDndActive(active: Boolean) {
        ConnectionService.setCompanionDndActive(active)
    }

    fun refreshDndState() {
        ConnectionService.refreshDndState()
    }
}
