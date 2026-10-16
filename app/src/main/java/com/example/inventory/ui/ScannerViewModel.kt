package com.example.inventory.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.inventory.data.local.ScanEvent
import com.example.inventory.data.repository.InventoryRepository
import com.example.inventory.data.repository.ScanOutcome
import com.example.inventory.util.InvNormalizer
import com.example.inventory.util.SoundFeedback
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** Карточка результата последнего скана для UI. */
data class ScanCard(
    val kind: Kind,
    val text: String
) {
    enum class Kind { GREEN, BLUE, RED, YELLOW }
}

data class ScannerUiState(
    val isDbEmpty: Boolean = false,
    val totalUnits: Int = 0,
    val foundUnits: Int = 0,
    val recentScans: List<ScanEvent> = emptyList(),
    val lastCard: ScanCard? = null,
    val pendingOverage: PendingOverage? = null,
    val busy: Boolean = false
)

data class PendingOverage(val invNumber: String, val name: String, val expected: Int)

class ScannerViewModel(app: Application) : AndroidViewModel(app) {

    private val repo = InventoryRepository(app)

    // Debounce: один и тот же код игнорируется 2.5 с после обработки
    private val lastHandled = mutableMapOf<String, Long>()
    private val debounceMs = 2_500L

    private val _state = MutableStateFlow(ScannerUiState())
    val state: StateFlow<ScannerUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            combine3(
                repo.observeItemCount(),
                repo.observeItemsWithCounts(),
                repo.observeRecentScans(20)
            ).collect { (count, items, scans) ->
                val total = items.sumOf { it.expectedQty }
                val found = items.sumOf { it.scannedQty.coerceAtMost(it.expectedQty) }
                _state.value = _state.value.copy(
                    isDbEmpty = count == 0,
                    totalUnits = total,
                    foundUnits = found,
                    recentScans = scans
                )
            }
        }
    }

    fun onCode(raw: String, fromCamera: Boolean) {
        val x = InvNormalizer.normalize(raw)
        if (x.isEmpty()) return
        val now = System.currentTimeMillis()
        val prev = lastHandled[x] ?: 0L
        if (now - prev < debounceMs) return // debounce одного и того же кода
        lastHandled[x] = now

        val source = if (fromCamera) "CAMERA" else "MANUAL"
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            try {
                when (val outcome = repo.processCode(x, source)) {
                    is ScanOutcome.Found -> {
                        val card = if (outcome.fullyCounted) {
                            ScanCard(
                                ScanCard.Kind.GREEN,
                                "✅ Найдено: ${outcome.item.name} — ${outcome.newScanned} из ${outcome.item.expectedQty}"
                            )
                        } else {
                            ScanCard(
                                ScanCard.Kind.GREEN,
                                "✅ Найдено: ${outcome.item.name} — ${outcome.newScanned} из ${outcome.item.expectedQty}"
                            )
                        }
                        emitCard(card, if (outcome.fullyCounted)
                            SoundFeedback.TONE_FULL else SoundFeedback.TONE_PARTIAL)
                    }
                    is ScanOutcome.NotFound -> {
                        lastHandled.remove(x) // разрешаем повторную попытку исправить ошибку
                        emitCard(ScanCard(ScanCard.Kind.RED, "❌ Не найдено в базе: $x"),
                            SoundFeedback.TONE_ERROR)
                    }
                    is ScanOutcome.NeedOverageConfirm -> {
                        _state.value = _state.value.copy(
                            pendingOverage = PendingOverage(
                                invNumber = outcome.item.invNumber,
                                name = outcome.item.name,
                                expected = outcome.expected
                            ),
                            busy = false
                        )
                    }
                    is ScanOutcome.OverageAdded -> {
                        emitCard(ScanCard(ScanCard.Kind.BLUE,
                            "📦+ Излишек добавлен (всего: ${outcome.totalOverage})"),
                            SoundFeedback.TONE_OVERAGE)
                    }
                    ScanOutcome.EmptyCode -> {
                        emitCard(ScanCard(ScanCard.Kind.YELLOW, "⚠️ Пустой код"),
                            SoundFeedback.TONE_ERROR)
                    }
                }
            } catch (e: Exception) {
                emitCard(ScanCard(ScanCard.Kind.RED, "⚠️ Ошибка: ${e.message}"),
                    SoundFeedback.TONE_ERROR)
            } finally {
                _state.value = _state.value.copy(busy = false)
            }
        }
    }

    fun confirmAddOverage(sourceManual: Boolean) {
        val pending = _state.value.pendingOverage ?: return
        val source = if (sourceManual) "MANUAL" else "CAMERA"
        viewModelScope.launch {
            _state.value = _state.value.copy(pendingOverage = null, busy = true)
            try {
                when (val o = repo.confirmOverage(pending.invNumber, source)) {
                    is ScanOutcome.OverageAdded -> emitCard(
                        ScanCard(ScanCard.Kind.BLUE,
                            "📦+ Излишек добавлен (всего: ${o.totalOverage})"),
                        SoundFeedback.TONE_OVERAGE)
                    else -> emitCard(ScanCard(ScanCard.Kind.RED, "❌ Ошибка подтверждения излишка"),
                        SoundFeedback.TONE_ERROR)
                }
            } finally {
                _state.value = _state.value.copy(busy = false)
            }
        }
    }

    fun dismissOverage() {
        _state.value = _state.value.copy(pendingOverage = null)
    }

    fun clearCard() {
        _state.value = _state.value.copy(lastCard = null)
    }

    private suspend fun emitCard(card: ScanCard, tone: Int) {
        _state.value = _state.value.copy(lastCard = card)
        SoundFeedback.feedback(getApplication(), tone)
        delay(100)
    }
}

/** Утилита объединения трёх Flow (без внешних библиотек). */
private fun <A, B, C> combine3(
    f1: kotlinx.coroutines.flow.Flow<A>,
    f2: kotlinx.coroutines.flow.Flow<B>,
    f3: kotlinx.coroutines.flow.Flow<C>
): kotlinx.coroutines.flow.Flow<Triple<A, B, C>> {
    return kotlinx.coroutines.flow.combine(f1, f2, f3) { a, b, c -> Triple(a, b, c) }
}
