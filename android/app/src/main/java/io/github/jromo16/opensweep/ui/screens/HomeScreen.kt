package io.github.jromo16.opensweep.ui.screens

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
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.ui.LabeledBar
import io.github.jromo16.opensweep.ui.PermissionCard
import io.github.jromo16.opensweep.ui.ScreenTitle
import io.github.jromo16.opensweep.ui.SectionCard
import io.github.jromo16.opensweep.ui.UsageRing
import io.github.jromo16.opensweep.ui.rememberPermissionState
import io.github.jromo16.opensweep.ui.theme.Brand
import kotlinx.coroutines.delay

@Composable
fun HomeScreen(modifier: Modifier, onClean: () -> Unit, onApps: () -> Unit) {
    val ctx = LocalContext.current
    val perms = rememberPermissionState()
    var storage by remember { mutableStateOf(DeviceInfo.storage()) }
    var memory by remember { mutableStateOf(DeviceInfo.memory(ctx)) }
    var battery by remember { mutableStateOf(DeviceInfo.battery(ctx)) }

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
        ScreenTitle("OpenSweep", "Limpia y optimiza tu teléfono")

        if (!perms.storage) {
            PermissionCard(
                Icons.Filled.Lock, "Permiso de almacenamiento",
                "OpenSweep necesita acceso a los archivos para encontrar basura, archivos grandes y duplicados. Nada sale de tu teléfono.",
                "Conceder acceso", perms.requestStorage
            )
        }

        SectionCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                UsageRing(storage.percent, 130.dp) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("${(storage.percent * 100).toInt()}%", fontSize = 28.sp, fontWeight = FontWeight.Bold)
                        Text("usado", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
                    }
                }
                Spacer(Modifier.width(20.dp))
                Column {
                    Text("Almacenamiento", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
                    Text("${formatBytes(storage.free)} libres", color = Brand)
                    Text("de ${formatBytes(storage.total)}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.height(10.dp))
                    Button(onClick = onClean) { Text("Analizar y limpiar") }
                }
            }
        }

        SectionCard {
            Text("Estado del equipo", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Spacer(Modifier.height(4.dp))
            LabeledBar("Memoria RAM", "${formatBytes(memory.used)} / ${formatBytes(memory.total)}", memory.percent)
            battery?.let { b ->
                LabeledBar(
                    "Batería" + if (b.charging) " (cargando)" else "",
                    "${b.level}% · ${b.temperatureC} °C",
                    b.level / 100f,
                    color = if (b.level <= 15) MaterialTheme.colorScheme.error else Brand
                )
            }
        }

        SectionCard {
            Text("Apps", fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
            Text(
                "Desinstala apps que no usas y elimina sus restos. Ordénalas por tamaño o por último uso.",
                color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp
            )
            Spacer(Modifier.height(10.dp))
            OutlinedButton(onClick = onApps, modifier = Modifier.fillMaxWidth()) { Text("Administrar apps") }
        }
    }
}
