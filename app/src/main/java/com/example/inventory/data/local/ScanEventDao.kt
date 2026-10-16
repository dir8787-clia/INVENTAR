package com.example.inventory.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Transaction
import kotlinx.coroutines.flow.Flow

@Dao
interface ScanEventDao {

    @Insert
    suspend fun insert(event: ScanEvent): Long

    /** Массовый вставочный запрос для импорта бэкапа. */
    @Insert
    suspend fun insertAll(events: List<ScanEvent>)

    @Query("SELECT COUNT(*) FROM scan_events WHERE invNumber = :invNumber AND type = :type")
    suspend fun countByNumberAndType(invNumber: String, type: String): Int

    @Query(
        """
        SELECT * FROM scan_events
        ORDER BY timestamp DESC
        LIMIT :limit
        """
    )
    fun observeRecent(limit: Int): Flow<List<ScanEvent>>

    @Query("SELECT * FROM scan_events ORDER BY timestamp DESC")
    fun observeAll(): Flow<List<ScanEvent>>

    @Query("SELECT * FROM scan_events ORDER BY timestamp DESC")
    suspend fun getAllOnce(): List<ScanEvent>

    /** Объединение бэкапа: вставляет только отсутствующие (invNumber+timestamp) пары. */
    @Query(
        """
        SELECT invNumber || '|' || timestamp FROM scan_events
        """
    )
    suspend fun existingKeys(): Set<String>

    @Transaction
    suspend fun mergeEvents(events: List<ScanEvent>): Int {
        val existing = existingKeys()
        val toInsert = events.filter { "${it.invNumber}|${it.timestamp}" !in existing }
        if (toInsert.isNotEmpty()) insertAll(toInsert)
        return toInsert.size
    }

    @Query("DELETE FROM scan_events")
    suspend fun deleteAll()

    @Query(
        """
        SELECT IFNULL(SUM(CASE WHEN type='NORMAL' THEN 1 ELSE 0 END),0) FROM scan_events
        """
    )
    fun observeTotalNormalScans(): Flow<Int>
}
