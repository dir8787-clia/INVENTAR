package com.example.inventory.data.local

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

object ScanType {
    const val NORMAL = "NORMAL"
    const val OVERAGE = "OVERAGE"
}

object ScanSource {
    const val CAMERA = "CAMERA"
    const val MANUAL = "MANUAL"
}

/** Событие сканирования/учёта единицы оборудования. */
@Entity(
    tableName = "scan_events",
    indices = [Index("invNumber"), Index("timestamp")]
)
data class ScanEvent(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val invNumber: String,   // нормализованный номер
    val timestamp: Long,     // System.currentTimeMillis()
    val type: String,        // NORMAL | OVERAGE
    val source: String       // CAMERA | MANUAL
)
