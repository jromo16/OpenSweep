package io.github.jromo16.opensweep.ui.screens

import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.AppEntry
import io.github.jromo16.opensweep.core.AppManager
import io.github.jromo16.opensweep.core.Permissions
import io.github.jromo16.opensweep.core.formatAgo
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.formatDate
import io.github.jromo16.opensweep.ui.InfoRow
import io.github.jromo16.opensweep.ui.PermissionCard
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.rememberPermissionState
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

private enum class SortBy(val title: String) { NAME("Nombre"), SIZE("Tamaño"), LAST_USED("Sin usar"), INSTALLED("Instalación") }

@Composable
fun AppsScreen(modifier: Modifier) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val perms = rememberPermissionState()
    var apps by remember { mutableStateOf<List<AppEntry>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var query by remember { mutableStateOf("") }
    var sort by remember { mutableStateOf(SortBy.SIZE) }
    var showSystem by remember { mutableStateOf(false) }
    val checked = remember { mutableStateListOf<String>() }
    var details by remember { mutableStateOf<AppEntry?>(null) }

    // Cola de desinstalación (una confirmación del sistema por app)
    val queue = remember { mutableStateListOf<AppEntry>() }
    val removed = remember { mutableStateListOf<AppEntry>() }
    var leftovers by remember { mutableStateOf<List<String>>(emptyList()) }
    var reloadKey by remember { mutableStateOf(0) }

    fun next(launch: (AppEntry) -> Unit) {
        if (queue.isNotEmpty()) launch(queue.first())
    }

    lateinit var uninstallLauncher: androidx.activity.compose.ManagedActivityResultLauncher<Intent, androidx.activity.result.ActivityResult>
    fun launchUninstall(app: AppEntry) {
        @Suppress("DEPRECATION")
        val intent = Intent(Intent.ACTION_DELETE, Uri.parse("package:${app.packageName}"))
            .putExtra(Intent.EXTRA_RETURN_RESULT, true)
        uninstallLauncher.launch(intent)
    }
    uninstallLauncher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        val app = queue.removeFirstOrNull()
        if (app != null && !AppManager.isInstalled(ctx, app.packageName)) removed.add(app)
        if (queue.isNotEmpty()) next(::launchUninstall)
        else {
            // Fin de la cola: buscar restos de las apps desinstaladas
            val found = removed.flatMap { AppManager.leftovers(it.packageName, it.label) }.distinct()
            if (removed.isNotEmpty()) {
                Toast.makeText(ctx, "${removed.size} apps desinstaladas", Toast.LENGTH_SHORT).show()
                checked.removeAll(removed.map { it.packageName })
                reloadKey++
            }
            removed.clear()
            leftovers = found
        }
    }

    LaunchedEffect(showSystem, perms.usage, reloadKey) {
        loading = true
        apps = withContext(Dispatchers.IO) { AppManager.loadApps(ctx, showSystem) }
        loading = false
    }

    val visible = remember(apps, query, sort) {
        val q = query.trim().lowercase()
        val filtered = if (q.isEmpty()) apps else apps.filter { it.label.lowercase().contains(q) || it.packageName.contains(q) }
        when (sort) {
            SortBy.NAME -> filtered.sortedBy { it.label.lowercase() }
            SortBy.SIZE -> filtered.sortedByDescending { it.totalBytes }
            SortBy.LAST_USED -> filtered.sortedBy { if (it.lastUsed == 0L) Long.MIN_VALUE else it.lastUsed }
            SortBy.INSTALLED -> filtered.sortedByDescending { it.installed }
        }
    }

    Column(modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(16.dp))
        ScreenTitle("Apps", "${apps.size} apps · ${formatBytes(apps.sumOf { it.totalBytes })}")
        if (!perms.usage) {
            PermissionCard(Icons.Filled.Lock, "Acceso de uso",
                "Concédelo para ver cuánto ocupa cada app (datos y caché) y cuándo la usaste por última vez.",
                "Conceder", perms.requestUsage)
        }
        OutlinedTextField(
            value = query, onValueChange = { query = it },
            modifier = Modifier.fillMaxWidth(),
            leadingIcon = { Icon(Icons.Filled.Search, null) },
            placeholder = { Text("Buscar app") }, singleLine = true,
        )
        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(vertical = 8.dp)) {
            items(SortBy.entries) { s -> FilterChip(selected = sort == s, onClick = { sort = s }, label = { Text(s.title) }) }
            item { FilterChip(selected = showSystem, onClick = { showSystem = !showSystem }, label = { Text("Sistema") }) }
        }
        if (loading) LinearProgressIndicator(Modifier.fillMaxWidth())

        Box(Modifier.weight(1f)) {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                items(visible, key = { it.packageName }) { app ->
                    AppRow(app, checked = app.packageName in checked,
                        onChecked = { if (it) checked.add(app.packageName) else checked.remove(app.packageName) },
                        onClick = { details = app })
                }
            }
        }
        if (checked.isNotEmpty()) {
            Button(
                onClick = {
                    queue.clear()
                    queue.addAll(apps.filter { it.packageName in checked })
                    next(::launchUninstall)
                },
                modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)
            ) {
                val size = apps.filter { it.packageName in checked }.sumOf { it.totalBytes }
                Text("Desinstalar ${checked.size} apps (${formatBytes(size)})")
            }
        }
    }

    details?.let { app ->
        AlertDialog(
            onDismissRequest = { details = null },
            title = { Text(app.label) },
            text = {
                Column {
                    InfoRow("Paquete", app.packageName)
                    InfoRow("Versión", app.versionName)
                    InfoRow("Instalada", formatDate(app.installed))
                    InfoRow("Actualizada", formatDate(app.updated))
                    InfoRow("Último uso", if (perms.usage) formatAgo(app.lastUsed) else "Requiere acceso de uso")
                    if (app.appBytes > 0) {
                        InfoRow("App", formatBytes(app.appBytes))
                        InfoRow("Datos", formatBytes(app.dataBytes - app.cacheBytes))
                        InfoRow("Caché", formatBytes(app.cacheBytes))
                    } else InfoRow("APK", formatBytes(app.apkBytes))
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    details = null
                    queue.clear(); queue.add(app); next(::launchUninstall)
                }) { Text("Desinstalar") }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = {
                        ctx.packageManager.getLaunchIntentForPackage(app.packageName)?.let { ctx.startActivity(it) }
                    }) { Text("Abrir") }
                    TextButton(onClick = { ctx.startActivity(Permissions.appDetailsIntent(app.packageName)) }) { Text("Ajustes") }
                }
            },
        )
    }

    if (leftovers.isNotEmpty()) {
        AlertDialog(
            onDismissRequest = { leftovers = emptyList() },
            title = { Text("Restos encontrados") },
            text = {
                Column {
                    Text("Estas carpetas pertenecían a las apps desinstaladas:")
                    Spacer(Modifier.height(8.dp))
                    leftovers.forEach { Text(it, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val list = leftovers
                    leftovers = emptyList()
                    scope.launch(Dispatchers.IO) { list.forEach { File(it).deleteRecursively() } }
                    Toast.makeText(ctx, "Restos eliminados", Toast.LENGTH_SHORT).show()
                }) { Text("Eliminar restos") }
            },
            dismissButton = { TextButton(onClick = { leftovers = emptyList() }) { Text("Conservar") } },
        )
    }
}

@Composable
private fun AppRow(app: AppEntry, checked: Boolean, onChecked: (Boolean) -> Unit, onClick: () -> Unit) {
    val ctx = LocalContext.current
    val icon by produceState<androidx.compose.ui.graphics.ImageBitmap?>(null, app.packageName) {
        value = withContext(Dispatchers.IO) { AppManager.icon(ctx, app.packageName)?.asImageBitmap() }
    }
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh,
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick),
    ) {
        Row(Modifier.padding(horizontal = 8.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked = checked, onCheckedChange = onChecked)
            Box(Modifier.size(40.dp).clip(RoundedCornerShape(10.dp))) {
                icon?.let { Image(it, null, Modifier.size(40.dp)) }
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(app.label, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    if (app.lastUsed > 0) "Usada: ${formatAgo(app.lastUsed)}" else app.versionName,
                    fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1
                )
            }
            Text(formatBytes(app.totalBytes), color = Brand, fontWeight = FontWeight.Medium, fontSize = 14.sp)
        }
    }
}
