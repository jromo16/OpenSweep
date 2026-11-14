package io.github.jromo16.opensweep.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val Brand = Color(0xFF34D399)
val BrandDark = Color(0xFF10B981)
val Danger = Color(0xFFF87171)
val Warn = Color(0xFFFBBF24)
val Muted = Color(0xFF9AA4B2)

private val Colors = darkColorScheme(
    primary = Brand,
    onPrimary = Color(0xFF00281A),
    primaryContainer = Color(0xFF0B3B2C),
    onPrimaryContainer = Color(0xFFB7F5DC),
    secondary = Color(0xFF7DD3FC),
    background = Color(0xFF101317),
    onBackground = Color(0xFFE6E9EE),
    surface = Color(0xFF101317),
    onSurface = Color(0xFFE6E9EE),
    surfaceVariant = Color(0xFF1F242B),
    onSurfaceVariant = Muted,
    surfaceContainer = Color(0xFF181C22),
    surfaceContainerHigh = Color(0xFF1F242B),
    surfaceContainerHighest = Color(0xFF262C34),
    surfaceContainerLow = Color(0xFF15191E),
    outline = Color(0xFF3A424D),
    outlineVariant = Color(0xFF2C333D),
    secondaryContainer = Color(0xFF0B3B2C),
    onSecondaryContainer = Color(0xFFB7F5DC),
    error = Danger,
)

@Composable
fun OpenSweepTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = Colors, content = content)
}
