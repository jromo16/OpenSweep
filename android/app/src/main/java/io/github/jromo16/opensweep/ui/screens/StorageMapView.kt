package io.github.jromo16.opensweep.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.Box as MapBox
import io.github.jromo16.opensweep.core.FolderSizes
import io.github.jromo16.opensweep.core.MapEntry
import io.github.jromo16.opensweep.core.Treemap
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.t

private val Palette = listOf(
    Color(0xFF10B981), Color(0xFF0A84FF), Color(0xFFBF5AF2), Color(0xFFFF9F0A), Color(0xFFFF375F),
    Color(0xFF30B0C7), Color(0xFF5E5CE6), Color(0xFF32B65A), Color(0xFFFF453A), Color(0xFF64A0FF),
)

/**
 * Mapa del almacenamiento (como el de Windows): cada rectángulo es una carpeta, de tamaño proporcional
 * a lo que ocupa. Tocar una carpeta entra en ella; la flecha vuelve atrás. Los archivos sueltos van en gris.
 */
@Composable
fun StorageMapView(sizes: FolderSizes) {
    var dir by remember(sizes) { mutableStateOf(sizes.root.trimEnd('/')) }
    val root = sizes.root.trimEnd('/')
    val entries = remember(sizes, dir) { sizes.entries(dir).take(60) }

    Column(Modifier.fillMaxSize()) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            IconButton(onClick = { dir = dir.substringBeforeLast('/').let { if (it.length < root.length) root else it } },
                enabled = dir.length > root.length) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, t("Subir"))
            }
            Column(Modifier.weight(1f)) {
                Text(if (dir == root) t("Almacenamiento interno") else dir.removePrefix("$root/"),
                    fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.MiddleEllipsis)
                Text(formatBytes(sizes.total[dir] ?: 0L), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        if (entries.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(t("Esta carpeta no tiene subcarpetas."), color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            return@Column
        }
        BoxWithConstraints(
            Modifier.weight(1f).fillMaxWidth().clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surfaceContainerHigh)
        ) {
            val density = LocalDensity.current
            val wPx = with(density) { maxWidth.toPx() }
            val hPx = with(density) { maxHeight.toPx() }
            val layout = remember(entries, wPx, hPx) {
                Treemap.layout(entries.map { it to it.bytes.toDouble() }, MapBox(0f, 0f, wPx, hPx))
            }
            var colorIndex = 0
            layout.forEach { (e: MapEntry, r) ->
                val color = if (e.isFiles) Color(0xFF8E8E93) else Palette[colorIndex++ % Palette.size]
                val w = with(density) { (r.w - 2).coerceAtLeast(0f).toDp() }
                val h = with(density) { (r.h - 2).coerceAtLeast(0f).toDp() }
                if (w < 2.dp || h < 2.dp) return@forEach
                Box(
                    Modifier
                        .offset(with(density) { (r.x + 1).toDp() }, with(density) { (r.y + 1).toDp() })
                        .size(w, h)
                        .clip(RoundedCornerShape(6.dp))
                        .background(color)
                        .clickable(enabled = !e.isFiles) { dir = e.path }
                ) {
                    if (w > 64.dp && h > 34.dp) {
                        Column(Modifier.padding(6.dp)) {
                            Text(if (e.isFiles) t("Archivos sueltos") else e.name, color = Color.White, fontWeight = FontWeight.SemiBold,
                                fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(formatBytes(e.bytes), color = Color.White.copy(alpha = 0.9f), fontSize = 11.sp, maxLines = 1)
                        }
                    }
                }
            }
        }
        Text(t("Toca una carpeta para ver qué hay dentro."), fontSize = 12.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 6.dp))
    }
}
