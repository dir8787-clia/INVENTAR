package com.example.inventory.util

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.net.Uri
import android.print.PrintAttributes
import android.print.PrintManager
import com.example.inventory.data.local.Item
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.ceil

/**
 * Печать QR-этикеток на самоклеящуюся бумагу Lomond 2100195.
 *
 * ТОЧНЫЕ РАЗМЕРЫ (ТЗ п.7):
 *  - этикетка: 48.5 × 25.4 мм
 *  - лист A4: 4 колонки × 10 строк = 40 этикеток
 *  - поля листа: слева/справа 8 мм, сверху/снизу 21.5 мм
 *    (8 + 4×48.5 + 8 = 210 мм — точно ширина A4; сетка привязана к левому верхнему углу)
 *  - QR слева 15×15 мм (содержит нормализованный invNumber);
 *    справа текст: invNumber (bold 8pt, моноширинный, перенос по символам),
 *    name (5.5pt, максимум 4 строки, обрезка с многоточием)
 *  - Количество этикеток позиции = expectedQty; >40 → многостраничность
 *
 * Все координаты в pt: 1 мм = 72/25.4 pt. Генерацию вызывать из Dispatchers.IO.
 */
object PdfLabelPrinter {

    private const val MM_TO_PT = 72.0 / 25.4

    // Геометрия, мм
    private const val LABEL_W_MM = 48.5
    private const val LABEL_H_MM = 25.4
    private const val COLS = 4
    private const val ROWS = 10
    const val PER_PAGE = COLS * ROWS
    private const val MARGIN_LR_MM = 8.0
    private const val MARGIN_TB_MM = 21.5
    private const val PAGE_W_MM = 210.0
    private const val PAGE_H_MM = 296.5 // 21.5 + 10*25.4 + 21.5 = 296 мм (сетка Lomond)
    private const val QR_SIZE_MM = 15.0
    private const val QR_LEFT_PAD_MM = 3.0
    private const val TEXT_LEFT_MM = 20.0
    private const val TEXT_RIGHT_PAD_MM = 2.0

    private fun mm(v: Double): Float = (v * MM_TO_PT).toFloat()

    /** Этикетка: позиция + номер копии (0..copies-1). */
    data class Label(val item: Item, val copyIndex: Int)

    /** Разворачивает позиции в плоский список этикеток (expectedQty копий каждой). */
    fun buildLabels(items: List<Item>): List<Label> {
        val out = ArrayList<Label>(items.sumOf { it.expectedQty.coerceAtLeast(1) })
        items.forEach { item ->
            repeat(item.expectedQty.coerceAtLeast(1)) { i -> out.add(Label(item, i)) }
        }
        return out
    }

    fun totalPages(labels: List<Label>): Int =
        ceil(labels.size / PER_PAGE.toDouble()).toInt().coerceAtLeast(1)

    /** Создаёт PdfDocument со всеми этикетками (тяжёлая операция — только IO поток). */
    fun createDocument(labels: List<Label>): PdfDocument {
        val pages = totalPages(labels)
        val info = PdfDocument.Info()
        info.pageCount = pages
        val doc = PdfDocument(info)

        val qrPaint = Paint(Paint.FILTER_BITMAP_FLAG)

        val numPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.BLACK
            typeface = Typeface.create(Typeface.MONOSPACE, Typeface.BOLD)
            textSize = mm(8.0) // 8pt
        }

        val namePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.BLACK
            typeface = Typeface.SANS_SERIF
            textSize = mm(5.5) // 5.5pt
        }

        val pageW = mm(PAGE_W_MM)
        val pageH = mm(PAGE_H_MM)
        val labelW = mm(LABEL_W_MM)
        val labelH = mm(LABEL_H_MM)
        val marginLR = mm(MARGIN_LR_MM)
        val marginTB = mm(MARGIN_TB_MM)
        val qrSize = mm(QR_SIZE_MM)

        var idx = 0
        for (pageNo in 0 until pages) {
            val page = doc.startPage(
                PdfDocument.PageInfo.Builder(pageW.toInt(), pageH.toInt(), pageNo + 1).create()
            )
            val c = page.canvas

            outer@ for (row in 0 until ROWS) {
                for (col in 0 until COLS) {
                    if (idx >= labels.size) break@outer
                    val x0 = marginLR + col * labelW
                    val y0 = marginTB + row * labelH
                    drawLabel(c, labels[idx], x0, y0, labelW, labelH, qrSize,
                        numPaint, namePaint, qrPaint)
                    idx++
                }
            }
            doc.finishPage(page)
        }
        return doc
    }

    private fun drawLabel(
        c: Canvas,
        label: Label,
        x0: Float, y0: Float, labelW: Float, labelH: Float, qrSize: Float,
        numPaint: Paint, namePaint: Paint, qrPaint: Paint
    ) {
        val item = label.item

        // QR СЛЕВА: 15×15 мм, вертикально центрирован в этикетке
        val qrX = x0 + mm(QR_LEFT_PAD_MM)
        val qrY = y0 + (labelH - qrSize) / 2f
        val qrBmp: Bitmap = QrGenerator.generate(item.invNumber, 300)
        c.drawBitmap(qrBmp, null, RectF(qrX, qrY, qrX + qrSize, qrY + qrSize), qrPaint)

        // ТЕКСТ СПРАВА
        val textX = x0 + mm(TEXT_LEFT_MM)
        val textMaxW = labelW - mm(TEXT_LEFT_MM) - mm(TEXT_RIGHT_PAD_MM)

        // Инв. номер: bold 8pt моноширинный, перенос ПО СИМВОЛАМ
        val numLines = wrapByChar(numPaint, item.invNumber, textMaxW, maxLines = 2)
        val lineHeightNum = mm(8.0) * 1.15f
        var ty = y0 + mm(5.0) + mm(8.0) // baseline первой строки

        for (line in numLines) {
            c.drawText(line, textX, ty, numPaint)
            ty += lineHeightNum
        }

        // Наименование: 5.5pt, до 4 строк, обрезка с «…»
        ty += mm(1.2)
        val lineHeightName = mm(5.5) * 1.15f
        val nameLines = wrapWords(namePaint, item.name, textMaxW, maxLines = 4)
        for (line in nameLines) {
            if (ty > y0 + labelH - mm(1.0)) break
            c.drawText(line, textX, ty, namePaint)
            ty += lineHeightName
        }
    }

    /** Перенос по символам (инвентарные номера без пробелов). */
    private fun wrapByChar(paint: Paint, text: String, maxWidth: Float, maxLines: Int): List<String> {
        val lines = mutableListOf<String>()
        var cur = StringBuilder()
        for (ch in text) {
            val trial = cur.toString() + ch
            if (paint.measureText(trial) > maxWidth && cur.isNotEmpty()) {
                lines += cur.toString()
                cur = StringBuilder(ch.toString())
                if (lines.size >= maxLines) break
            } else {
                cur.append(ch)
            }
        }
        if (lines.size < maxLines && cur.isNotEmpty()) lines += cur.toString()
        return lines.ifEmpty { listOf(text.take(1)) }
    }

    /** Перенос по словам, максимум maxLines строк, обрезка с многоточием. */
    private fun wrapWords(paint: Paint, text: String, maxWidth: Float, maxLines: Int): List<String> {
        val normalized = text.replace(Regex("\\s+"), " ").trim()
        if (normalized.isEmpty()) return emptyList()
        val words = normalized.split(" ")
        val lines = mutableListOf<String>()
        var cur = StringBuilder()
        var overflow = false
        for (w in words) {
            val trial = if (cur.isEmpty()) w else "$cur $w"
            if (paint.measureText(trial) <= maxWidth) {
                cur = StringBuilder(trial)
            } else {
                if (cur.isNotEmpty()) {
                    lines += cur.toString()
                    cur = StringBuilder(w)
                } else {
                    // слово длиннее ширины строки — режем по символам
                    var piece = StringBuilder()
                    for (ch in w) {
                        if (paint.measureText(piece.toString() + ch) > maxWidth) {
                            lines += piece.toString()
                            piece = StringBuilder(ch.toString())
                        } else piece.append(ch)
                    }
                    cur = piece
                }
                if (lines.size >= maxLines) {
                    overflow = true
                    cur = StringBuilder()
                    break
                }
            }
        }
        if (!overflow && cur.isNotEmpty() && lines.size < maxLines) lines += cur.toString()
        else if (cur.isNotEmpty()) overflow = true

        val result = lines.toMutableList()
        if (overflow && result.isNotEmpty()) {
            var last = result[result.size - 1]
            while (last.isNotEmpty() && paint.measureText("$last…") > maxWidth) {
                last = last.dropLast(1)
            }
            result[result.size - 1] = "$last…"
        }
        return result
    }

    /** Сохранение PDF в файл через SAF uri. Вызывать из IO. */
    fun saveToUri(context: Context, labels: List<Label>, uri: Uri) {
        val doc = createDocument(labels)
        try {
            context.contentResolver.openOutputStream(uri)?.use { out ->
                doc.writeTo(out)
            } ?: throw IllegalStateException("Не удалось открыть поток записи")
        } finally {
            doc.close()
        }
    }

    /** Сохранение во внутренний кэш (для предпросмотра через PdfRenderer). Возвращает File. */
    fun saveToCache(context: Context, labels: List<Label>, fileName: String): File {
        val f = File(context.cacheDir, fileName)
        FileOutputStream(f).use { out ->
            val doc = createDocument(labels)
            doc.writeTo(out)
            doc.close()
        }
        return f
    }

    /** Печать через системный PrintManager (Android Print Framework). */
    fun print(context: Context, labels: List<Label>, jobName: String) {
        val doc = createDocument(labels)
        val adapter = PdfDocPrintAdapter(doc, jobName)
        val pm = context.getSystemService(Context.PRINT_SERVICE) as PrintManager
        val attrs = PrintAttributes.Builder()
            .setMediaSize(PrintAttributes.MediaSize.ISO_A4)
            .setResolution(PrintAttributes.Resolution("pdf", "pdf", 600, 600))
            .setMinMargins(PrintAttributes.Margins.NO_MARGINS)
            .build()
        pm.print(jobName, adapter, attrs)
    }

    fun defaultFileName(): String {
        val d = SimpleDateFormat("yyyy-MM-dd", Locale.ROOT).format(Date())
        return "Этикетки_$d.pdf"
    }
}
