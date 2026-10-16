package com.example.inventory.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.inventory.data.local.ItemStatus
import com.example.inventory.data.local.ItemWithCounts
import com.example.inventory.data.local.ScanEvent
import com.example.inventory.data.repository.InventoryRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn

data class ReportUiState(
    val items: List<ItemWithCounts> = emptyList(),
    val scans: List<ScanEvent> = emptyList(),
    val totalUnits: Int = 0,
    val foundUnits: Int = 0,
    val missingUnits: Int = 0,
    val partialPositions: Int = 0,
    val overageUnits: Int = 0,
    val isDbEmpty: Boolean = false
)

class ReportViewModel(app: Application) : AndroidViewModel(app) {

    private val repo = InventoryRepository(app)

    private val showLog = MutableStateFlow(false)

    val state: StateFlow<ReportUiState> =
        combine(repo.observeItemsWithCounts(), repo.observeAllScans(), showLog) { items, scans, _ ->
            val total = items.sumOf { it.expectedQty }
            val found = items.sumOf { it.scannedQty.coerceAtMost(it.expectedQty) }
            val missing = items.filter { it.status() == ItemStatus.MISSING }
                .sumOf { it.expectedQty }
            val partial = items.count { it.status() == ItemStatus.PARTIAL }
            val overage = items.sumOf { it.overageQty }
            ReportUiState(
                items = items,
                scans = scans,
                totalUnits = total,
                foundUnits = found,
                missingUnits = missing,
                partialPositions = partial,
                overageUnits = overage,
                isDbEmpty = items.isEmpty()
            )
        }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), ReportUiState())

    fun toggleLog() { showLog.value = !showLog.value }
}
