package com.example.inventory.ui.screens

import android.Manifest
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.viewmodel.compose.viewModel
import com.example.inventory.R
import com.example.inventory.data.local.ScanEvent
import com.example.inventory.ui.ScanCard
import com.example.inventory.ui.ScannerViewModel
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Экран сканирования: CameraX preview + ML Kit QR (fallback — ручной ввод),
 * debounce 2.5 с, обратная связь, модалка излишка, счётчики, последние 20 сканов.
 */
@Composable
fun ScannerScreen(vm: ScannerViewModel = viewModel()) {
    val state by vm.state.collectAsStateSafe()
    val context = LocalContext.current

    var cameraPermission by remember { mutableStateOf(
        ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) ==
                android.content.pm.PackageManager.PERMISSION_GRANTED) }
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted -> cameraPermission = granted }

    DisposableEffect(Unit) {
        if (!cameraPermission) permissionLauncher.launch(Manifest.permission.CAMERA)
        onDispose { }
    }

    Column(modifier = Modifier.fillMaxSize().padding(8.dp)) {

        // Счётчики
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier.padding(horizontal = 8.dp)) {
            Text(stringResource(R.string.scanner_found_count, state.foundUnits, state.totalUnits),
                style = MaterialTheme.typography.titleMedium)
        }
        if (state.totalUnits > 0) {
            LinearProgressIndicator(
                progress = { (state.foundUnits.toFloat() / state.totalUnits).coerceIn(0f, 1f) },
                modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 6.dp)
            )
        }

        if (state.isDbEmpty) {
            EmptyStateBox(stringResource(R.string.empty_db_hint))
        } else {
            // Камера
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f)
                    .padding(8.dp)
            ) {
                if (cameraPermission) {
                    QrCameraPreview(
                        enabled = !state.busy && state.pendingOverage == null,
                        onQr = { code -> vm.onCode(code, fromCamera = true) }
                    )
                } else {
                    EmptyStateBox(stringResource(R.string.camera_permission_denied))
                }
            }

            // Карточка результата
            state.lastCard?.let { card ->
                ResultCard(card, modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 8.dp, vertical = 4.dp))
            }

            // Ручной ввод (Enter = обработать)
            var manual by remember { mutableStateOf("") }
            OutlinedTextField(
                value = manual,
                onValueChange = { manual = it },
                label = { Text(stringResource(R.string.manual_input_hint)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = {
                    if (manual.isNotBlank()) {
                        vm.onCode(manual, fromCamera = false)
                        manual = ""
                    }
                }),
                textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 8.dp, vertical = 4.dp)
            )

            // Последние 20 сканов
            Text(stringResource(R.string.recent_scans),
                style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.padding(start = 12.dp, top = 4.dp))
            LazyColumn(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(140.dp)
            ) {
                items(state.recentScans, key = { it.id }) { e ->
                    ScanLogRow(e)
                }
            }
        }
    }

    // Модалка подтверждения излишка
    state.pendingOverage?.let { p ->
        AlertDialog(
            onDismissRequest = { vm.dismissOverage() },
            title = { Text("⚠️") },
            text = {
                Text(stringResource(R.string.overage_dialog_text, p.expected, p.expected, p.name))
            },
            confirmButton = {
                TextButton(onClick = { vm.confirmAddOverage(sourceManual = false) }) {
                    Text(stringResource(R.string.action_add_overage))
                }
            },
            dismissButton = {
                TextButton(onClick = { vm.dismissOverage() }) {
                    Text(stringResource(R.string.action_cancel))
                }
            }
        )
    }
}

/** Пустой state с подсказкой. */
@Composable
fun EmptyStateBox(text: String) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(16.dp),
        contentAlignment = Alignment.Center
    ) {
        Card(colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
            Text(text, modifier = Modifier.padding(16.dp), fontSize = 15.sp)
        }
    }
}

/** Цветная карточка результата скана. */
@Composable
fun ResultCard(card: ScanCard, modifier: Modifier = Modifier) {
    val bg = when (card.kind) {
        ScanCard.Kind.GREEN -> Color(0xFF2E7D32)
        ScanCard.Kind.BLUE -> Color(0xFF1565C0)
        ScanCard.Kind.RED -> Color(0xFFC62828)
        ScanCard.Kind.YELLOW -> Color(0xFFF9A825)
    }
    Box(modifier = modifier.height(56.dp).background(bg, MaterialTheme.shapes.medium)) {
        Text(
            text = card.text,
            color = Color.White,
            fontSize = 15.sp,
            modifier = Modifier.align(Alignment.CenterStart).padding(horizontal = 12.dp)
        )
    }
}

@Composable
fun ScanLogRow(e: ScanEvent) {
    val time = SimpleDateFormat("HH:mm:ss", Locale.ROOT).format(Date(e.timestamp))
    val icon = if (e.type == "OVERAGE") "📦+" else "✅"
    val source = if (e.source == "CAMERA") "📷" else "⌨️"
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Text(icon)
        Text(time, fontSize = 13.sp)
        Text(e.invNumber, fontSize = 13.sp, modifier = Modifier.weight(1f))
        Text(source, fontSize = 13.sp)
    }
}

/**
 * CameraX Preview + ImageAnalysis с ML Kit Barcode Scanning (QR).
 * Fallback: если ML Kit недоступен — пользователь использует ручной ввод.
 */
@Composable
fun QrCameraPreview(enabled: Boolean, onQr: (String) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context) }
    val enabledState = remember(enabled) { mutableStateOf(enabled) }
    enabledState.value = enabled
    val callbackRef = remember { mutableStateOf(onQr) }
    callbackRef.value = onQr

    DisposableEffect(lifecycleOwner) {
        val providerFuture = ProcessCameraProvider.getInstance(context)
        val scanner = BarcodeScanning.getClient(
            BarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .build()
        )
        providerFuture.addListener({
            val provider = providerFuture.get()
            val preview = Preview.Builder().build().also {
                it.setSurfaceProvider(previewView.surfaceProvider)
            }
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { a ->
                    a.setAnalyzer(
                        ContextCompat.getMainExecutor(context)
                    ) { imageProxy ->
                        if (!enabledState.value) {
                            imageProxy.close()
                            return@setAnalyzer
                        }
                        val media = imageProxy.mediaImage
                        if (media != null) {
                            val input = InputImage.fromMediaImage(
                                media, imageProxy.imageInfo.rotationDegrees
                            )
                            scanner.process(input)
                                .addOnSuccessListener { barcodes ->
                                    barcodes.firstOrNull()?.rawBytes?.let { bytes ->
                                        val text = bytes.toString(Charsets.UTF_8)
                                        if (text.isNotBlank()) callbackRef.value(text)
                                    } ?: run {
                                        val s = barcodes.firstOrNull()?.rawValue
                                        if (!s.isNullOrBlank()) callbackRef.value(s)
                                    }
                                }
                                .addOnCompleteListener { imageProxy.close() }
                        } else {
                            imageProxy.close()
                        }
                    }
                }
            try {
                provider.unbindAll()
                provider.bindToLifecycle(
                    lifecycleOwner,
                    CameraSelector.DEFAULT_BACK_CAMERA,
                    preview, analysis
                )
            } catch (_: Exception) {
            }
        }, ContextCompat.getMainExecutor(context))

        onDispose {
            runCatching { scanner.close() }
            runCatching {
                ProcessCameraProvider.getInstance(context).get().unbindAll()
            }
        }
    }

    AndroidView(factory = { previewView }, modifier = Modifier.fillMaxSize())
}

/** collectAsState helper. */
@Composable
private fun <T> kotlinx.coroutines.flow.StateFlow<T>.collectAsStateSafe() =
    collectAsState()
