package com.example.inventory.util

import java.text.Normalizer
import java.util.Locale

/**
 * ЕДИНАЯ функция нормализации инвентарных номеров (импорт / скан / ручной ввод).
 *
 * Алгоритм (по ТЗ п.4):
 * 1. NFKC-нормализация (java.text.Normalizer)
 * 2. Удалить ВСЕ пробельные символы: пробел, \t, \n, \r, \u00A0, \u2000–\u200B,
 *    \u2028–\u2029, \uFEFF, \u00AD (мягкий дефис и zero-width space тоже вычищаются)
 * 3. Удалить символы \uFFFD (битая кодировка из Excel)
 * 4. Оставить только буквы (любые Unicode), цифры и дефис «-»
 * 5. Верхний регистр (locale-independent — для «бп» → «БП»)
 */
object InvNormalizer {

    private val WHITESPACE_AND_HIDDEN = Regex(
        "[\\s\\u00A0\\u2000-\\u200B\\u2028\\u2029\\uFEFF\\u00AD]"
    )

    // Всё, кроме букв любого регистра/алфавита, цифр и ASCII-дефиса
    private val FORBIDDEN = Regex("[^\\p{L}\\p{N}\\-]")

    fun normalize(raw: String?): String {
        if (raw == null) return ""
        // 1. NFKC
        var s = Normalizer.normalize(raw, Normalizer.Form.NFKC)
        // 2 + 3. Пробельные/невидимые символы и replacement char
        s = s.replace(WHITESPACE_AND_HIDDEN, "")
        s = s.replace("\uFFFD", "")
        // 4. Только буквы, цифры, дефис
        s = s.replace(FORBIDDEN, "")
        // 5. ВЕРХНИЙ РЕГИСТР
        return s.uppercase(Locale.ROOT)
    }

    /** Проверка: является ли строка уже нормализованным непустым номером. */
    fun isValidNormalized(s: String): Boolean =
        s.isNotEmpty() && s == normalize(s)
}

