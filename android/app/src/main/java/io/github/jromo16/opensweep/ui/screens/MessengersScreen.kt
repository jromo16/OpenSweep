package io.github.jromo16.opensweep.ui.screens

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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.FileRec
import io.github.jromo16.opensweep.core.MessengerCategory
import io.github.jromo16.opensweep.core.Messengers
import io.github.jromo16.opensweep.core.StorageScanner
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.isSent
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.ui.BackHeader
import io.github.jromo16.opensweep.ui.ScanState
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun MessengersScreen(modifier: Modifier, onBack: () -> Unit) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var cats by remember { mutableStateOf<List<MessengerCategory>>(emptyList()) }
    var busy by remember { mutableStateOf(true) }
    var olderThanDays by remember { mutableIntStateOf(30) }
    var includeSent by remember { mutableStateOf(true) }
    val checked = remember { mutableStateListOf<String>() }
    var confirm by remember { mutableStateOf(false) }

    suspend fun load() {
        busy = true
        val s = ScanState.scanner ?: ScanState.scan(ctx)
        cats = withContext(Dispatchers.Default) { Messengers.categories(s.files) }
        checked.clear()
        // Por defecto: vídeos, GIF, stickers, estados y audios (fotos y documentos suelen ser importantes)
        cats.filter { it.name in setOf("Vídeos", "GIF", "Stickers", "Estados", "Videomensajes", "Audios") }.forEach { checked.add(it.key) }
        busy = false
    }
    LaunchedEffect(Unit) { load() }

    fun filtered(c: MessengerCategory): List<FileRec> {
        val limit = System.currentTimeMillis() - olderThanDays * 86_400_000L
        return c.files.filter { (olderThanDays == 0 || it.modified < limit) && (includeSent || !it.isSent) }
    }
    val toDelete = cats.filter { it.key in checked }.flatMap { filtered(it) }

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        BackHeader(t("WhatsApp y Telegram"), t("Fotos, vídeos y audios recibidos en tus chats"), onBack)
        if (busy) {
            LinearProgressIndicator(Modifier.fillMaxWidth())
            Text(ScanState.progress, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            return@Column
        }
        if (cats.isEmpty()) {
            SectionCard { Text(t("No se encontraron archivos de WhatsApp ni de Telegram.")) }
            return@Column
        }
        SectionCard {
            Text("${t("Total")}: ${formatBytes(cats.sumOf { it.bytes })}", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text(t("Solo borrar archivos con más de:"), fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(listOf(0, 7, 30, 90, 365)) { d ->
                    FilterChip(selected = olderThanDays == d, onClick = { olderThanDays = d },
                        label = { Text(if (d == 0) t("Todos") else "$d ${t("días")}") })
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Switch(checked = includeSent, onCheckedChange = { includeSent = it })
                Spacer(Modifier.padding(4.dp))
                Text(t("Incluir archivos enviados por mí"))
            }
        }
        Spacer(Modifier.height(8.dp))
        Box(Modifier.weight(1f)) {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                items(cats, key = { it.key }) { c ->
                    val f = filtered(c)
                    SectionCard {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(checked = c.key in checked, onCheckedChange = { if (it) checked.add(c.key) else checked.remove(c.key) })
                            Column(Modifier.weight(1f)) {
                                Text("${t(c.name)} · ${c.app}", fontWeight = FontWeight.SemiBold)
                                Text("${c.files.size} ${t("archivos")} · ${t("enviados")}: ${formatBytes(c.sentBytes)}",
                                    fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            Column(horizontalAlignment = Alignment.End) {
                                Text(formatBytes(f.sumOf { it.size }), color = Brand, fontWeight = FontWeight.SemiBold)
                                Text("${t("de")} ${formatBytes(c.bytes)}", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                    }
                }
            }
        }
        Button(onClick = { confirm = true }, enabled = toDelete.isNotEmpty(), modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
            Text("${t("Eliminar")} ${toDelete.size} ${t("archivos")} (${formatBytes(toDelete.sumOf { it.size })})")
        }
    }

    if (confirm) {
        AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text(t("¿Eliminar archivos de los chats?")) },
            text = { Text(t("Los mensajes se conservan, pero los archivos eliminados ya no se podrán abrir desde el chat.")) },
            confirmButton = {
                TextButton(onClick = {
                    confirm = false
                    val list = toDelete
                    scope.launch {
                        withContext(Dispatchers.IO) { StorageScanner.delete(list) }
                        ScanState.invalidate()
                        load()
                    }
                }) { Text(t("Eliminar")) }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(t("Cancelar")) } },
        )
    }
}
