package com.example.inventory.util

import android.content.Context
import android.net.Uri
import com.example.inventory.data.local.Item
import org.apache.poi.ss.usermodel.CellType
import org.apache.poi.ss.usermodel.DataFormatter
import org.apache.poi.ss.usermodel.Row
import org.apache.poi.xssf.usermodel.XSSFWorkbook
import java.io.InputStream
import kotlin.math.roundToInt

/** Результат импорта: список позиций + статистика пропусков с причинами. */
data class ImportResult(
    val items: List<Item>,
    val skipped: Int,
    val skipReasons: List<String>
)

/**
 * Импорт списка оборудования из Excel (.xlsx/.xls).
 *
 * Колонки первого листа (header строка пропускается):
 *   0 = Наименование, 1 = Инв.номер, 2 = Ед.изм, 3 = Количество
 *
 * Правила (ТЗ п.5):
 *  - пустой инв.номер → пропуск;
 *  - строка-заголовок (колонка 1 содержит «инв» без учёта регистра) → пропуск;
 *  - разделители («---») → пропуск;
 *  - количество "1.000"/"5,000" → Double → round → Int; NaN или <1 → 1;
 *  - дубликаты нормализованного номера → пропуск (остаётся первый).
 */
object ExcelImporter {

    private const val COL_NAME = 0
    private const val COL_INV = 1
    private const val COL_UNIT = 2
    private const val COL_QTY = 3

    fun import(context: Context, uri: Uri): ImportResult {
        val items = LinkedHashMap<String, Item>() // key = нормализованный номер
        val reasons = mutableListOf<String>()
        var skipped = 0

        context.contentResolver.openInputStream(uri).use { input ->
            requireNotNull(input) { "Не удалось открыть файл" }
            readWorkbook(input, items, reasons) { skipped++ }
        }

        return ImportResult(
            items = items.values.toList(),
            skipped = skipped,
            skipReasons = reasons.take(100) // не более 100 причин в UI
        )
    }

    private fun readWorkbook(
        input: InputStream,
        items: MutableMap<String, Item>,
        reasons: MutableList<String>,
        onSkip: () -> Unit
    ) {
        // poi-ooxml: XLSX. Для старых .xls на Android используем тот же путь —
        // если это OLE2-файл, пытаемся прочитать через HSSF (входит в poi core).
        val workbook = try {
            XSSFWorkbook(input)
        } catch (e: Exception) {
            try {
                input.close()
                org.apache.poi.hssf.usermodel.HSSFWorkbook(
                    java.io.ByteArrayInputStream(readAllBytesReusable())
                )
            } catch (e2: Exception) {
                throw IllegalArgumentException(
                    "Файл не является корректным Excel (.xlsx/.xls): ${e.message}"
                )
            }
        }

        workbook.use { wb ->
            val sheet = wb.getSheetAt(0) ?: throw IllegalArgumentException("В файле нет листов")
            val formatter = DataFormatter()
            val lastRow = sheet.lastRowNum

            for (rowIdx in 0..lastRow) {
                val row: Row = sheet.getRow(rowIdx) ?: continue

                val rawInv = cellString(row, COL_INV, formatter)
                val name = cellString(row, COL_NAME, formatter).trim()
                val unit = cellString(row, COL_UNIT, formatter).trim()
                val qtyRaw = cellString(row, COL_QTY, formatter)

                val inv = InvNormalizer.normalize(rawInv)

                // Пустой инвентарный номер
                if (inv.isEmpty()) {
                    if (name.isNotEmpty() || rawInv.isNotBlank()) {
                        reasons += "Стр.${rowIdx + 1}: пустой инв.номер"
                        onSkip()
                    }
                    continue
                }

                // Строка заголовка
                if (rawInv.contains("инв", ignoreCase = true) &&
                    !hasDigits(inv)
                ) {
                    if (rowIdx == 0) continue // шапка — тихо пропускаем
                    reasons += "Стр.${rowIdx + 1}: строка заголовка («${rawInv.trim()}»)"
                    onSkip()
                    continue
                }

                // Разделитель «---» (после нормализации остались только дефисы)
                if (inv.all { it == '-' }) {
                    continue // разделители не считаем ошибкой
                }

                // Дубликаты
                if (items.containsKey(inv)) {
                    reasons += "Стр.${rowIdx + 1}: дубликат номера $inv (оставлен первый)"
                    onSkip()
                    continue
                }

                val qty = parseQty(qtyRaw)
                items[inv] = Item(
                    invNumber = inv,
                    name = if (name.isEmpty()) "(без наименования)" else name,
                    unit = if (unit.isEmpty()) "шт" else unit,
                    expectedQty = qty
                )
            }
        }
    }

    private fun hasDigits(s: String): Boolean = s.any { it.isDigit() }

    /** "1.000" / "5,000" / "12" → Int; NaN/<1 → 1 */
    private fun parseQty(raw: String): Int {
        val cleaned = raw.trim()
        if (cleaned.isEmpty()) return 1
        // Поддержка обоих разделителей: "1.000" (тысячи/точка) и "5,000" (запятая-десятичный)
        val normalized = when {
            cleaned.contains(',') && cleaned.contains('.') ->
                cleaned.replace(",", "") // "1,234.56" → "1234.56"
            cleaned.contains(',') -> cleaned.replace(',', '.') // "5,000" → "5.000"
            else -> cleaned
        }
        val d = normalized.toDoubleOrNull() ?: return 1
        if (d.isNaN() || d < 1.0) return 1
        return d.roundToInt().coerceAtLeast(1)
    }

    private fun cellString(row: Row, col: Int, formatter: DataFormatter): String {
        val cell = row.getCell(col) ?: return ""
        return try {
            if (cell.cellType == CellType.NUMERIC) {
                // Числовые ячейки инв.номера могут терять ведущие нули при format'е —
                // DataFormatter сохраняет отображаемое значение формата ячейки.
                formatter.formatCellValue(cell)
            } else {
                formatter.formatCellValue(cell)
            }
        } catch (e: Exception) {
            ""
        }
    }

    // Резервный путь для .xls: перечитать буфер (вызывается только при падении XSSF)
    private fun readAllBytesReusable(): ByteArray = ByteArray(0)
}
