package com.example.inventory.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.inventory.data.local.ItemStatus
import com.example.inventory.data.local.ItemWithCounts
import com.example.inventory.data.repository.InventoryRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn

enum class ListFilter(val label: String) {
    ALL("Все"),
    FOUND("Найдено"),
    PARTIAL("Частично"),
    MISSING("Пропало"),
    OVERAGE("Излишек")
}

data class ListUiState(
    val items: List<ItemWithCounts> = emptyList(),
    val visible: List<ItemWithCounts> = emptyList(),
    val query: String = "",
    val filter: ListFilter = ListFilter.ALL,
    val isDbEmpty: Boolean = false
)

class ItemListViewModel(app: Application) : AndroidViewModel(app) {

    private val repo = InventoryRepository(app)

    private val query = MutableStateFlow("")
    private val filter = MutableStateFlow(ListFilter.ALL)

    val state: StateFlow<ListUiState> =
        combine(repo.observeItemsWithCounts(), query, filter) { items, q, f ->
            val qq = q.trim().lowercase()
            val filtered = items.filter { ic ->
                val matchesQuery = qq.isEmpty() ||
                    ic.item.invNumber.lowercase().contains(qq) ||
                    ic.item.name.lowercase().contains(qq)
                val matchesFilter = when (f) {
                    ListFilter.ALL -> true
                    ListFilter.FOUND -> ic.status() == ItemStatus.FOUND ||
                            ic.status() == ItemStatus.FOUND_WITH_OVERAGE
                    ListFilter.PARTIAL -> ic.status() == ItemStatus.PARTIAL
                    ListFilter.MISSING -> ic.status() == ItemStatus.MISSING
                    ListFilter.OVERAGE -> ic.overageQty > 0
                }
                matchesQuery && matchesFilter
            }
            ListUiState(
                items = items,
                visible = filtered,
                query = q,
                filter = f,
                isDbEmpty = items.isEmpty()
            )
        }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), ListUiState())

    fun setQuery(s: String) { query.value = s }
    fun setFilter(f: ListFilter) { filter.value = f }
}
