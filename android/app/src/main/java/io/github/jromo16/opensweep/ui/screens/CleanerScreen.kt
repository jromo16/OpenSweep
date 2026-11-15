package io.github.jromo16.opensweep.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.background.CacheCleaner
import io.github.jromo16.opensweep.background.CacheCleanerService
import io.github.jromo16.opensweep.background.StorageWidget
import io.github.jromo16.opensweep.core.AppManager
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.core.JunkGroup
import io.github.jromo16.opensweep.core.JunkType
import io.github.jromo16.opensweep.core.Permissions
import io.github.jromo16.opensweep.core.StorageScanner
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.ui.NavHints
import io.github.jromo16.opensweep.ui.PermissionCard
import io.github.jromo16.opensweep.ui.ScanState
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.rememberPermissionState
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun CleanerScreen(modifier: Modifier, onMessengers: () -> Unit) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val perms = rememberPermissionState()
    var groups by remember { mutableStateOf<List<JunkGroup>>(emptyList()) }
    val selected = remember {
        mutableStateListOf<JunkType>().apply {
            addAll(JunkType.entries.filter { it.defaultOn })
            NavHints.cleanerSelect?.let { if (it !in this) add(it) }
            NavHints.cleanerSelect = null
        }
    }
    var busy by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    var confirm by remember { mutableStateOf(false) }
    var askAccessibility by remember { mutableStateOf(false) }

    /** Borra la caché de las demás apps pulsando «Borrar caché» en sus ajustes (servicio de accesibilidad). */
    fun clearAppCaches(filesMsg: String) {
        scope.launch {
            val before = withContext(Dispatchers.IO) { AppManager.appsWithCache(ctx, 256 * 1024) }
            if (before.isEmpty()) return@launch
            val beforeBytes = before.sumOf { it.third }
            CacheCleaner.start(ctx, before.map { it.first to it.second }) { n ->
                scope.launch {
                    val after = withContext(Dispatchers.IO) { AppManager.totalCacheBytes(ctx) ?: 0L }
                    val freed = (beforeBytes - after).coerceAtLeast(0)
                    status = "$filesMsg · ${t("Caché de")} $n ${t("apps borrada")}" + (if (freed > 0) ": ${formatBytes(freed)}" else "")
                    ScanState.invalidate()
                    StorageWidget.updateAll(ctx)
                }
            }
        }
    }

    fun analyze() {
        scope.launch {
            busy = true
            status = t("Analizando…")
            val scanner = ScanState.scan(ctx)
            val cache = withContext(Dispatchers.IO) { AppManager.totalCacheBytes(ctx) }
            groups = withContext(Dispatchers.Default) { scanner.junkGroups(cache) }
            val total = groups.filter { it.type in selected }.sumOf { it.bytes }
            status = "${t("Se pueden liberar")} ${formatBytes(total)}"
            busy = false
        }
    }

    fun clean() {
        scope.launch {
            busy = true
            status = t("Limpiando…")
            val toClean = groups.filter { it.type in selected }
            val (bytes, count) = withContext(Dispatchers.IO) {
                var b = 0L; var c = 0
                toClean.forEach { g ->
                    val toTrash = g.type in setOf(JunkType.APKS, JunkType.DOWNLOADS, JunkType.LEFTOVERS)
                    val (bb, cc) = StorageScanner.delete(if (g.type == JunkType.LEFTOVERS) emptyList() else g.files, g.folders, recursive = g.type == JunkType.LEFTOVERS,
                        trashCtx = if (toTrash) ctx else null)
                    b += bb; c += cc
                }
                b to c
            }
            ScanState.invalidate()
            StorageWidget.updateAll(ctx)
            val otherCache = JunkType.APP_CACHE in selected && (groups.firstOrNull { it.type == JunkType.APP_CACHE }?.extraBytes ?: 0) > 0
            groups = emptyList()
            status = "${t("Listo")}: ${formatBytes(bytes)} ${t("limpiados")} ($count ${t("elementos")})"
            busy = false
            // La caché de las demás apps: desde Android 11 solo se puede borrar desde los ajustes de cada app
            if (otherCache) {
                if (CacheCleanerService.instance != null) clearAppCaches(status) else askAccessibility = true
            }
        }
    }

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(16.dp))
        ScreenTitle(t("Limpieza"), t("Elimina archivos basura de forma segura"))
        if (!perms.storage) {
            PermissionCard(Icons.Filled.Lock, t("Se necesita acceso a los archivos"),
                t("Sin este permiso solo se puede limpiar la caché."), t("Conceder acceso"), perms.requestStorage)
        }
        if (!perms.usage) {
            PermissionCard(Icons.Filled.Lock, "${t("Acceso de uso (opcional")})",
                t("Permite medir la caché de todas las apps instaladas."), t("Conceder"), perms.requestUsage, onRestricted = perms.openAppDetails)
        }

        SectionCard {
            Text(if (status.isEmpty()) t("Pulsa «Analizar» para empezar") else status, fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            if (CacheCleaner.running) {
                Spacer(Modifier.height(8.dp))
                LinearProgressIndicator(progress = { if (CacheCleaner.total == 0) 0f else CacheCleaner.done.toFloat() / CacheCleaner.total }, modifier = Modifier.fillMaxWidth())
                Text("${t("Borrando caché")}: ${CacheCleaner.done} / ${CacheCleaner.total} · ${CacheCleaner.current}", fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                TextButton(onClick = { CacheCleaner.cancel() }) { Text(t("Detener")) }
            }
            if (busy) {
                Spacer(Modifier.height(8.dp))
                LinearProgressIndicator(Modifier.fillMaxWidth())
                Text(ScanState.progress, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { analyze() }, enabled = !busy && !CacheCleaner.running) { Text(t("Analizar")) }
                Button(onClick = { confirm = true }, enabled = !busy && !CacheCleaner.running && groups.isNotEmpty() && selected.isNotEmpty()) { Text(t("Limpiar")) }
            }
        }
        Spacer(Modifier.height(12.dp))

        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            item {
                SectionCard {
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable(onClick = onMessengers)) {
                        Column(Modifier.weight(1f)) {
                            Text(t("WhatsApp y Telegram"), fontWeight = FontWeight.SemiBold)
                            Text(t("Vídeos, audios, stickers y estados de tus chats"), fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null)
                    }
                }
            }
            items(if (groups.isEmpty()) JunkType.entries.map { JunkGroup(it) } else groups, key = { it.type.name }) { g ->
                JunkRow(
                    group = g,
                    analyzed = groups.isNotEmpty(),
                    checked = g.type in selected,
                    onChecked = { if (it) selected.add(g.type) else selected.remove(g.type) },
                )
            }
            item { Spacer(Modifier.height(16.dp)) }
        }
    }

    if (askAccessibility) {
        AlertDialog(
            onDismissRequest = { askAccessibility = false },
            title = { Text(t("Borrar la caché de todas las apps")) },
            text = {
                Text(t("Android no deja que una app borre la caché de otras. OpenSweep puede hacerlo por ti abriendo los ajustes de cada app y pulsando «Borrar caché», con el servicio de accesibilidad.") + "\n\n" +
                    t("Solo lo usa mientras limpias: no lee nada más, no guarda nada y no se conecta a internet.") + "\n\n" +
                    t("Activa «OpenSweep» en la lista, vuelve aquí, pulsa «Analizar» y después «Limpiar». Si Android dice «Ajuste restringido», ve a Ajustes › Apps › OpenSweep › ⋮ › Permitir ajustes restringidos."))
            },
            confirmButton = {
                TextButton(onClick = {
                    askAccessibility = false
                    try { ctx.startActivity(CacheCleanerService.settingsIntent()) } catch (_: Exception) { }
                }) { Text(t("Activar")) }
            },
            dismissButton = { TextButton(onClick = { askAccessibility = false }) { Text(t("Ahora no")) } },
        )
    }

    if (confirm) {
        val total = groups.filter { it.type in selected }.sumOf { it.bytes }
        AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text(t("¿Limpiar ahora?")) },
            text = { Text("${t("Se eliminarán")} ${formatBytes(total)}. ${t("Los instaladores APK, las descargas y los restos de apps van a la papelera de OpenSweep (30 días); lo demás se borra.")}") },
            confirmButton = { TextButton(onClick = { confirm = false; clean() }) { Text(t("Limpiar")) } },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(t("Cancelar")) } },
        )
    }
}

@Composable
private fun JunkRow(group: JunkGroup, analyzed: Boolean, checked: Boolean, onChecked: (Boolean) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    SectionCard {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable(enabled = analyzed && group.count > 0) { expanded = !expanded }) {
            Checkbox(checked = checked, onCheckedChange = onChecked)
            Column(Modifier.weight(1f)) {
                Text(t(group.type.title), fontWeight = FontWeight.SemiBold)
                Text(t(group.type.description), fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                group.note?.let { Text(t(it), fontSize = 12.sp, color = MaterialTheme.colorScheme.secondary) }
            }
            if (analyzed) {
                Column(horizontalAlignment = Alignment.End) {
                    Text(formatBytes(group.bytes), color = Brand, fontWeight = FontWeight.SemiBold)
                    Text("${group.count} elem.", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                if (group.count > 0) Icon(if (expanded) Icons.Filled.KeyboardArrowUp else Icons.Filled.KeyboardArrowDown, null)
            }
        }
        if (expanded) {
            val lines = group.folders.map { "📁 $it" } + group.files.sortedByDescending { it.size }.map { "${formatBytes(it.size)} · ${it.path}" }
            lines.take(60).forEach {
                Text(it, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.MiddleEllipsis, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (lines.size > 60) Text("… y ${lines.size - 60} ${t("más")}", fontSize = 12.sp)
        }
    }
}
