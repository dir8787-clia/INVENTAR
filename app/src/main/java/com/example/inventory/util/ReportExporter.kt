package com.example.inventory.util

import android.content.Context
import android.net.Uri
import com.example.inventory.data.local.ItemStatus
import com.example.inventory.data.local.ItemWithCounts
import org.apache.poi.ss.usermodel.BorderStyle
import org.apache.poi.ss.usermodel.FillPatternType
import org.apache.poi.ss.usermodel.IndexedColors
import org.apache.poi.xssf.usermodel.XSSFWorkbook
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Экспорт отчёта инвентаризации.
 * Колонки: [Инв.номер, Наименование, Ед.изм, Должно быть, Найдено, Излишек, Статус]
 * Форматы: .xlsx (Apache POI) и .csv с UTF-8 BOM (фолбэк).
 * Имя файла: «Инвентаризация_Отчет_ГГГГ-ММ-ДД».
 */
object ReportExporter {

    data class Row(
        val invNumber: String,
        val name: String,
        val unit: String,
        val expected: Int,
        val found: Int,
        val overage: Int,
        val status: String
    )

    fun reportFileName(): String {
        val d = SimpleDateFormat("yyyy-MM-dd", Locale.ROOT).format(Date())
        return "Инвентаризация_Отчет_$d"
    }

    fun statusText(status: ItemStatus): String = when (status) {
        ItemStatus.FOUND -> "Найдено"
        ItemStatus.PARTIAL -> "Частично"
        ItemStatus.MISSING -> "Пропало"
        ItemStatus.OVERAGE_ONLY -> "Излишек"
        ItemStatus.FOUND_WITH_OVERAGE -> "Найдено + излишек"
    }

    fun buildRows(items: List<ItemWithCounts>): List<Row> =
        items.map {
            Row(
                invNumber = it.item.invNumber,
                name = it.item.name,
                unit = it.item.unit,
                expected = it.expectedQty,
                found = it.scannedQty,
                overage = it.overageQty,
                status = statusText(it.status())
            )
        }

    /** Экспорт в XLSX по SAF uri. Вызывать из Dispatchers.IO. */
    fun exportXlsx(context: Context, uri: Uri, rows: List<Row>) {
        XSSFWorkbook().use { wb ->
            val sheet = wb.createSheet("Отчёт")
            val headerFont = wb.createFont().apply { bold = true }
            val headerStyle = wb.createCellStyle().apply {
                setFont(headerFont)
                fillForegroundColor = IndexedColors.GREY_25_PERCENT.index
                fillPattern = FillPatternType.SOLID_FOREGROUND
                borderBottom = BorderStyle.THIN
            }

            val headers = listOf(
                "Инв.номер", "Наименование", "Ед.изм",
                "Должно быть", "Найдено", "Излишек", "Статус"
            )
            val headerRow = sheet.createRow(0)
            headers.forEachIndexed { i, h ->
                val cell = headerRow.createCell(i)
                cell.setCellValue(h)
                cell.cellStyle = headerStyle
            }

            rows.forEachIndexed { ri, r ->
                val row = sheet.createRow(ri + 1)
                row.createCell(0).setCellValue(r.invNumber)
                row.createCell(1).setCellValue(r.name)
                row.createCell(2).setCellValue(r.unit)
                row.createCell(3).setCellValue(r.expected.toDouble())
                row.createCell(4).setCellValue(r.found.toDouble())
                row.createCell(5).setCellValue(r.overage.toDouble())
                row.createCell(6).setCellValue(r.status)
            }

            for (i in headers.indices) sheet.setColumnWidth(i, 4500)
            sheet.setColumnWidth(1, 9000) // наименование шире

            context.contentResolver.openOutputStream(uri)?.use { out ->
                wb.write(out)
            } ?: throw IllegalStateException("Не удалось открыть поток записи")
        }
    }

    /** Экспорт в CSV с UTF-8 BOM (фолбэк, не требует POI). */
    fun exportCsv(context: Context, uri: Uri, rows: List<Row>) {
        context.contentResolver.openOutputStream(uri)?.use { out ->
            // UTF-8 BOM — чтобы Excel корректно открыл кириллицу
            out.write(byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()))
            val sb = StringBuilder()
            sb.append("Инв.номер;Наименование;Ед.изм;Должно быть;Найдено;Излишек;Статус\n")
            rows.forEach { r ->
                sb.append(csvEscape(r.invNumber)).append(';')
                    .append(csvEscape(r.name)).append(';')
                    .append(csvEscape(r.unit)).append(';')
                    .append(r.expected).append(';')
                    .append(r.found).append(';')
                    .append(r.overage).append(';')
                    .append(csvEscape(r.status)).append('\n')
            }
            out.write(sb.toString().toByteArray(Charsets.UTF_8))
        } ?: throw IllegalStateException("Не удалось открыть поток записи")
    }

    private fun csvEscape(s: String): String {
        return if (s.any { it == ';' || it == '"' || it == '\n' }) {
            "\"" + s.replace("\"", "\"\"") + "\""
        } else s
    }
}
