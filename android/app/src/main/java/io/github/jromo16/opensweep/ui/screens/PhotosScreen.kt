package io.github.jromo16.opensweep.ui.screens

import android.content.Intent
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.MediaAnalyzer
import io.github.jromo16.opensweep.core.MediaRec
import io.github.jromo16.opensweep.core.MediaResult
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.formatDate
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.ui.PermissionCard
import io.github.jromo16.opensweep.ui.ScanState
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.rememberPermissionState
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Resultado guardado mientras la app está abierta (para no repetir el análisis al cambiar de pestaña). */
private object PhotoCache { var result: MediaResult? = null }

private data class Section(val title: String, val groups: List<Pair<String, List<MediaRec>>>, val isSimilar: Boolean)

@Composable
fun PhotosScreen(modifier: Modifier) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val perms = rememberPermissionState()
    var result by remember { mutableStateOf(PhotoCache.result) }
    var busy by remember { mutableStateOf(false) }
    var progress by remember { mutableStateOf("") }
    var tab by remember { mutableIntStateOf(0) }
    val selected = remember { mutableStateListOf<String>() }  // uris marcadas
    var confirm by remember { mutableStateOf(false) }

    fun defaultSelection(r: MediaResult) {
        selected.clear()
        // Los grupos de capturas de pantalla no se premarcan: dos capturas parecidas pueden tener información distinta
        r.similar.filter { g -> g.none { it.isScreenshot } }.forEach { g -> g.filter { !it.isBest }.forEach { selected.add(it.uri.toString()) } }
        // Los vídeos «similares» tampoco: solo se compara la miniatura y dos clips distintos pueden parecerse
    }

    fun analyze() = scope.launch {
        busy = true
        val r = withContext(Dispatchers.IO) { MediaAnalyzer.analyze(ctx, 6) { progress = it } }
        PhotoCache.result = r
        result = r
        defaultSelection(r)
        busy = false
    }

    val sections = result?.let { r ->
        fun byMonth(l: List<MediaRec>) = l.groupBy { formatDate(it.modified).substring(3) }.toList()
        listOf(
            Section(t("Similares"), r.similar.mapIndexed { i, g -> "${t("Grupo")} ${i + 1}" to g }, true),
            Section(t("Vídeos"), r.similarVideos.mapIndexed { i, g -> "${t("Grupo")} ${i + 1}" to g }, true),
            Section(t("Borrosas"), byMonth(r.blurry), false),
            Section(t("Oscuras"), byMonth(r.dark), false),
            Section(t("Capturas"), byMonth(r.screenshots), false),
        )
    } ?: emptyList()

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(16.dp))
        ScreenTitle(t("Fotos"), t("Similares, borrosas, oscuras y capturas"))
        if (!perms.storage) {
            PermissionCard(Icons.Filled.Lock, t("Se necesita acceso a los archivos"),
                t("Para analizar tus fotos y vídeos."), t("Conceder acceso"), perms.requestStorage)
            return@Column
        }
        if (result == null || busy) {
            SectionCard {
                Text(t("Libera espacio en tu galería"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                Text(t("OpenSweep compara cómo se ven tus fotos para encontrar ráfagas, copias reenviadas y fotos de mala calidad. Siempre conserva la mejor de cada grupo."),
                    fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.height(10.dp))
                if (busy) {
                    LinearProgressIndicator(Modifier.fillMaxWidth())
                    Text(progress, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else Button(onClick = { analyze() }) { Text(t("Analizar fotos")) }
            }
            return@Column
        }

        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(bottom = 8.dp)) {
            items(sections.size) { i ->
                val count = sections[i].groups.sumOf { it.second.size }
                FilterChip(selected = tab == i, onClick = { tab = i }, label = { Text("${sections[i].title} ($count)") })
            }
        }
        val section = sections[tab]
        val sectionItems = section.groups.flatMap { it.second }
        val marked = sectionItems.filter { it.uri.toString() in selected }

        Box(Modifier.weight(1f)) {
            if (sectionItems.isEmpty()) {
                Text(t("No se encontró nada aquí. ¡Bien!"), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(16.dp))
            }
            LazyVerticalGrid(columns = GridCells.Fixed(3), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                section.groups.forEach { (title, items) ->
                    item(span = { GridItemSpan(maxLineSpan) }, key = "h-${section.title}-$title") {
                        Text("$title · ${formatBytes(items.sumOf { it.size })}", color = Brand, fontWeight = FontWeight.SemiBold,
                            modifier = Modifier.padding(top = 10.dp, bottom = 2.dp))
                    }
                    items(items, key = { "${section.title}-${it.uri}" }) { m ->
                        val key = m.uri.toString()
                        MediaTile(m, checked = key in selected, showBest = section.isSimilar) {
                            if (key in selected) selected.remove(key) else selected.add(key)
                        }
                    }
                }
            }
        }
        // Acciones secundarias arriba y el botón principal a todo el ancho (no se parte en pantallas estrechas)
        Row(Modifier.fillMaxWidth().padding(top = 4.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            TextButton(onClick = {
                // Marcar / desmarcar todo en esta pestaña (en similares nunca se marca la mejor)
                val keys = sectionItems.filter { !(section.isSimilar && it.isBest) }.map { it.uri.toString() }
                if (keys.all { it in selected }) selected.removeAll(keys) else keys.filter { it !in selected }.forEach { selected.add(it) }
            }) { Text(if (marked.isEmpty()) t("Marcar todo") else t("Desmarcar"), maxLines = 1) }
            TextButton(onClick = { analyze() }) { Text(t("Volver a analizar"), maxLines = 1) }
        }
        Button(onClick = { confirm = true }, enabled = marked.isNotEmpty(), modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp)) {
            Text("${t("Eliminar")} ${marked.size} · ${formatBytes(marked.sumOf { it.size })}", maxLines = 1)
        }

        if (confirm) {
            // Desde cualquier pestaña (similares, borrosas, oscuras…): nunca borrar todas las fotos de un grupo de similares
            val markedKeys = marked.map { it.uri.toString() }.toSet()
            val allGroups = result?.let { it.similar + it.similarVideos }.orEmpty()
            val unsafe = allGroups.any { g -> g.size > 1 && g.all { it.uri.toString() in markedKeys } }
            AlertDialog(
                onDismissRequest = { confirm = false },
                title = { Text(if (unsafe) t("Revisa la selección") else t("¿Eliminar archivos?")) },
                text = {
                    Text(if (unsafe) t("Has marcado todas las fotos de algún grupo. Deja al menos una sin marcar.")
                    else "${marked.size} ${t("archivos")} (${formatBytes(marked.sumOf { it.size })}) ${t("irán a la papelera de OpenSweep: podrás recuperarlos durante 30 días.")}")
                },
                confirmButton = {
                    if (!unsafe) TextButton(onClick = {
                        confirm = false
                        scope.launch {
                            busy = true
                            val deleted = withContext(Dispatchers.IO) { marked.filter { MediaAnalyzer.delete(ctx, it) }.map { it.uri }.toSet() }
                            val r = result!!
                            fun keep(l: List<MediaRec>) = l.filter { it.uri !in deleted }
                            fun keepGroups(l: List<List<MediaRec>>) = l.map(::keep).filter { it.size > 1 }
                            val nr = r.copy(similar = keepGroups(r.similar), similarVideos = keepGroups(r.similarVideos),
                                blurry = keep(r.blurry), dark = keep(r.dark), screenshots = keep(r.screenshots))
                            PhotoCache.result = nr; result = nr
                            selected.removeAll(deleted.map { it.toString() })
                            ScanState.invalidate()
                            busy = false
                        }
                    }) { Text(t("Eliminar")) }
                },
                dismissButton = { TextButton(onClick = { confirm = false }) { Text(if (unsafe) t("Entendido") else t("Cancelar")) } },
            )
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun MediaTile(m: MediaRec, checked: Boolean, showBest: Boolean, onToggle: () -> Unit) {
    val ctx = LocalContext.current
    val thumb by produceState<ImageBitmap?>(null, m.uri) {
        value = withContext(Dispatchers.IO) { MediaAnalyzer.thumbnail(ctx, m, 256)?.asImageBitmap() }
    }
    Column {
        Box(
            Modifier.fillMaxWidth().aspectRatio(1f).clip(RoundedCornerShape(10.dp))
                .background(MaterialTheme.colorScheme.surfaceContainerHigh)
                .border(2.dp, if (showBest && m.isBest) Brand else Color.Transparent, RoundedCornerShape(10.dp))
                .combinedClickable(
                    onClick = onToggle,
                    onLongClick = {
                        // Mantener pulsado: abrir la foto o el vídeo
                        try {
                            ctx.startActivity(Intent(Intent.ACTION_VIEW, m.uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
                        } catch (_: Exception) { }
                    }
                )
        ) {
            thumb?.let { Image(it, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop) }
            if (checked) Box(Modifier.fillMaxSize().background(Color(0x66000000)))
            Icon(
                Icons.Filled.CheckCircle, null,
                tint = if (checked) Brand else Color(0x99FFFFFF),
                modifier = Modifier.align(Alignment.TopStart).padding(4.dp)
            )
            if (m.isVideo) Icon(Icons.Filled.PlayArrow, null, tint = Color.White, modifier = Modifier.align(Alignment.Center))
            if (showBest && m.isBest) Text(
                t("Mejor"), color = Color.Black, fontSize = 11.sp, fontWeight = FontWeight.SemiBold,
                modifier = Modifier.align(Alignment.TopEnd).padding(4.dp).background(Brand, RoundedCornerShape(4.dp)).padding(horizontal = 5.dp)
            )
            Text(formatBytes(m.size), color = Color.White, fontSize = 11.sp,
                modifier = Modifier.align(Alignment.BottomEnd).background(Color(0x99000000), RoundedCornerShape(topStart = 6.dp)).padding(horizontal = 4.dp))
        }
    }
}
