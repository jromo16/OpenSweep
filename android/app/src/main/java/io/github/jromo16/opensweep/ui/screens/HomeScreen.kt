package io.github.jromo16.opensweep.ui.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.DeviceInfo
import io.github.jromo16.opensweep.core.Prefs
import io.github.jromo16.opensweep.core.UpdateInfo
import io.github.jromo16.opensweep.core.Updater
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.core.JunkType
import io.github.jromo16.opensweep.core.Trash
import io.github.jromo16.opensweep.ui.LabeledBar
import io.github.jromo16.opensweep.ui.NavHints
import io.github.jromo16.opensweep.ui.ScanState
import io.github.jromo16.opensweep.ui.Suggestion
import io.github.jromo16.opensweep.ui.SuggestionTarget
import io.github.jromo16.opensweep.ui.Suggestions
import io.github.jromo16.opensweep.ui.PermissionCard
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.UsageRing
import io.github.jromo16.opensweep.ui.rememberPermissionState
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

/** Se comprueba una sola vez por sesión. */
private object UpdateState { var checked = false; var info: UpdateInfo? = null }

@Composable
fun HomeScreen(
    modifier: Modifier, onClean: () -> Unit, onApps: () -> Unit, onPhotos: () -> Unit, onDevice: () -> Unit,
    onFiles: () -> Unit, onTrash: () -> Unit,
) {
    val ctx = LocalContext.current
    val perms = rememberPermissionState()
    var storage by remember { mutableStateOf(DeviceInfo.storage()) }
    var memory by remember { mutableStateOf(DeviceInfo.memory(ctx)) }
    var battery by remember { mutableStateOf(DeviceInfo.battery(ctx)) }
    var update by remember { mutableStateOf(UpdateState.info) }
    var suggestions by remember { mutableStateOf<List<Suggestion>>(emptyList()) }

    // Sugerencias: se analiza el almacenamiento en segundo plano (una vez por sesión) y se calculan
    LaunchedEffect(perms.storage, perms.usage) {
        withContext(Dispatchers.IO) { Trash.purgeExpired() }
        if (perms.storage && ScanState.scanner == null && !ScanState.scanning) ScanState.scan(ctx)
        suggestions = withContext(Dispatchers.IO) { Suggestions.compute(ctx) }
    }

    LaunchedEffect(Unit) {
        if (!UpdateState.checked && Prefs.updateCheck(ctx)) {
            UpdateState.checked = true
            val version = try { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0" } catch (_: Exception) { "0" }
            UpdateState.info = withContext(Dispatchers.IO) { Updater.check(version) }
            update = UpdateState.info
        }
    }
    // Actualiza RAM y batería cada pocos segundos
    LaunchedEffect(Unit) {
        while (true) {
            delay(3000)
            storage = DeviceInfo.storage()
            memory = DeviceInfo.memory(ctx)
            battery = DeviceInfo.battery(ctx)
        }
    }

    Column(
        modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        ScreenTitle("OpenSweep", t("Limpia y optimiza tu teléfono"))

        update?.let { u ->
            PermissionCard(Icons.Filled.Star, "${t("Nueva versión disponible")}: ${u.version}",
                t("Descarga la última versión de OpenSweep desde GitHub."), t("Descargar"), onClick = {
                ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(u.apkUrl ?: u.pageUrl)))
            })
        }

        if (!perms.storage) {
            PermissionCard(
                Icons.Filled.Lock, t("Permiso de almacenamiento"),
                t("OpenSweep necesita acceso a los archivos para encontrar basura, archivos grandes y duplicados. Nada sale de tu teléfono."),
                t("Conceder acceso"), perms.requestStorage
            )
        }

        SectionCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                UsageRing(storage.percent, 130.dp) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("${(storage.percent * 100).toInt()}%", fontSize = 28.sp, fontWeight = FontWeight.Bold)
                        Text(t("usado"), color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
                    }
                }
                Spacer(Modifier.width(20.dp))
                Column {
                    Text(t("Almacenamiento"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                    Text("${formatBytes(storage.free)} ${t("libres")}", color = Brand)
                    Text("${t("de")} ${formatBytes(storage.total)}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.height(10.dp))
                    Button(onClick = onClean) { Text(t("Analizar y limpiar")) }
                }
            }
        }

        if (suggestions.isNotEmpty()) {
            Text(t("Sugerencias"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp, modifier = Modifier.padding(top = 4.dp))
            suggestions.forEach { s ->
                SectionCard {
                    Text(s.title, fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                    Text(s.detail, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.height(8.dp))
                    OutlinedButton(onClick = {
                        when (s.target) {
                            SuggestionTarget.APPS -> { NavHints.appsSortUnused = true; onApps() }
                            SuggestionTarget.FILES -> { NavHints.filesTab = 1; NavHints.filesMinMb = 100; onFiles() }
                            SuggestionTarget.CLEANER -> { NavHints.cleanerSelect = JunkType.DOWNLOADS; onClean() }
                            SuggestionTarget.TRASH -> onTrash()
                        }
                    }) { Text(s.button) }
                }
            }
        }

        SectionCard {
            Text(t("Estado del equipo"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Spacer(Modifier.height(4.dp))
            LabeledBar(t("Memoria RAM"), "${formatBytes(memory.used)} / ${formatBytes(memory.total)}", memory.percent)
            battery?.let { b ->
                LabeledBar(
                    t("Batería") + if (b.charging) " (${t("cargando")})" else "",
                    "${b.level}% · ${b.temperatureC} °C",
                    b.level / 100f,
                    color = if (b.level <= 15) MaterialTheme.colorScheme.error else Brand
                )
            }
            Spacer(Modifier.height(6.dp))
            OutlinedButton(onClick = onDevice, modifier = Modifier.fillMaxWidth()) { Text(t("Información del equipo y ajustes")) }
        }

        SectionCard {
            Text(t("Fotos"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text(t("Encuentra fotos parecidas, borrosas y capturas antiguas, y conserva solo la mejor."),
                color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
            Spacer(Modifier.height(10.dp))
            OutlinedButton(onClick = onPhotos, modifier = Modifier.fillMaxWidth()) { Text(t("Revisar fotos")) }
        }

        SectionCard {
            Text(t("Apps"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text(t("Desinstala apps que no usas y elimina sus restos. Ordénalas por tamaño o por último uso."),
                color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
            Spacer(Modifier.height(10.dp))
            OutlinedButton(onClick = onApps, modifier = Modifier.fillMaxWidth()) { Text(t("Administrar apps")) }
        }
    }
}
