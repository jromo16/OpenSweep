package io.github.jromo16.opensweep.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.ui.graphics.Color

/** Verde de la marca: se adapta al tema (más oscuro en modo claro para que se lea bien). */
val Brand: Color
    @Composable @ReadOnlyComposable get() = MaterialTheme.colorScheme.primary

val Danger = Color(0xFFF87171)
val Warn = Color(0xFFFBBF24)
val Muted = Color(0xFF9AA4B2)

// Paletas inspiradas en macOS / iOS
private val DarkColors = darkColorScheme(
    primary = Color(0xFF34D399),
    onPrimary = Color(0xFF00281A),
    primaryContainer = Color(0xFF0B3B2C),
    onPrimaryContainer = Color(0xFFB7F5DC),
    secondary = Color(0xFF34D399),
    secondaryContainer = Color(0xFF0B3B2C),
    onSecondaryContainer = Color(0xFFB7F5DC),
    background = Color(0xFF000000),
    onBackground = Color(0xFFF5F5F7),
    surface = Color(0xFF000000),
    onSurface = Color(0xFFF5F5F7),
    surfaceVariant = Color(0xFF2C2C2E),
    onSurfaceVariant = Color(0xFF98989D),
    surfaceContainerLowest = Color(0xFF000000),
    surfaceContainerLow = Color(0xFF111113),
    surfaceContainer = Color(0xFF161618),
    surfaceContainerHigh = Color(0xFF1C1C1E),
    surfaceContainerHighest = Color(0xFF2C2C2E),
    outline = Color(0xFF48484A),
    outlineVariant = Color(0xFF38383A),
    error = Color(0xFFF87171),
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF059669),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFD1FAE5),
    onPrimaryContainer = Color(0xFF065F46),
    secondary = Color(0xFF059669),
    secondaryContainer = Color(0xFFD1FAE5),
    onSecondaryContainer = Color(0xFF065F46),
    background = Color(0xFFF2F2F7),
    onBackground = Color(0xFF1D1D1F),
    surface = Color(0xFFF2F2F7),
    onSurface = Color(0xFF1D1D1F),
    surfaceVariant = Color(0xFFE5E5EA),
    onSurfaceVariant = Color(0xFF6E6E73),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFF7F7FA),
    surfaceContainer = Color(0xFFF9F9FB),
    surfaceContainerHigh = Color(0xFFFFFFFF),
    surfaceContainerHighest = Color(0xFFE5E5EA),
    outline = Color(0xFFC7C7CC),
    outlineVariant = Color(0xFFE5E5EA),
    error = Color(0xFFDC2626),
)

/** mode: auto | light | dark */
@Composable
fun isDarkTheme(mode: String): Boolean = when (mode) {
    "light" -> false
    "dark" -> true
    else -> isSystemInDarkTheme()
}

@Composable
fun OpenSweepTheme(dark: Boolean, content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (dark) DarkColors else LightColors, content = content)
}
