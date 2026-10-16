package com.example.inventory.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

private val LightColors = lightColorScheme(
    primary = Color(0xFF1B5E20),
    onPrimary = Color.White,
    secondary = Color(0xFF00695C),
    tertiary = Color(0xFF4527A0)
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF81C784),
    onPrimary = Color(0xFF003300),
    secondary = Color(0xFF80CBC4),
    tertiary = Color(0xFFB39DDB)
)

@Composable
fun InventoryTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colors = if (darkTheme) DarkColors else LightColors
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            window.statusBarColor = colors.primary.toArgb()
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !darkTheme
        }
    }
    MaterialTheme(
        colorScheme = colors,
        typography = androidx.compose.material3.Typography(),
        content = content
    )
}
