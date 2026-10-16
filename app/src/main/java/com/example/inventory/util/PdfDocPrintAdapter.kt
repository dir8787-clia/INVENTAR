package com.example.inventory.util

import android.content.Context
import android.graphics.pdf.PdfDocument
import android.os.Bundle
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import java.io.FileInputStream
import java.io.FileOutputStream

/**
 * Адаптер печати для уже готового in-memory PdfDocument.
 * (Аналог скрытого android.print.pdf.PrintablePdfDocumentAdapter — свой, чтобы
 *  не зависеть от internal API; работает на всех SDK >= 24.)
 */
class PdfDocPrintAdapter(
    private val document: PdfDocument,
    private val jobName: String
) : PrintDocumentAdapter() {

    private var savedFile: java.io.File? = null

    override fun onLayout(
        oldAttributes: PrintAttributes?,
        newAttributes: PrintAttributes,
        cancellationSignal: CancellationSignal,
        callback: LayoutResultCallback,
        extras: Bundle?
    ) {
        if (cancellationSignal.isCanceled) {
            callback.onLayoutCancelled()
            return
        }
        val info = PrintDocumentInfo.Builder(jobName)
            .setPageCount(document.pageCount.coerceAtLeast(1))
            .setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT)
            .build()
        callback.onLayoutFinished(info, true)
    }

    override fun onWrite(
        pages: Array<out PageRange>?,
        destination: ParcelFileDescriptor,
        cancellationSignal: CancellationSignal,
        callback: WriteResultCallback
    ) {
        try {
            // Пишем документ во временный файл, затем копируем в destination
            val tmp = java.io.File.createTempFile("labels_", ".pdf",
                contextCacheDir())
            FileOutputStream(tmp).use { fos -> document.writeTo(fos) }
            savedFile = tmp
            FileInputStream(tmp).use { ins ->
                FileOutputStream(destination.fileDescriptor).use { out ->
                    ins.copyTo(out)
                }
            }
            val written = if (pages == null || pages.isEmpty()) {
                arrayOf(PageRange.ALL_PAGES)
            } else pages
            callback.onWriteFinished(written)
        } catch (e: Exception) {
            callback.onWriteFailed(e.message)
        }
    }

    private fun contextCacheDir(): java.io.File {
        // Print framework вызывает на UI-потоке; temp-файл кладём в cacheDir процесса
        return FallbackAppContext.get().cacheDir
    }
}

/** Мини-хост глобального контекста (приложение полностью офлайн, DI не нужен). */
object FallbackAppContext {
    @Volatile
    private var ctx: Context? = null

    fun init(c: Context) {
        ctx = c.applicationContext
    }

    fun get(): Context = ctx ?: error("FallbackAppContext not initialized")
}
