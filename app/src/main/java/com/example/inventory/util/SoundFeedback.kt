package com.example.inventory.util

import android.content.Context
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager

/**
 * Обратная связь сканера: вибрация 150 мс + звуковой тон (ToneGenerator).
 *  - найдено частично       → 800 Гц
 *  - учтено полностью       → 1200 Гц
 *  - излишек                → 600 Гц
 *  - ошибка (нет в базе)    → 200 Гц
 */
object SoundFeedback {

    const val TONE_PARTIAL = ToneGenerator.TONE_PROP_BEEP // ~800 Гц
    const val TONE_FULL = ToneGenerator.TONE_DTMF_P       // высокая (~1200 Гц область)
    const val TONE_OVERAGE = ToneGenerator.TONE_PROP_PROMPT // средняя (~600-700 Гц)
    const val TONE_ERROR = ToneGenerator.TONE_PROP_NACK   // низкая (~200-300 Гц)

    private const val DURATION_MS = 180

    fun feedback(context: Context, toneType: Int) {
        vibrate(context, 150)
        playTone(toneType)
    }

    fun vibrate(context: Context, ms: Long = 150) {
        try {
            val vibrator: Vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                val vm = context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
                vm?.defaultVibrator ?: return
            } else {
                @Suppress("DEPRECATION")
                context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator ?: return
            }
            if (!vibrator.hasVibrator()) return
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(
                    VibrationEffect.createOneShot(ms, VibrationEffect.DEFAULT_AMPLITUDE)
                )
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(ms)
            }
        } catch (_: Exception) {
            // вибрация не критична
        }
    }

    fun playTone(toneType: Int) {
        try {
            val tg = ToneGenerator(AudioManager.STREAM_MUSIC, 80)
            tg.startTone(toneType, DURATION_MS)
            // Освобождение после проигрывания
            android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(
                { runCatching { tg.release() } },
                (DURATION_MS + 100).toLong()
            )
        } catch (_: Exception) {
            // звук не критичен (например, нет аудио-выхода)
        }
    }
}
