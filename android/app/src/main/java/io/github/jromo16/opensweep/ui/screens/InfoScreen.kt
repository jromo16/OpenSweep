package io.github.jromo16.opensweep.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.core.DeviceInfo
import io.github.jromo16.opensweep.ui.InfoRow
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.SectionCard
import kotlinx.coroutines.delay

@Composable
fun InfoScreen(modifier: Modifier) {
    val ctx = LocalContext.current
    var rows by remember { mutableStateOf(DeviceInfo.rows(ctx)) }
    var battery by remember { mutableStateOf(DeviceInfo.battery(ctx)) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(5000)
            rows = DeviceInfo.rows(ctx)
            battery = DeviceInfo.battery(ctx)
        }
    }
    Column(
        modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        ScreenTitle("Equipo", "Información del teléfono")
        SectionCard {
            Text("Sistema", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            rows.forEach { (k, v) -> InfoRow(k, v) }
        }
        battery?.let { b ->
            SectionCard {
                Text("Batería", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                InfoRow("Nivel", "${b.level}%")
                InfoRow("Estado", if (b.charging) "Cargando" else "Descargando")
                InfoRow("Fuente", b.plugged)
                InfoRow("Salud", b.health)
                InfoRow("Temperatura", "${b.temperatureC} °C")
                InfoRow("Voltaje", "${b.voltage} mV")
                InfoRow("Tecnología", b.technology)
            }
        }
        SectionCard {
            Text("Acerca de OpenSweep", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text("Limpiador y gestor de apps libre y de código abierto (MIT).\ngithub.com/jromo16/OpenSweep", fontSize = 14.sp)
        }
    }
}
