package com.example.inventory.data.repository

import android.content.Context
import android.net.Uri
import com.example.inventory.data.local.AppDatabase
import com.example.inventory.data.local.Item
import com.example.inventory.data.local.ItemWithCounts
import com.example.inventory.data.local.ScanEvent
import com.example.inventory.data.local.ScanType
import com.example.inventory.util.ExcelImporter
import com.example.inventory.util.ImportResult
import com.example.inventory.util.InvNormalizer
import com.example.inventory.util.JsonBackup
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.withContext

/** Результат обработки одного скана для UI. */
sealed class ScanOutcome {
    /** Код не найден в базе. */
    data class NotFound(val normalized: String) : ScanOutcome()

    /** Учёт единицы: scanned стал k из N (k < N — частично, k == N — полностью). */
    data class Found(
        val item: Item,
        val newScanned: Int,
        val fullyCounted: Boolean
    ) : ScanOutcome()

    /** Требуется подтверждение пользователя: лимит достигнут → излишек? */
    data class NeedOverageConfirm(val item: Item, val expected: Int) : ScanOutcome()

    /** Излишек записан: всего overageQty. */
    data class OverageAdded(val item: Item, val totalOverage: Int) : ScanOutcome()

    /** Пустой/некорректный код после нормализации. */
    object EmptyCode : ScanOutcome()
}

/**
 * Центральный репозиторий: Room + бизнес-логика сканирования/импорта/бэкапов.
 */
class InventoryRepository(private val context: Context) {

    private val db = AppDatabase.get(context)
    private val itemDao = db.itemDao()
    private val scanDao = db.scanEventDao()

    // --- Наблюдаемые потоки ---
    fun observeItemsWithCounts(): Flow<List<ItemWithCounts>> = itemDao.observeItemsWithCounts()
    fun observeItemCount(): Flow<Int> = itemDao.observeItemCount()
    fun observeRecentScans(limit: Int = 20): Flow<List<ScanEvent>> = scanDao.observeRecent(limit)
    fun observeAllScans(): Flow<List<ScanEvent>> = scanDao.observeAll()

    // --- Сканирование ---

    /**
     * Обрабатывает сырой код X (из камеры или ручного ввода).
     * Нормализует, сверяет с базой и пишет ScanEvent по правилам ТЗ п.6.
     */
    suspend fun processCode(raw: String, source: String): ScanOutcome = withContext(Dispatchers.IO) {
        val x = InvNormalizer.normalize(raw)
        if (x.isEmpty()) return@withContext ScanOutcome.EmptyCode

        val item = itemDao.findByInvNumber(x)
            ?: return@withContext ScanOutcome.NotFound(x)

        val normalCount = scanDao.countByNumberAndType(x, ScanType.NORMAL)
        if (normalCount < item.expectedQty) {
            scanDao.insert(ScanEvent(invNumber = x, timestamp = System.currentTimeMillis(),
                type = ScanType.NORMAL, source = source))
            ScanOutcome.Found(item, newScanned = normalCount + 1,
                fullyCounted = normalCount + 1 >= item.expectedQty)
        } else {
            // лимит достигнут — нужен пользовательский выбор (модалка «Добавить как ИЗЛИШЕК?»)
            ScanOutcome.NeedOverageConfirm(item, item.expectedQty)
        }
    }

    /** Подтверждённое добавление излишка (после модалки). */
    suspend fun confirmOverage(normalizedNumber: String, source: String): ScanOutcome =
        withContext(Dispatchers.IO) {
            val x = InvNormalizer.normalize(normalizedNumber)
            val item = itemDao.findByInvNumber(x)
                ?: return@withContext ScanOutcome.NotFound(x)
            scanDao.insert(ScanEvent(invNumber = x, timestamp = System.currentTimeMillis(),
                type = ScanType.OVERAGE, source = source))
            val total = scanDao.countByNumberAndType(x, ScanType.OVERAGE)
            ScanOutcome.OverageAdded(item, total)
        }

    // --- Импорт Excel ---

    suspend fun importExcel(uri: Uri): ImportResult = withContext(Dispatchers.IO) {
        val result = ExcelImporter.import(context, uri)
        // замена базы: старые сканы удаляются
        itemDao.replaceAll(result.items.map { it.copy(id = 0) })
        scanDao.deleteAll()
        result
    }

    // --- Бэкапы JSON ---

    suspend fun exportBackup(uri: Uri) = withContext(Dispatchers.IO) {
        val items = itemDao.getAll()
        val events = scanDao.getAllOnce()
        JsonBackup.export(context, uri, items, events)
    }

    /** Режимы импорта бэкапа. */
    enum class BackupMode { REPLACE_ALL, MERGE_SCANS }

    suspend fun importBackup(uri: Uri, mode: BackupMode): Int = withContext(Dispatchers.IO) {
        val data = JsonBackup.parse(context, uri)
        when (mode) {
            BackupMode.REPLACE_ALL -> {
                itemDao.replaceAll(data.items.map { it.copy(id = 0) })
                scanDao.deleteAll()
                scanDao.insertAll(data.scanEvents.map { it.copy(id = 0) })
                data.items.size
            }
            BackupMode.MERGE_SCANS -> {
                // объединение сканов по invNumber+timestamp без дублей; позиции не трогаем
                scanDao.mergeEvents(data.scanEvents.map { it.copy(id = 0) })
            }
        }
    }

    // --- Очистка ---

    suspend fun clearScans() = withContext(Dispatchers.IO) { scanDao.deleteAll() }
    suspend fun clearAll() = withContext(Dispatchers.IO) {
        itemDao.deleteAll(); scanDao.deleteAll()
    }

    suspend fun getAllItemsOnce(): List<Item> = withContext(Dispatchers.IO) { itemDao.getAll() }
}
