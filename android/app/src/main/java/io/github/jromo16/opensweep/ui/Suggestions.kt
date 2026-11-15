package io.github.jromo16.opensweep.ui

import android.content.Context
import io.github.jromo16.opensweep.core.AppManager
import io.github.jromo16.opensweep.core.JunkType
import io.github.jromo16.opensweep.core.Permissions
import io.github.jromo16.opensweep.core.Trash
import io.github.jromo16.opensweep.core.formatBytes
import io.github.jromo16.opensweep.core.t
import android.os.Environment

/**
 * Indicaciones para abrir otra pantalla ya preparada (p. ej. Apps ordenadas por «Sin usar»).
 * La pantalla destino las lee una vez al abrirse y las borra.
 */
object NavHints {
    var appsSortUnused = false
    var filesTab: Int? = null
    var filesMinMb: Long? = null
    var cleanerSelect: JunkType? = null
}

enum class SuggestionTarget { APPS, FILES, CLEANER, TRASH }

data class Suggestion(val title: String, val detail: String, val button: String, val target: SuggestionTarget, val bytes: Long)

/** Sugerencias de la pantalla de Inicio, al estilo de «Files by Google». */
object Suggestions {
    private const val DAY = 86_400_000L
    private const val MB = 1024L * 1024L

    fun compute(ctx: Context): List<Suggestion> {
        val list = mutableListOf<Suggestion>()
        val now = System.currentTimeMillis()

        // Apps sin usar en 90 días (instaladas hace más de 30, para no marcar las recién instaladas)
        if (Permissions.hasUsageAccess(ctx)) {
            val unused = AppManager.loadApps(ctx, includeSystem = false)
                .filter { it.installed in 1..<(now - 30 * DAY) && (it.lastUsed == 0L || it.lastUsed < now - 90 * DAY) }
            if (unused.size >= 2) {
                val bytes = unused.sumOf { it.totalBytes }
                list.add(Suggestion(
                    "${unused.size} ${t("apps que no usas")}",
                    "${t("No las abres desde hace más de 3 meses y ocupan")} ${formatBytes(bytes)}.",
                    t("Revisar apps"), SuggestionTarget.APPS, bytes))
            }
        }

        val scanner = ScanState.scanner
        if (scanner != null) {
            // Vídeos de más de 100 MB
            val videoExt = setOf("mp4", "mkv", "mov", "avi", "3gp", "webm", "m4v")
            val big = scanner.files.filter { it.ext in videoExt && it.size >= 100 * MB }
            if (big.isNotEmpty()) {
                val bytes = big.sumOf { it.size }
                list.add(Suggestion(
                    "${big.size} ${if (big.size == 1) t("vídeo grande") else t("vídeos grandes")}",
                    "${t("Vídeos de más de 100 MB que ocupan")} ${formatBytes(bytes)}.",
                    if (big.size == 1) t("Ver vídeo") else t("Ver vídeos"), SuggestionTarget.FILES, bytes))
            }
            // Descargas de hace más de 90 días
            val dl = "${Environment.getExternalStorageDirectory().absolutePath}/${Environment.DIRECTORY_DOWNLOADS}/"
            val old = scanner.files.filter { it.path.startsWith(dl) && it.modified in 1..<(now - 90 * DAY) }
            val oldBytes = old.sumOf { it.size }
            if (old.size >= 3 && oldBytes >= 20 * MB) {
                list.add(Suggestion(
                    "${old.size} ${t("descargas antiguas")}",
                    "${t("Archivos descargados hace más de 3 meses que ocupan")} ${formatBytes(oldBytes)}.",
                    t("Revisar descargas"), SuggestionTarget.CLEANER, oldBytes))
            }
        }

        // Papelera
        val trash = Trash.entries()
        if (trash.isNotEmpty()) {
            val bytes = trash.sumOf { it.size }
            list.add(Suggestion(
                "${t("Papelera")}: ${formatBytes(bytes)}",
                "${trash.size} ${t("elementos que puedes restaurar o eliminar definitivamente.")}",
                t("Abrir papelera"), SuggestionTarget.TRASH, 0))
        }
        return list.sortedByDescending { it.bytes }
    }
}
