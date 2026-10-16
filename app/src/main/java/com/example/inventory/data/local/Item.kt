package com.example.inventory.data.local

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * Позиция оборудования. invNumber хранится в НОРМАЛИЗОВАННОМ виде
 * (см. util/InvNormalizer.kt) — единая нормализация для импорта/скана/ручного ввода.
 */
@Entity(
    tableName = "items",
    indices = [Index(value = ["invNumber"], unique = true)]
)
data class Item(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val invNumber: String,
    val name: String,
    val unit: String,
    val expectedQty: Int
)
