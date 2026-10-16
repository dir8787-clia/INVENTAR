package com.example.inventory.data.local

import androidx.room.Embedded

/** Позиция + агрегированные счётчики (обёртка для запросов с JOIN). */
data class ItemWithCounts(
    @Embedded val item: Item,
    val scannedQty: Int,
    val overageQty: Int
) {
    val expectedQty: Int get() = item.expectedQty

    /** Статус позиции для отчётов и фильтров. */
    fun status(): ItemStatus = when {
        scannedQty == 0 && overageQty > 0 -> ItemStatus.OVERAGE_ONLY
        scannedQty == 0 -> ItemStatus.MISSING
        scannedQty < expectedQty -> ItemStatus.PARTIAL
        overageQty > 0 -> ItemStatus.FOUND_WITH_OVERAGE
        else -> ItemStatus.FOUND
    }
}

enum class ItemStatus {
    FOUND,              // найдено полностью (scanned == expected, без излишков)
    PARTIAL,            // частично (0 < scanned < expected)
    MISSING,            // пропало (scanned == 0, излишков нет)
    OVERAGE_ONLY,       // только излишки (в базе есть, но normal-сканов 0)
    FOUND_WITH_OVERAGE; // найдено + излишки
}
