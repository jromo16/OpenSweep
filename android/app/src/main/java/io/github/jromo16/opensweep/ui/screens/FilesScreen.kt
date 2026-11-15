package io.github.jromo16.opensweep.ui.screens

import io.github.jromo16.opensweep.core.t
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.PrimaryScrollableTabRow
import androidx.compose.material3.Surface
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
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
import io.github.jromo16.opensweep.core.FileRec
import io.github.jromo16.opensweep.core.FolderSizes
import io.github.jromo16.opensweep.core.MediaStats
import io.github.jromo16.opensweep.core.StorageScanner
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.formatDate
import io.github.jromo16.opensweep.ui.LabeledBar
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

private const val MB = 1024L * 1024L

@Composable
fun FilesScreen(modifier: Modifier) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val perms = rememberPermissionState()
    var tab by remember { mutableIntStateOf(NavHints.filesTab ?: 0).also { NavHints.filesTab = null } }
    var stats by remember { mutableStateOf<List<MediaStats>>(emptyList()) }
    var large by remember { mutableStateOf<List<FileRec>>(emptyList()) }
    var dups by remember { mutableStateOf<List<List<FileRec>>>(emptyList()) }
    var folderSizes by remember { mutableStateOf<FolderSizes?>(null) }
    var minLarge by remember { mutableLongStateOf((NavHints.filesMinMb ?: 50) * MB).also { NavHints.filesMinMb = null } }
    var busy by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    val checked = remember { mutableStateListOf<String>() }
    var confirm by remember { mutableStateOf(false) }

    suspend fun scanner(): StorageScanner = ScanState.scanner ?: ScanState.scan(ctx)

    fun load() {
        scope.launch {
            busy = true
            checked.clear()
            val s = scanner()
            when (tab) {
                0 -> stats = withContext(Dispatchers.Default) { s.mediaStats() }
                1 -> large = withContext(Dispatchers.Default) { s.largeFiles(minLarge) }
                2 -> {
                    dups = withContext(Dispatchers.IO) { s.duplicates(1 * MB) { status = it } }
                    // marcar todas las copias excepto la más antigua
                    dups.forEach { g -> g.drop(1).forEach { checked.add(it.path) } }
                    status = "${dups.size} ${t("grupos")} · ${formatBytes(dups.sumOf { it.first().size * (it.size - 1) })} ${t("recuperables")}"
                }
                3 -> folderSizes = withContext(Dispatchers.Default) { FolderSizes(s.root.absolutePath, s.files) }
            }
            busy = false
        }
    }

    LaunchedEffect(tab, minLarge, perms.storage) { if (perms.storage) load() }

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(16.dp))
        ScreenTitle(t("Archivos"), t("Encuentra lo que más espacio ocupa"))
        if (!perms.storage) {
            PermissionCard(Icons.Filled.Lock, t("Se necesita acceso a los archivos"),
                t("Para analizar fotos, vídeos, descargas y duplicados."), t("Conceder acceso"), perms.requestStorage)
            return@Column
        }
        PrimaryScrollableTabRow(selectedTabIndex = tab, containerColor = MaterialTheme.colorScheme.background, edgePadding = 0.dp) {
            listOf(t("Resumen"), t("Grandes"), t("Duplicados"), t("Mapa")).forEachIndexed { i, t ->
                Tab(selected = tab == i, onClick = { tab = i }, text = { Text(t, maxLines = 1) })
            }
        }
        Spacer(Modifier.height(8.dp))
        if (busy) {
            LinearProgressIndicator(Modifier.fillMaxWidth())
            Text(if (tab == 2) status else ScanState.progress, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }

        Box(Modifier.weight(1f)) {
            when (tab) {
                0 -> LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item {
                        SectionCard {
                            val total = stats.sumOf { it.bytes }.coerceAtLeast(1)
                            Text(t("Uso por tipo de archivo"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                            stats.forEach { s ->
                                LabeledBar("${t(s.name)} (${s.count})", formatBytes(s.bytes), s.bytes.toFloat() / total, Brand)
                            }
                        }
                    }
                    item {
                        TextButton(onClick = { ScanState.invalidate(); load() }) { Text(t("Volver a analizar")) }
                    }
                }
                1 -> Column {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(listOf(10L, 50L, 100L, 500L)) { mb ->
                            FilterChip(selected = minLarge == mb * MB, onClick = { minLarge = mb * MB }, label = { Text("> $mb MB") })
                        }
                    }
                    FileList(large, checked)
                }
                3 -> folderSizes?.let { StorageMapView(it) }
                else -> Column {
                    if (!busy) Text(status, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    LazyColumn(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        dups.forEachIndexed { gi, g ->
                            item(key = "h$gi") {
                                Text("${t("Grupo")} ${gi + 1} · ${g.size} ${t("copias de")} ${formatBytes(g.first().size)}",
                                    fontWeight = FontWeight.SemiBold, color = Brand, modifier = Modifier.padding(top = 8.dp))
                            }
                            items(g, key = { "d$gi-${it.path}" }) { f -> FileRow(f, f.path in checked) { c -> if (c) checked.add(f.path) else checked.remove(f.path) } }
                        }
                    }
                }
            }
        }

        if (checked.isNotEmpty() && tab in 1..2) {
            val all = (large + dups.flatten()).distinctBy { it.path }.filter { it.path in checked }
            Button(onClick = { confirm = true }, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                Text("${t("Eliminar")} ${all.size} ${t("archivos")} (${formatBytes(all.sumOf { it.size })})")
            }
        }
    }

    if (confirm) {
        val all = (large + dups.flatten()).distinctBy { it.path }.filter { it.path in checked }
        // Nunca borrar todas las copias de un grupo de duplicados
        val unsafe = tab == 2 && dups.any { g -> g.all { it.path in checked } }
        AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text(if (unsafe) t("Revisa la selección") else t("¿Eliminar archivos?")) },
            text = {
                Text(
                    if (unsafe) t("Has marcado todas las copias de algún grupo. Deja al menos una copia sin marcar.")
                    else "${t("Se eliminarán")} ${all.size} ${t("archivos")} (${formatBytes(all.sumOf { it.size })}) ${t("de forma permanente.")}"
                )
            },
            confirmButton = {
                if (!unsafe) TextButton(onClick = {
                    confirm = false
                    scope.launch {
                        busy = true
                        val (bytes, _) = withContext(Dispatchers.IO) { StorageScanner.delete(all, trashCtx = ctx) }
                        ScanState.invalidate()
                        status = "${t("Se liberaron")} ${formatBytes(bytes)}"
                        busy = false
                        load()
                    }
                }) { Text(t("Eliminar")) }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(if (unsafe) t("Entendido") else t("Cancelar")) } },
        )
    }
}

@Composable
private fun FileList(files: List<FileRec>, checked: MutableList<String>) {
    LazyColumn(verticalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.padding(top = 8.dp)) {
        items(files, key = { it.path }) { f ->
            FileRow(f, f.path in checked) { c -> if (c) checked.add(f.path) else checked.remove(f.path) }
        }
    }
}

@Composable
private fun FileRow(f: FileRec, checked: Boolean, onChecked: (Boolean) -> Unit) {
    Surface(shape = RoundedCornerShape(12.dp), color = MaterialTheme.colorScheme.surfaceContainerHigh, modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked = checked, onCheckedChange = onChecked)
            Column(Modifier.weight(1f)) {
                Text(f.name, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${formatDate(f.modified)} · ${f.folder}", fontSize = 12.sp, maxLines = 1,
                    overflow = TextOverflow.MiddleEllipsis, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(formatBytes(f.size), color = Brand, fontWeight = FontWeight.Medium, fontSize = 14.sp)
        }
    }
}
