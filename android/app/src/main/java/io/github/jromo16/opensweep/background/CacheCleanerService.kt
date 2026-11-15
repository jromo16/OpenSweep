package io.github.jromo16.opensweep.background

import android.accessibilityservice.AccessibilityService
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.jromo16.opensweep.MainActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Limpieza de la caché de todas las apps (como SD Maid o CCleaner).
 * Desde Android 11 ninguna app puede borrar la caché de otra directamente, y el diálogo oficial
 * (ACTION_CLEAR_APP_CACHE) en la práctica no libera nada. Este servicio abre los ajustes de cada app
 * y pulsa «Almacenamiento» y «Borrar caché», igual que lo haría el usuario.
 * Solo actúa mientras hay una limpieza en marcha que el usuario inició; no lee ni guarda nada más.
 */
class CacheCleanerService : AccessibilityService() {

    override fun onServiceConnected() { instance = this }
    // Si el usuario desactiva el servicio a mitad, la limpieza se detiene
    override fun onDestroy() { CacheCleaner.cancel(); if (instance === this) instance = null; super.onDestroy() }
    override fun onUnbind(intent: Intent?): Boolean { CacheCleaner.cancel(); if (instance === this) instance = null; return super.onUnbind(intent) }
    override fun onAccessibilityEvent(event: AccessibilityEvent?) { }   // se consulta la pantalla bajo demanda
    override fun onInterrupt() { CacheCleaner.cancel() }

    companion object {
        @Volatile var instance: CacheCleanerService? = null
            private set

        fun isEnabled(ctx: Context): Boolean {
            if (instance != null) return true
            val me = ComponentName(ctx, CacheCleanerService::class.java).flattenToString()
            val enabled = Settings.Secure.getString(ctx.contentResolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES) ?: return false
            return enabled.split(':').any { it.equals(me, ignoreCase = true) }
        }

        fun settingsIntent(): Intent = Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}

/** Estado y lógica de la limpieza de caché (observable desde Compose). */
object CacheCleaner {
    var running by mutableStateOf(false); private set
    var done by mutableStateOf(0); private set
    var total by mutableStateOf(0); private set
    var current by mutableStateOf(""); private set
    var cleaned by mutableStateOf(0); private set
    var lastResult by mutableStateOf<String?>(null)

    private var job: Job? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    private const val SETTINGS = "com.android.settings"

    /** Textos de los botones en el idioma del teléfono, leídos de la propia app de Ajustes, más alternativas. */
    private fun labels(ctx: Context, names: List<String>, fallback: List<String>): List<String> {
        val out = mutableListOf<String>()
        try {
            val res = ctx.packageManager.getResourcesForApplication(SETTINGS)
            for (n in names) {
                @Suppress("DiscouragedApi")
                val id = res.getIdentifier(n, "string", SETTINGS)
                if (id != 0) out += res.getString(id)
            }
        } catch (_: Exception) { }
        return (out + fallback).map { it.trim() }.filter { it.isNotEmpty() }.distinct()
    }

    fun start(ctx: Context, packages: List<Pair<String, String>>, onFinished: (Int) -> Unit) {
        val service = CacheCleanerService.instance ?: return
        if (running || packages.isEmpty()) return
        val storage = labels(ctx, listOf("storage_settings_for_app", "storage_label", "storage_settings", "storage_and_cache"),
            listOf("Storage & cache", "Storage and cache", "Storage", "Almacenamiento y caché", "Almacenamiento y memoria caché", "Almacenamiento"))
        val clear = labels(ctx, listOf("clear_cache_btn_text"), listOf("Clear cache", "Borrar caché", "Borrar memoria caché", "Vaciar caché", "Limpiar caché"))
        // Textos de los botones que BORRAN LOS DATOS de la app: si aparecen junto al botón, no se pulsa nada
        danger = labels(ctx, listOf("clear_user_data_text", "clear_storage_btn_text", "clear_data_dlg_title", "manage_space_text"),
            listOf("Clear storage", "Clear data", "Manage space", "Borrar almacenamiento", "Borrar datos", "Eliminar datos",
                "Administrar espacio", "Gestionar espacio", "Borrar todos los datos", "Liberar espacio"))
            .map { it.lowercase() }
        running = true; done = 0; total = packages.size; cleaned = 0; lastResult = null
        job = scope.launch {
            var failsInARow = 0
            try {
                for ((pkg, label) in packages) {
                    if (!running) break
                    current = label
                    val ok = cleanOne(service, pkg, label, storage, clear)
                    if (ok) { cleaned++; failsInARow = 0 } else failsInARow++
                    done++
                    // Si falla varias veces seguidas (pantalla de Ajustes distinta, el usuario salió…), no insistir
                    if (failsInARow >= 4) break
                }
            } finally {
                running = false; current = ""
                returnToApp(service)
                val n = cleaned
                launch(Dispatchers.Main) { onFinished(n) }
            }
        }
    }

    fun cancel() { running = false }

    @Volatile private var danger: List<String> = emptyList()

    /** ¿El nodo o algo dentro de él muestra un texto de «Borrar almacenamiento / datos»? */
    private fun mentionsDanger(node: AccessibilityNodeInfo): Boolean {
        val text = (node.text?.toString().orEmpty() + " " + node.contentDescription?.toString().orEmpty()).lowercase()
        if (danger.any { it.isNotEmpty() && text.contains(it) }) return true
        for (i in 0 until node.childCount) node.getChild(i)?.let { if (mentionsDanger(it)) return true }
        return false
    }

    private suspend fun cleanOne(s: AccessibilityService, pkg: String, appLabel: String, storage: List<String>, clear: List<String>): Boolean {
        s.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$pkg"))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK or Intent.FLAG_ACTIVITY_NO_HISTORY))
        delay(300)
        // 0. Esperar a que se muestre la ficha de ESTA app (no la de la anterior, que puede seguir en pantalla)
        waitFor(s, listOf(appLabel), scroll = false, timeoutMs = 4000) { true } ?: return fail(pkg, "no se abrió su ficha")
        // 1. «Almacenamiento» en la ficha de la app. Solo vale una fila que se pueda pulsar
        //    (así no se confunde con el título «Almacenamiento» de otra pantalla).
        val entry = waitFor(s, storage, scroll = true) { clickableAncestor(it) != null } ?: return fail(pkg, "sin «Almacenamiento»")
        if (clickableAncestor(entry)?.performAction(AccessibilityNodeInfo.ACTION_CLICK) != true) return fail(pkg, "no se pudo pulsar «Almacenamiento»")
        delay(300)
        // 2. «Borrar caché»: el texto suele ir debajo de un botón aparte; se pulsa ese botón
        val label = waitFor(s, clear, scroll = false) { true } ?: return fail(pkg, "sin «Borrar caché»")
        // Solo dentro de la app de Ajustes (no en otra ventana que muestre el mismo texto)
        val windowPkg = s.rootInActiveWindow?.packageName?.toString().orEmpty()
        if (!windowPkg.contains("settings", ignoreCase = true)) return fail(pkg, "la ventana no es de Ajustes ($windowPkg)")
        val button = buttonFor(label) ?: return fail(pkg, "no se encontró el botón")
        if (mentionsDanger(button)) return fail(pkg, "el botón parece de borrar datos: no se pulsa")
        if (!button.isEnabled) return true            // ya no tenía caché
        if (!button.performAction(AccessibilityNodeInfo.ACTION_CLICK)) return fail(pkg, "no se pudo pulsar «Borrar caché»")
        delay(500)
        return true
    }


    private fun fail(pkg: String, why: String): Boolean { android.util.Log.d("OpenSweepCache", "$pkg: $why"); return false }

    /**
     * Botón asociado a una etiqueta: el propio nodo si es un botón, o el único botón dentro de su contenedor
     * (subiendo como mucho dos niveles). Si en el contenedor hay más de un botón se descarta, para no pulsar
     * nunca «Borrar almacenamiento», que está justo al lado y borraría los datos de la app.
     */
    private fun buttonFor(label: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (isButton(label)) return label
        var container = label.parent
        repeat(2) {
            val c = container ?: return null
            // Si en este bloque aparece «Borrar almacenamiento/datos», ya no es solo el botón de la caché
            if (mentionsDanger(c)) return null
            val buttons = mutableListOf<AccessibilityNodeInfo>()
            collectButtons(c, buttons)
            if (buttons.size == 1) return buttons[0]
            if (buttons.size > 1) return null
            container = c.parent
        }
        return null
    }

    /** Cuenta como botón aunque esté desactivado o no sea pulsable: así nunca se confunde «un solo botón». */
    private fun isButton(n: AccessibilityNodeInfo) = n.className?.toString()?.contains("Button") == true

    private fun collectButtons(node: AccessibilityNodeInfo, out: MutableList<AccessibilityNodeInfo>) {
        if (isButton(node)) out += node
        for (i in 0 until node.childCount) node.getChild(i)?.let { collectButtons(it, out) }
    }

    /** Busca un nodo cuyo texto coincida con alguna etiqueta (y cumpla [accept]), esperando a que cargue la pantalla. */
    private suspend fun waitFor(
        s: AccessibilityService, labels: List<String>, scroll: Boolean, timeoutMs: Long = 5000,
        accept: (AccessibilityNodeInfo) -> Boolean,
    ): AccessibilityNodeInfo? {
        val end = System.currentTimeMillis() + timeoutMs
        var scrolls = 0
        while (System.currentTimeMillis() < end && running) {
            val root = s.rootInActiveWindow
            if (root != null && root.refresh()) {
                find(root, labels, accept)?.let { return it }
                if (scroll && scrolls < 4 && System.currentTimeMillis() > end - timeoutMs + 1200) {
                    scrollable(root)?.performAction(AccessibilityNodeInfo.ACTION_SCROLL_FORWARD)
                    scrolls++
                }
            }
            delay(250)
        }
        return null
    }

    private fun find(root: AccessibilityNodeInfo, labels: List<String>, accept: (AccessibilityNodeInfo) -> Boolean): AccessibilityNodeInfo? {
        // Búsqueda directa en la app de Ajustes (no usa la copia en caché del árbol, que puede estar incompleta)
        for (l in labels) {
            root.findAccessibilityNodeInfosByText(l)
                ?.firstOrNull { it.text?.toString()?.trim().equals(l, ignoreCase = true) && accept(it) }
                ?.let { return it }
        }
        // Las pantallas hechas con Compose no responden a la búsqueda directa: se recorre el árbol
        return walkFind(root, labels, accept)
    }

    private fun walkFind(node: AccessibilityNodeInfo, labels: List<String>, accept: (AccessibilityNodeInfo) -> Boolean): AccessibilityNodeInfo? {
        val text = node.text?.toString()?.trim()
        if (text != null && labels.any { text.equals(it, ignoreCase = true) } && accept(node)) return node
        for (i in 0 until node.childCount) node.getChild(i)?.let { c -> walkFind(c, labels, accept)?.let { return it } }
        return null
    }

    private fun scrollable(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (node.isScrollable) return node
        for (i in 0 until node.childCount) node.getChild(i)?.let { c -> scrollable(c)?.let { return it } }
        return null
    }

    /** El nodo o su primer antecesor que se puede pulsar (el texto suele estar dentro de la fila). */
    private fun clickableAncestor(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        var n: AccessibilityNodeInfo? = node
        while (n != null) {
            if (n.isClickable && n.isEnabled) return n
            n = n.parent
        }
        return null
    }

    private fun returnToApp(s: AccessibilityService) {
        try {
            s.startActivity(Intent(s, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP))
        } catch (_: Exception) { }
    }
}
