package io.github.jromo16.opensweep

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import io.github.jromo16.opensweep.ui.screens.AppsScreen
import io.github.jromo16.opensweep.ui.screens.CleanerScreen
import io.github.jromo16.opensweep.ui.screens.FilesScreen
import io.github.jromo16.opensweep.ui.screens.HomeScreen
import io.github.jromo16.opensweep.ui.screens.InfoScreen
import io.github.jromo16.opensweep.ui.theme.OpenSweepTheme

private data class Tab(val title: String, val icon: ImageVector)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        val tabs = listOf(
            Tab("Inicio", Icons.Filled.Home),
            Tab("Limpiar", Icons.Filled.Delete),
            Tab("Apps", Icons.AutoMirrored.Filled.List),
            Tab("Archivos", Icons.Filled.Search),
            Tab("Equipo", Icons.Filled.Info),
        )
        setContent {
            OpenSweepTheme {
                var selected by rememberSaveable { mutableIntStateOf(0) }
                Scaffold(
                    containerColor = MaterialTheme.colorScheme.background,
                    bottomBar = {
                        NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainer) {
                            tabs.forEachIndexed { i, t ->
                                NavigationBarItem(
                                    selected = selected == i,
                                    onClick = { selected = i },
                                    icon = { Icon(t.icon, contentDescription = t.title) },
                                    label = { Text(t.title) },
                                )
                            }
                        }
                    }
                ) { padding ->
                    val m = Modifier.padding(padding)
                    when (selected) {
                        0 -> HomeScreen(m, onClean = { selected = 1 }, onApps = { selected = 2 })
                        1 -> CleanerScreen(m)
                        2 -> AppsScreen(m)
                        3 -> FilesScreen(m)
                        else -> InfoScreen(m)
                    }
                }
            }
        }
    }
}
