package io.github.jromo16.opensweep.ui

import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import io.github.jromo16.opensweep.core.Permissions

class PermissionState(
    val storage: Boolean,
    val usage: Boolean,
    val requestStorage: () -> Unit,
    val requestUsage: () -> Unit,
)

/** Estado de los permisos especiales; se vuelve a comprobar al volver de Ajustes. */
@Composable
fun rememberPermissionState(): PermissionState {
    val ctx = LocalContext.current
    var tick by remember { mutableIntStateOf(0) }
    val settingsLauncher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { tick++ }
    val legacyLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { tick++ }
    val storage = remember(tick) { Permissions.hasStorage(ctx) }
    val usage = remember(tick) { Permissions.hasUsageAccess(ctx) }
    return PermissionState(
        storage = storage,
        usage = usage,
        requestStorage = {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) Permissions.storageSettingsIntent(ctx)?.let { settingsLauncher.launch(it) }
            else legacyLauncher.launch(Permissions.legacyStoragePermissions)
        },
        requestUsage = { settingsLauncher.launch(Permissions.usageSettingsIntent(ctx)) },
    )
}
