package com.example.inventory.ui.screens

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.example.inventory.R
import com.example.inventory.ui.SettingsViewModel
import com.example.inventory.util.JsonBackup
import com.example.inventory.util.PdfLabelPrinter

/**
 * Глобальные «крюки» SAF-экранов: MainScreen регистрирует обработчики сохранения,
 * SettingsScreen запускает选择 файлов. Простой офлайн-паттерн без DI-библиотек.
 */
object ReportExportHolder {
    var onXlsx: ((Uri) -> Unit)? = null
    var onCsv: ((Uri) -> Unit)? = null
}

object BackupImportHolder {
    val pending = mutableStateOf<Uri?>(null)
}

/**
 * Экран «Настройки»: импорт Excel (с подтверждением замены базы), печать этикеток,
 * бэкапы JSON, пароль, очистка журнала. Все файловые операции — через SAF.
 */
@Composable
fun SettingsScreen(vm: SettingsViewModel = viewModel()) {
    val state by vm.state.collectAsState()
    var snackbar by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(state.message) {
        state.message?.let { snackbar = it; vm.consumeMessage() }
    }
    LaunchedEffect(snackbar) {
        if (snackbar != null) {
            kotlinx.coroutines.delay(4000)
            snackbar = null
        }
    }

    // --- SAF launchers ---

    // Импорт Excel: OPEN_DOCUMENT с MIME xlsx/xls
    var pendingExcelReplace by remember { mutableStateOf<Uri?>(null) }
    val excelPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri -> if (uri != null) pendingExcelReplace = uri }

    // Сохранение PDF / отчёта / бэкапа: CreateDocument
    val pdfSaver = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/pdf")
    ) { uri -> if (uri != null) vm.savePdfToUri(uri) }

    val xlsxSaver = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
    ) { uri -> if (uri != null) ReportExportHolder.onXlsx?.invoke(uri) }

    val csvSaver = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("text/csv")
    ) { uri -> if (uri != null) ReportExportHolder.onCsv?.invoke(uri) }

    val jsonSaver = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/json")
    ) { uri -> if (uri != null) vm.exportBackup(uri) }

    val jsonPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri -> if (uri != null) BackupImportHolder.pending.value = uri }

    // Журнал сканов для экспорта передаётся извне через ReportExportHolder
    var showPasswordDialog by remember { mutableStateOf<String?>(null) } // "set","change","disable"
    var confirmClearScans by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Text(stringResource(R.string.section_excel), style = MaterialTheme.typography.titleMedium)
        Button(
            onClick = { excelPicker.launch(arrayOf(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel",
                "text/spreadsheet"
            )) },
            modifier = Modifier.fillMaxWidth().height(48.dp)
        ) { Text(stringResource(R.string.import_excel)) }
        Text(stringResource(R.string.import_excel_hint), fontSize = 12.sp)

        state.importResult?.let { r ->
            Card(r.items.size, r.skipped, r.skipReasons)
        }

        Spacer(Modifier.height(6.dp))
        Text(stringResource(R.string.section_labels), style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { vm.prepareLabels(previewOnly = true) },
                modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.labels_preview))
            }
            Button(onClick = { vm.prepareLabels(previewOnly = false) },
                modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.labels_print))
            }
            OutlinedButton(onClick = { pdfSaver.launch(PdfLabelPrinter.defaultFileName()) },
                modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.labels_save_pdf))
            }
        }
        state.labelPreviewInfo?.let { Text(it, fontSize = 13.sp) }
        Text(stringResource(R.string.labels_lomond_hint), fontSize = 12.sp)

        Spacer(Modifier.height(6.dp))
        Text(stringResource(R.string.section_backup), style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { jsonSaver.launch(JsonBackup.exportFileName()) },
                modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.backup_export))
            }
            Button(onClick = { jsonPicker.launch(arrayOf("application/json", "*/*")) },
                modifier = Modifier.height(48.dp)) {
                Text(stringResource(R.string.backup_import))
            }
        }

        Spacer(Modifier.height(6.dp))
        Text(stringResource(R.string.section_password), style = MaterialTheme.typography.titleMedium)
        if (state.hasPassword) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { showPasswordDialog = "change" },
                    modifier = Modifier.height(48.dp)) {
                    Text(stringResource(R.string.password_change))
                }
                OutlinedButton(onClick = { showPasswordDialog = "disable" },
                    modifier = Modifier.height(48.dp)) {
                    Text(stringResource(R.string.password_disable))
                }
            }
        } else {
            Button(onClick = { showPasswordDialog = "set" },
                modifier = Modifier.fillMaxWidth().height(48.dp)) {
                Text(stringResource(R.string.password_set))
            }
        }

        Spacer(Modifier.height(6.dp))
        Text(stringResource(R.string.section_danger), style = MaterialTheme.typography.titleMedium)
        OutlinedButton(
            onClick = { confirmClearScans = true },
            modifier = Modifier.fillMaxWidth().height(48.dp)
        ) { Text(stringResource(R.string.clear_scans)) }

        Spacer(Modifier.height(80.dp))
    }

    // Диалог подтверждения замены базы при импорте Excel
    pendingExcelReplace?.let { uri ->
        AlertDialog(
            onDismissRequest = { pendingExcelReplace = null },
            title = { Text(stringResource(R.string.confirm_replace_title)) },
            text = { Text(stringResource(R.string.confirm_replace_text)) },
            confirmButton = {
                TextButton(onClick = {
                    pendingExcelReplace = null
                    vm.onExcelSelected(uri)
                }) { Text(stringResource(R.string.action_yes_replace)) }
            },
            dismissButton = {
                TextButton(onClick = { pendingExcelReplace = null }) {
                    Text(stringResource(R.string.action_cancel))
                }
            }
        )
    }

    // Выбор режима импорта бэкапа
    BackupImportHolder.pending.value?.let { uri ->
        AlertDialog(
            onDismissRequest = { BackupImportHolder.pending.value = null },
            title = { Text(stringResource(R.string.backup_import_mode_title)) },
            text = { Text(stringResource(R.string.backup_import_mode_text)) },
            confirmButton = {
                TextButton(onClick = {
                    BackupImportHolder.pending.value = null
                    vm.importBackup(uri, replaceAll = true)
                }) { Text(stringResource(R.string.backup_mode_replace)) }
            },
            dismissButton = {
                TextButton(onClick = {
                    BackupImportHolder.pending.value = null
                    vm.importBackup(uri, replaceAll = false)
                }) { Text(stringResource(R.string.backup_mode_merge)) }
            }
        )
    }

    // Подтверждение очистки журнала
    if (confirmClearScans) {
        AlertDialog(
            onDismissRequest = { confirmClearScans = false },
            title = { Text(stringResource(R.string.confirm_clear_title)) },
            text = { Text(stringResource(R.string.confirm_clear_text)) },
            confirmButton = {
                TextButton(onClick = {
                    confirmClearScans = false
                    vm.clearScans()
                }) { Text(stringResource(R.string.action_yes)) }
            },
            dismissButton = {
                TextButton(onClick = { confirmClearScans = false }) {
                    Text(stringResource(R.string.action_cancel))
                }
            }
        )
    }

    // Диалоги пароля
    when (showPasswordDialog) {
        "set" -> PasswordDialog(
            title = stringResource(R.string.password_set),
            askOld = false,
            onDismiss = { showPasswordDialog = null },
            onSubmit = { _, npw ->
                if (vm.setPassword(npw)) showPasswordDialog = null
            }
        )
        "change" -> PasswordDialog(
            title = stringResource(R.string.password_change),
            askOld = true,
            onDismiss = { showPasswordDialog = null },
            onSubmit = { opw, npw ->
                if (vm.changePassword(opw, npw)) showPasswordDialog = null
            }
        )
        "disable" -> DisablePasswordDialog(
            onDismiss = { showPasswordDialog = null },
            onConfirm = { pw -> if (vm.disablePassword(pw)) showPasswordDialog = null }
        )
    }

    snackbar?.let { msg ->
        AlertDialog(
            onDismissRequest = { snackbar = null },
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = { snackbar = null }) { Text("OK") } }
        )
    }
}

/** Простая карточка итога импорта. */
@Composable
private fun Card(loaded: Int, skipped: Int, reasons: List<String>) {
    androidx.compose.material3.Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text("Загружено $loaded, пропущено $skipped", style = MaterialTheme.typography.bodyLarge)
            reasons.take(15).forEach { Text("• $it", fontSize = 12.sp) }
            if (reasons.size > 15) Text("…и ещё ${reasons.size - 15}", fontSize = 12.sp)
        }
    }
}

@Composable
private fun PasswordDialog(
    title: String,
    askOld: Boolean,
    onDismiss: () -> Unit,
    onSubmit: (String, String) -> Unit
) {
    var oldPw by remember { mutableStateOf("") }
    var newPw by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (askOld) {
                    OutlinedTextField(
                        value = oldPw, onValueChange = { oldPw = it },
                        label = { Text(stringResource(R.string.password_current)) },
                        visualTransformation = PasswordVisualTransformation(),
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp)
                    )
                }
                OutlinedTextField(
                    value = newPw, onValueChange = { newPw = it },
                    label = { Text(stringResource(R.string.password_new)) },
                    visualTransformation = PasswordVisualTransformation(),
                    singleLine = true,
                    imeAction = ImeAction.Done,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                    textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp)
                )
                Text(stringResource(R.string.password_rule), fontSize = 12.sp)
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(oldPw, newPw) }) {
                Text(stringResource(R.string.action_save))
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.action_cancel)) } }
    )
}

@Composable
private fun DisablePasswordDialog(onDismiss: () -> Unit, onConfirm: (String) -> Unit) {
    var pw by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.password_disable_confirm_title)) },
        text = {
            Column {
                Text(stringResource(R.string.password_disable_confirm_text), fontSize = 14.sp)
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = pw, onValueChange = { pw = it },
                    label = { Text(stringResource(R.string.password_enter)) },
                    visualTransformation = PasswordVisualTransformation(),
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp)
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(pw) }) {
                Text(stringResource(R.string.action_disable))
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.action_cancel)) } }
    )
}
