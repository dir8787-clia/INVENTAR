package com.example.inventory.util

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import java.io.ByteArrayOutputStream
import java.util.concurrent.ConcurrentHashMap

/**
 * Генерация QR-битмапов (ZXing core). Вызывать только в фоновых потоках (Dispatchers.IO).
 * Есть LRU-кэш для предпросмотра этикеток.
 */
object QrGenerator {

    /** Кэш: content -> Bitmap. Простой size-capped кэш. */
    private val cache = ConcurrentHashMap<String, Bitmap>()
    private const val MAX_CACHE = 800

    fun generate(content: String, sizePx: Int): Bitmap {
        cache[content]?.takeIf { !it.isRecycled && it.width == sizePx }?.let { return it }

        val hints = mapOf(
            EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M,
            EncodeHintType.MARGIN to 1, // минимальный отступ — важно для точной сетки печати
            EncodeHintType.CHARACTER_SET to "UTF-8"
        )
        val matrix = QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, sizePx, sizePx, hints)

        val bmp = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        for (x in 0 until sizePx) {
            for (y in 0 until sizePx) {
                bmp.setPixel(x, y, if (matrix[x, y]) Color.BLACK else Color.WHITE)
            }
        }

        if (cache.size >= MAX_CACHE) cache.clear() // грубая очистка вместо вытеснения
        cache[content] = bmp
        return bmp
    }

    fun clearCache() {
        cache.values.forEach { runCatching { it.recycle() } }
        cache.clear()
    }

    /** Запись битмапа в PNG по SAF-uri (используется редко; основной вывод — PDF). */
    fun writePng(context: Context, uri: Uri, bmp: Bitmap) {
        context.contentResolver.openOutputStream(uri)?.use { out ->
            val bos = ByteArrayOutputStream()
            bmp.compress(Bitmap.CompressFormat.PNG, 100, bos)
            out.write(bos.toByteArray())
        }
    }
}
