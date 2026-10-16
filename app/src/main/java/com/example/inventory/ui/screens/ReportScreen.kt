package com.example.inventory.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.example.inventory.R
import com.example.inventory.data.local.ScanEvent
import com.example.inventory.ui.ReportViewModel
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Экран «Отчёт»: дашборд (карточки + прогресс), журнал сканирований, экспорт.
 * Экспорт запускает SAF CreateDocument («Инвентаризация_Отчет_ГГГГ-ММ-ДД.xlsx»).
 */
@Composable
fun ReportScreen(
    vm: ReportViewModel = viewModel(),
    onExportXlsx: () -> Unit,
    onExportCsv: () -> Unit
) {
    val state by vm.state.collectAsState()

    Column(modifier = Modifier.fillMaxSize().padding(8.dp)) {
        if (state.isDbEmpty) {
            EmptyStateBox(stringResource(R.string.empty_db_hint))
            return@Column
        }

        // Дашборд
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.fillMaxWidth()) {
            StatCard(stringResource(R.string.stat_total_units), "${state.totalUnits}", Modifier.weight(1f))
            StatCard(stringResource(R.string.stat_found), "${state.foundUnits}", Modifier.weight(1f))
            StatCard(stringResource(R.string.stat_missing), "${state.missingUnits}", Modifier.weight(1f))
        }
        Spacer(Modifier.height(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.fillMaxWidth()) {
            StatCard(stringResource(R.string.stat_partial), "${state.partialPositions}", Modifier.weight(1f))
            StatCard(stringResource(R.string.stat_overage), "${state.overageUnits}", Modifier.weight(1f))
        }

        if (state.totalUnits > 0) {
            LinearProgressIndicator(
                progress = { (state.foundUnits.toFloat() / state.totalUnits).coerceIn(0f, 1f) },
                modifier = Modifier.fillMaxWidth().padding(vertical = 10.dp)
            )
            Text(stringResource(R.string.progress_caption, state.foundUnits, state.totalUnits),
                fontSize = 12.sp, modifier = Modifier.padding(bottom = 6.dp))
        }

        // Экспорт
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onExportXlsx, modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.export_xlsx))
            }
            OutlinedButton(onClick = onExportCsv, modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.export_csv))
            }
        }

        Spacer(Modifier.height(8.dp))
        Text(stringResource(R.string.scan_log_title), style = MaterialTheme.typography.titleMedium,
            modifier = Modifier.padding(start = 8.dp))

        LazyColumn(
            verticalArrangement = Arrangement.spacedBy(2.dp),
            modifier = Modifier.fillMaxSize()
        ) {
            items(state.scans, key = { it.id }) { e ->
                ScanLogRowDetailed(e)
            }
            if (state.scans.isEmpty()) {
                item { EmptyStateBox(stringResource(R.string.no_scans_yet)) }
            }
        }
    }
}

@Composable
private fun StatCard(label: String, value: String, modifier: Modifier = Modifier) {
    Card(modifier = modifier) {
        Column(modifier = Modifier.padding(10.dp)) {
            Text(value, fontSize = 20.sp, style = MaterialTheme.typography.titleLarge)
            Text(label, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun ScanLogRowDetailed(e: ScanEvent) {
    val time = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.ROOT).format(Date(e.timestamp))
    val typeRu = if (e.type == "OVERAGE")
        stringResource(R.string.log_type_overage) else stringResource(R.string.log_type_normal)
    val srcRu = if (e.source == "CAMERA")
        stringResource(R.string.log_source_camera) else stringResource(R.string.log_source_manual)
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Text(time, fontSize = 12.sp)
        Text(e.invNumber, fontSize = 12.sp, modifier = Modifier.weight(1f))
        Text(typeRu, fontSize = 12.sp,
            color = if (e.type == "OVERAGE") MaterialTheme.colorScheme.secondary
            else MaterialTheme.colorScheme.primary)
        Text(srcRu, fontSize = 12.sp)
    }
}
