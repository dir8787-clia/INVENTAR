package com.example.inventory.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Transaction
import kotlinx.coroutines.flow.Flow

@Dao
interface ItemDao {

    @Insert
    suspend fun insert(item: Item): Long

    @Insert
    suspend fun insertAll(items: List<Item>): List<Long>

    /** Все позиции со счётчиками сканов (NORMAL и OVERAGE). */
    @Query(
        """
        SELECT i.*,
               IFNULL((SELECT COUNT(*) FROM scan_events s
                       WHERE s.invNumber = i.invNumber AND s.type = 'NORMAL'), 0) AS scannedQty,
               IFNULL((SELECT COUNT(*) FROM scan_events s
                       WHERE s.invNumber = i.invNumber AND s.type = 'OVERAGE'), 0) AS overageQty
        FROM items i
        ORDER BY i.invNumber COLLATE NOCASE
        """
    )
    fun observeItemsWithCounts(): Flow<List<ItemWithCounts>>

    @Query("SELECT * FROM items WHERE invNumber = :invNumber LIMIT 1")
    suspend fun findByInvNumber(invNumber: String): Item?

    @Query("SELECT COUNT(*) FROM items")
    fun observeItemCount(): Flow<Int>

    @Query("DELETE FROM items")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(items: List<Item>) {
        deleteAll()
        insertAll(items)
    }

    @Query("SELECT * FROM items ORDER BY invNumber COLLATE NOCASE")
    suspend fun getAll(): List<Item>
}
