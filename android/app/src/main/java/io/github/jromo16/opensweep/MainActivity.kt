package io.github.jromo16.opensweep

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import io.github.jromo16.opensweep.background.JunkWorker
import io.github.jromo16.opensweep.core.I18n
import io.github.jromo16.opensweep.core.t
import io.github.jromo16.opensweep.ui.screens.AppsScreen
import io.github.jromo16.opensweep.ui.screens.CleanerScreen
import io.github.jromo16.opensweep.ui.screens.FilesScreen
import io.github.jromo16.opensweep.ui.screens.HomeScreen
import io.github.jromo16.opensweep.ui.screens.InfoScreen
import io.github.jromo16.opensweep.ui.screens.MessengersScreen
import io.github.jromo16.opensweep.ui.screens.PhotosScreen
import io.github.jromo16.opensweep.ui.theme.OpenSweepTheme

private data class Tab(val title: String, val icon: ImageVector)

class MainActivity : ComponentActivity() {

    /** Pestaña pedida desde una notificación o el widget. */
    private var requestedTab by mutableIntStateOf(-1)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        I18n.init(this)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        JunkWorker.schedule(this)
        if (Build.VERSION.SDK_INT >= 33 && savedInstanceState == null) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 10)
        }
        requestedTab = intent.getIntExtra(EXTRA_TAB, -1)

        val tabs = listOf(
            Tab(t("Inicio"), Icons.Filled.Home),
            Tab(t("Limpiar"), Icons.Filled.Delete),
            Tab(t("Fotos"), Icons.Filled.Face),
            Tab(t("Apps"), Icons.AutoMirrored.Filled.List),
            Tab(t("Archivos"), Icons.Filled.Search),
        )
        setContent {
            OpenSweepTheme {
                var selected by rememberSaveable { mutableIntStateOf(0) }
                var sub by rememberSaveable { mutableStateOf<String?>(null) }
                if (requestedTab >= 0) { selected = requestedTab; sub = null; requestedTab = -1 }
                BackHandler(enabled = sub != null) { sub = null }

                Scaffold(
                    containerColor = MaterialTheme.colorScheme.background,
                    bottomBar = {
                        NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainer) {
                            tabs.forEachIndexed { i, tab ->
                                NavigationBarItem(
                                    selected = selected == i && sub == null,
                                    onClick = { selected = i; sub = null },
                                    icon = { Icon(tab.icon, contentDescription = tab.title) },
                                    label = { Text(tab.title) },
                                )
                            }
                        }
                    }
                ) { padding ->
                    val m = Modifier.padding(padding)
                    when (sub) {
                        "device" -> InfoScreen(m, onBack = { sub = null }, onLanguageChanged = { recreate() })
                        "messengers" -> MessengersScreen(m, onBack = { sub = null })
                        else -> when (selected) {
                            0 -> HomeScreen(m, onClean = { selected = 1 }, onApps = { selected = 3 }, onPhotos = { selected = 2 },
                                onDevice = { sub = "device" })
                            1 -> CleanerScreen(m, onMessengers = { sub = "messengers" })
                            2 -> PhotosScreen(m)
                            3 -> AppsScreen(m)
                            else -> FilesScreen(m)
                        }
                    }
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        val tab = intent.getIntExtra(EXTRA_TAB, -1)
        if (tab >= 0) requestedTab = tab
    }

    companion object {
        const val EXTRA_TAB = "tab"
    }
}
