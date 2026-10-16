package com.example.inventory.ui

import android.app.Application
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.inventory.data.repository.InventoryRepository
import com.example.inventory.util.ImportResult
import com.example.inventory.util.PdfLabelPrinter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class SettingsUiState(
    val message: String? = null,
    val busy: Boolean = false,
    val importResult: ImportResult? = null,
    val labelPreviewInfo: String? = null // «N этикеток, M страниц»
)

class SettingsViewModel(app: Application) : AndroidViewModel(app) {

    private val repo = InventoryRepository(app)
    private val ctx get() = getApplication<android.app.Application>()

    private val _state = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = _state.asStateFlow()

    fun consumeMessage() {
        _state.value = _state.value.copy(message = null, importResult = null, labelPreviewInfo = null)
    }

    // ---------- Импорт Excel ----------

    fun onExcelSelected(uri: Uri) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                val result = repo.importExcel(uri)
                _state.value = _state.value.copy(
                    busy = false,
                    importResult = result,
                    message = "Загружено ${result.items.size}, пропущено ${result.skipped}"
                )
            } catch (e: Exception) {
                _state.value = _state.value.copy(
                    busy = false,
                    message = "Ошибка импорта: ${e.message}"
                )
            }
        }
    }

    // ---------- Печать этикеток ----------

    fun prepareLabels(previewOnly: Boolean) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                val items = repo.getAllItemsOnce()
                if (items.isEmpty()) {
                    _state.value = _state.value.copy(
                        busy = false, message = "База пуста — сначала импортируйте Excel"
                    )
                    return@launch
                }
                val labels = PdfLabelPrinter.buildLabels(items)
                val info = "${labels.size} этикеток, ${PdfLabelPrinter.totalPages(labels)} стр."
                if (previewOnly) {
                    withContext(Dispatchers.IO) {
                        PdfLabelPrinter.saveToCache(ctx, labels, PdfLabelPrinter.defaultFileName())
                    }
                    _state.value = _state.value.copy(busy = false, labelPreviewInfo = info)
                } else {
                    _state.value = _state.value.copy(busy = false, labelPreviewInfo = info)
                    withContext(Dispatchers.Main) {
                        PdfLabelPrinter.print(ctx, labels, "Этикетки Lomond 2100195")
                    }
                }
            } catch (e: Exception) {
                _state.value = _state.value.copy(busy = false, message = "Ошибка: ${e.message}")
            }
        }
    }

    fun savePdfToUri(uri: Uri) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                val items = repo.getAllItemsOnce()
                val labels = PdfLabelPrinter.buildLabels(items)
                withContext(Dispatchers.IO) { PdfLabelPrinter.saveToUri(ctx, labels, uri) }
                _state.value = _state.value.copy(busy = false, message = "PDF сохранён")
            } catch (e: Exception) {
                _state.value = _state.value.copy(busy = false, message = "Ошибка: ${e.message}")
            }
        }
    }

    // ---------- Бэкапы ----------

    fun exportBackup(uri: Uri) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                repo.exportBackup(uri)
                _state.value = _state.value.copy(busy = false, message = "Бэкап экспортирован")
            } catch (e: Exception) {
                _state.value = _state.value.copy(busy = false, message = "Ошибка: ${e.message}")
            }
        }
    }

    fun importBackup(uri: Uri, replaceAll: Boolean) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                val mode = if (replaceAll)
                    InventoryRepository.BackupMode.REPLACE_ALL
                else InventoryRepository.BackupMode.MERGE_SCANS
                val n = repo.importBackup(uri, mode)
                _state.value = _state.value.copy(
                    busy = false,
                    message = if (replaceAll) "Восстановлено позиций: $n"
                    else "Добавлено сканов: $n"
                )
            } catch (e: Exception) {
                _state.value = _state.value.copy(busy = false, message = "Ошибка: ${e.message}")
            }
        }
    }

    // ---------- Очистка ----------

    fun clearScans() {
        viewModelScope.launch {
            repo.clearScans()
            _state.value = _state.value.copy(message = "Журнал сканов очищен")
        }
    }
}
