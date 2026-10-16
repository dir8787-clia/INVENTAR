package com.example.inventory.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Badge
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.example.inventory.R
import com.example.inventory.data.local.ItemStatus
import com.example.inventory.data.local.ItemWithCounts
import com.example.inventory.ui.ItemListViewModel
import com.example.inventory.ui.ListFilter

/** Экран «Список»: поиск, фильтр статуса, ленивый список позиций. */
@Composable
fun ItemListScreen(vm: ItemListViewModel = viewModel()) {
    val state by vm.state.collectAsState()

    Column(modifier = Modifier.fillMaxSize().padding(8.dp)) {
        OutlinedTextField(
            value = state.query,
            onValueChange = { vm.setQuery(it) },
            label = { Text(stringResource(R.string.search_hint)) },
            singleLine = true,
            textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp),
            modifier = Modifier.fillMaxWidth()
        )

        Row(
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)
        ) {
            ListFilter.entries.forEach { f ->
                AssistChip(
                    onClick = { vm.setFilter(f) },
                    label = { Text(f.label, fontSize = 13.sp) },
                    selected = state.filter == f
                )
            }
        }

        if (state.isDbEmpty) {
            EmptyStateBox(stringResource(R.string.empty_db_hint))
        } else if (state.visible.isEmpty()) {
            EmptyStateBox(stringResource(R.string.empty_filter_hint))
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(6.dp),
                modifier = Modifier.fillMaxSize()
            ) {
                items(state.visible, key = { it.item.id }) { ic ->
                    ItemRow(ic)
                }
            }
        }
    }
}

@Composable
fun ItemRow(ic: ItemWithCounts) {
    val statusColor = when (ic.status()) {
        ItemStatus.FOUND -> MaterialTheme.colorScheme.primary
        ItemStatus.PARTIAL -> MaterialTheme.colorScheme.tertiary
        ItemStatus.MISSING -> MaterialTheme.colorScheme.error
        ItemStatus.OVERAGE_ONLY, ItemStatus.FOUND_WITH_OVERAGE -> MaterialTheme.colorScheme.secondary
    }
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(12.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(ic.item.invNumber, fontSize = 15.sp,
                    fontFamily = FontFamily.Monospace)
                Text(ic.item.name, fontSize = 14.sp, maxLines = 2)
                Text(
                    text = stringResource(
                        R.string.item_status_line,
                        ic.scannedQty, ic.expectedQty, ic.item.unit,
                        statusLabel(ic.status())
                    ),
                    fontSize = 12.sp, color = statusColor
                )
            }
            if (ic.overageQty > 0) {
                Badge(containerColor = MaterialTheme.colorScheme.secondary) {
                    Text("+${ic.overageQty}", color = MaterialTheme.colorScheme.onSecondary)
                }
            }
        }
    }
}

@Composable
fun statusLabel(s: ItemStatus): String = stringResource(
    when (s) {
        ItemStatus.FOUND -> R.string.status_found
        ItemStatus.PARTIAL -> R.string.status_partial
        ItemStatus.MISSING -> R.string.status_missing
        ItemStatus.OVERAGE_ONLY -> R.string.status_overage
        ItemStatus.FOUND_WITH_OVERAGE -> R.string.status_found_overage
    }
)
