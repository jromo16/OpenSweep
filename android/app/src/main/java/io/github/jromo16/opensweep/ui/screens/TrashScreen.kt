package io.github.jromo16.opensweep.ui.screens

import android.widget.Toast
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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import io.github.jromo16.opensweep.core.Trash
import io.github.jromo16.opensweep.core.TrashEntry
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.formatDate
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.ui.BackHeader
import io.github.jromo16.opensweep.ui.ScanState
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Papelera de OpenSweep: restaurar o eliminar definitivamente (se vacía sola a los 30 días). */
@Composable
fun TrashScreen(modifier: Modifier, onBack: () -> Unit) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var entries by remember { mutableStateOf<List<TrashEntry>>(emptyList()) }
    val checked = remember { mutableStateListOf<String>() }
    var busy by remember { mutableStateOf(false) }
    var confirmEmpty by remember { mutableStateOf(false) }

    suspend fun reload() {
        entries = withContext(Dispatchers.IO) { Trash.purgeExpired(); Trash.entries() }
        checked.retainAll(entries.map { it.id }.toSet())
    }
    LaunchedEffect(Unit) { reload() }

    fun run(restore: Boolean, list: List<TrashEntry>) = scope.launch {
        busy = true
        val ok = withContext(Dispatchers.IO) { list.count { if (restore) Trash.restore(ctx, it) else Trash.purge(it) } }
        ScanState.invalidate()
        reload()
        busy = false
        val msg = if (restore) "$ok ${t("elementos restaurados")}" else "$ok ${t("elementos eliminados definitivamente")}"
        Toast.makeText(ctx, msg, Toast.LENGTH_SHORT).show()
    }

    val selected = entries.filter { it.id in checked }

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        BackHeader(t("Papelera"), t("Lo que eliminas con OpenSweep se puede recuperar durante 30 días"), onBack)
        SectionCard {
            Text("${entries.size} ${t("elementos")} · ${formatBytes(entries.sumOf { it.size })}", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text(t("Pasados 30 días se eliminan definitivamente para liberar el espacio."), fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (entries.isNotEmpty()) {
                Spacer(Modifier.height(10.dp))
                OutlinedButton(onClick = { confirmEmpty = true }, enabled = !busy) { Text(t("Vaciar papelera")) }
            }
        }
        Spacer(Modifier.height(8.dp))
        Box(Modifier.weight(1f)) {
            if (entries.isEmpty()) {
                Text(t("La papelera está vacía."), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(16.dp))
            }
            LazyColumn(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                items(entries, key = { it.id }) { e ->
                    Surface(shape = RoundedCornerShape(12.dp), color = MaterialTheme.colorScheme.surfaceContainerHigh, modifier = Modifier.fillMaxWidth()) {
                        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(checked = e.id in checked, onCheckedChange = { if (it) checked.add(e.id) else checked.remove(e.id) })
                            Column(Modifier.weight(1f)) {
                                Text((if (e.isDir) "📁 " else "") + e.name, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text("${formatDate(e.deletedAt)} · ${e.daysLeft} ${t("días restantes")} · ${e.original.substringBeforeLast('/')}",
                                    fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.MiddleEllipsis, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            Text(formatBytes(e.size), color = Brand, fontWeight = FontWeight.Medium, fontSize = 14.sp)
                        }
                    }
                }
            }
        }
        if (selected.isNotEmpty()) {
            Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { run(false, selected) }, enabled = !busy, modifier = Modifier.weight(1f)) { Text(t("Eliminar")) }
                Button(onClick = { run(true, selected) }, enabled = !busy, modifier = Modifier.weight(1f)) {
                    Text("${t("Restaurar")} ${selected.size}")
                }
            }
        }
    }

    if (confirmEmpty) {
        AlertDialog(
            onDismissRequest = { confirmEmpty = false },
            title = { Text(t("¿Vaciar la papelera?")) },
            text = { Text("${entries.size} ${t("elementos")} (${formatBytes(entries.sumOf { it.size })}) ${t("se eliminarán definitivamente. No se podrán recuperar.")}") },
            confirmButton = { TextButton(onClick = { confirmEmpty = false; run(false, entries) }) { Text(t("Vaciar")) } },
            dismissButton = { TextButton(onClick = { confirmEmpty = false }) { Text(t("Cancelar")) } },
        )
    }
}
