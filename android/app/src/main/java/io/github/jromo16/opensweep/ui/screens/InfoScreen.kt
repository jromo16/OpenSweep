package io.github.jromo16.opensweep.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
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
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.ui.BackHeader
import io.github.jromo16.opensweep.ui.InfoRow
import io.github.jromo16.opensweep.ui.SectionCard
import kotlinx.coroutines.delay

@Composable
fun InfoScreen(modifier: Modifier, onBack: () -> Unit, onLanguageChanged: () -> Unit, onThemeChanged: () -> Unit) {
    val ctx = LocalContext.current
    var rows by remember { mutableStateOf(DeviceInfo.rows(ctx)) }
    var battery by remember { mutableStateOf(DeviceInfo.battery(ctx)) }
    var alerts by remember { mutableStateOf(Prefs.junkAlerts(ctx)) }
    var updates by remember { mutableStateOf(Prefs.updateCheck(ctx)) }
    var language by remember { mutableStateOf(Prefs.language(ctx)) }
    var theme by remember { mutableStateOf(Prefs.theme(ctx)) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(5000)
            rows = DeviceInfo.rows(ctx)
            battery = DeviceInfo.battery(ctx)
        }
    }
    Column(
        modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        BackHeader(t("Equipo y ajustes"), t("Información del teléfono"), onBack)
        SectionCard {
            Text(t("Sistema"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            rows.forEach { (k, v) -> InfoRow(t(k), v) }
        }
        battery?.let { b ->
            SectionCard {
                Text(t("Batería"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                InfoRow(t("Nivel"), "${b.level}%")
                InfoRow(t("Estado"), if (b.charging) t("Cargando") else t("Descargando"))
                InfoRow(t("Fuente"), t(b.plugged))
                InfoRow(t("Salud"), t(b.health))
                InfoRow(t("Temperatura"), "${b.temperatureC} °C")
                InfoRow(t("Voltaje"), "${b.voltage} mV")
                InfoRow(t("Tecnología"), b.technology)
            }
        }
        SectionCard {
            Text(t("Ajustes"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            SettingSwitch(t("Avisarme cuando haya mucha basura"), alerts) { alerts = it; Prefs.setJunkAlerts(ctx, it) }
            SettingSwitch(t("Buscar versiones nuevas de OpenSweep"), updates) { updates = it; Prefs.setUpdateCheck(ctx, it) }
            Text(t("Tema"), modifier = Modifier.padding(top = 8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("auto" to t("Automático"), "light" to t("Claro"), "dark" to t("Oscuro")).forEach { (code, label) ->
                    FilterChip(selected = theme == code, onClick = {
                        theme = code; Prefs.setTheme(ctx, code); onThemeChanged()
                    }, label = { Text(label) })
                }
            }
            Text(t("Idioma"), modifier = Modifier.padding(top = 8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("auto" to t("Automático"), "es" to "Español", "en" to "English").forEach { (code, label) ->
                    FilterChip(selected = language == code, onClick = {
                        language = code; Prefs.setLanguage(ctx, code); onLanguageChanged()
                    }, label = { Text(label) })
                }
            }
        }
        SectionCard {
            Text(t("Acerca de OpenSweep"), fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            val version = try { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName } catch (_: Exception) { "" }
            Text("${t("Versión")} $version · ${t("Libre y de código abierto (MIT).")}\ngithub.com/jromo16/OpenSweep",
                fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun SettingSwitch(label: String, value: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f))
        Switch(checked = value, onCheckedChange = onChange)
    }
}
