package io.github.jromo16.opensweep.core

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.Environment
import java.io.File
import java.io.FileInputStream
import java.security.MessageDigest

data class FileRec(val path: String, val size: Long, val modified: Long) {
    val name get() = path.substringAfterLast('/')
    val folder get() = path.substringBeforeLast('/')
    val ext get() = name.substringAfterLast('.', "").lowercase()
}

enum class JunkType(val title: String, val description: String, val defaultOn: Boolean = true) {
    APP_CACHE("Caché de aplicaciones", "Archivos temporales que guardan las apps. Se vuelven a crear cuando hace falta."),
    TEMP("Archivos temporales y registros", "Archivos .tmp, .log y similares que ya no se necesitan."),
    THUMBNAILS("Miniaturas", "Vistas previas de fotos y vídeos. La galería las regenera."),
    APKS("Instaladores APK", "Archivos de instalación de apps descargados. Ya no se necesitan si la app está instalada."),
    LEFTOVERS("Restos de apps desinstaladas", "Carpetas de apps que ya no están instaladas."),
    EMPTY_FOLDERS("Carpetas vacías", "Carpetas sin ningún archivo dentro."),
    DOWNLOADS("Descargas antiguas", "Archivos de la carpeta Descargas con más de 90 días.", defaultOn = false),
}

data class JunkGroup(
    val type: JunkType,
    val files: List<FileRec> = emptyList(),
    val folders: List<String> = emptyList(),
    val extraBytes: Long = 0,   // p. ej. caché de apps (no se borra por archivos)
    val note: String? = null,
) {
    val bytes get() = files.sumOf { it.size } + extraBytes
    val count get() = files.size + folders.size
}

data class MediaStats(val name: String, val bytes: Long, val count: Int)

/**
 * Recorre el almacenamiento compartido una sola vez y guarda todos los archivos,
 * para alimentar la limpieza, los archivos grandes, los duplicados y las estadísticas.
 */
class StorageScanner(private val ctx: Context) {

    val root: File = Environment.getExternalStorageDirectory()
    val files = ArrayList<FileRec>(20_000)
    val emptyDirs = ArrayList<String>()

    private val protectedTopDirs = setOf(
        "Android", "DCIM", "Download", "Pictures", "Music", "Movies", "Documents", "Alarms", "Notifications",
        "Podcasts", "Ringtones", "Audiobooks", "Recordings",
    )

    fun scan(onProgress: (String) -> Unit) {
        files.clear(); emptyDirs.clear()
        var n = 0
        fun walk(dir: File, depth: Int): Boolean { // devuelve true si la carpeta está vacía
            val children = dir.listFiles() ?: return false
            if (children.isEmpty()) return true
            var allEmpty = true
            for (c in children) {
                if (c.isDirectory) {
                    // Android/data y Android/obb no son accesibles desde Android 11
                    if (depth == 0 && c.name == "Android") {
                        walk(File(c, "media"), depth + 2)
                        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) walk(File(c, "data"), depth + 2)
                        allEmpty = false
                        continue
                    }
                    val empty = walk(c, depth + 1)
                    if (empty && !(depth == 0 && c.name in protectedTopDirs)) emptyDirs.add(c.absolutePath)
                    else allEmpty = false
                } else {
                    allEmpty = false
                    files.add(FileRec(c.absolutePath, c.length(), c.lastModified()))
                    if (++n % 500 == 0) onProgress("$n ${t("archivos analizados…")}")
                }
            }
            return allEmpty
        }
        walk(root, 0)
        onProgress("${files.size} ${t("archivos analizados")}")
    }

    // ---------------------- Basura ----------------------

    fun junkGroups(cacheBytes: Long?): List<JunkGroup> {
        val pm = ctx.packageManager
        val installed = pm.getInstalledApplications(0).map { it.packageName }.toHashSet()
        val tempExt = setOf("tmp", "temp", "log", "chk", "dmp", "old")
        val tempNames = setOf("thumbs.db", ".ds_store", "desktop.ini")
        val rootPath = root.absolutePath

        val temp = files.filter {
            (it.ext in tempExt || it.name.lowercase() in tempNames || it.name.startsWith("~$")) &&
                !it.path.startsWith("$rootPath/Android/media")
        }
        val thumbs = files.filter { it.path.contains("/.thumbnails/") || it.path.contains("/.Thumbnails/") }
        val apks = files.filter { it.ext == "apk" || it.ext == "xapk" || it.ext == "apks" }

        val leftoverDirs = mutableListOf<String>()
        for (base in listOf("$rootPath/Android/media", "$rootPath/Android/data", "$rootPath/Android/obb")) {
            File(base).listFiles()?.forEach { d ->
                if (d.isDirectory && d.name.contains('.') && d.name !in installed) leftoverDirs.add(d.absolutePath)
            }
        }
        val leftoverFiles = files.filter { f -> leftoverDirs.any { f.path.startsWith("$it/") } }

        val downloadsDir = "$rootPath/${Environment.DIRECTORY_DOWNLOADS}/"
        val ninetyDays = System.currentTimeMillis() - 90L * 86_400_000L
        val oldDownloads = files.filter { it.path.startsWith(downloadsDir) && it.modified in 1..<ninetyDays }

        // caché propia + caché de apps medida con StorageStatsManager
        val ownCache = (ctx.cacheDir.walkTopDown() + (ctx.externalCacheDir?.walkTopDown() ?: emptySequence()))
            .filter { it.isFile }.map { FileRec(it.absolutePath, it.length(), it.lastModified()) }.toList()

        return listOf(
            JunkGroup(
                JunkType.APP_CACHE, files = ownCache, extraBytes = cacheBytes ?: 0,
                note = when {
                    cacheBytes == null -> "Concede «Acceso de uso» para medir la caché de todas las apps."
                    Build.VERSION.SDK_INT < Build.VERSION_CODES.R -> "En esta versión de Android la caché de otras apps se limpia desde Ajustes."
                    else -> null
                }
            ),
            JunkGroup(JunkType.TEMP, files = temp),
            JunkGroup(JunkType.THUMBNAILS, files = thumbs),
            JunkGroup(JunkType.APKS, files = apks),
            JunkGroup(JunkType.LEFTOVERS, files = leftoverFiles, folders = leftoverDirs),
            JunkGroup(JunkType.EMPTY_FOLDERS, folders = emptyDirs.toList()),
            JunkGroup(JunkType.DOWNLOADS, files = oldDownloads),
        )
    }

    /** Indica si un APK ya está instalado (para mostrarlo en la lista). */
    fun apkInstalled(path: String): Boolean = try {
        val info = ctx.packageManager.getPackageArchiveInfo(path, 0)
        info != null && try { ctx.packageManager.getPackageInfo(info.packageName, 0); true } catch (_: PackageManager.NameNotFoundException) { false }
    } catch (_: Exception) { false }

    // ---------------------- Estadísticas ----------------------

    fun mediaStats(): List<MediaStats> {
        val cats = linkedMapOf(
            "Imágenes" to setOf("jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "bmp", "dng", "raw"),
            "Vídeos" to setOf("mp4", "mkv", "mov", "avi", "3gp", "webm", "m4v"),
            "Audio" to setOf("mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "amr"),
            "Documentos" to setOf("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "odt", "csv", "epub"),
            "Comprimidos" to setOf("zip", "rar", "7z", "tar", "gz"),
            "Instaladores" to setOf("apk", "xapk", "apks"),
        )
        val totals = LinkedHashMap<String, Pair<Long, Int>>()
        cats.keys.forEach { totals[it] = 0L to 0 }
        totals["Otros"] = 0L to 0
        for (f in files) {
            val cat = cats.entries.firstOrNull { f.ext in it.value }?.key ?: "Otros"
            val (b, c) = totals[cat]!!
            totals[cat] = (b + f.size) to (c + 1)
        }
        return totals.map { MediaStats(it.key, it.value.first, it.value.second) }.sortedByDescending { it.bytes }
    }

    fun largeFiles(minBytes: Long): List<FileRec> =
        files.filter { it.size >= minBytes }.sortedByDescending { it.size }

    /** Duplicados: mismo tamaño → mismo hash de los primeros 64 KB → mismo MD5 completo. */
    fun duplicates(minBytes: Long, onProgress: (String) -> Unit): List<List<FileRec>> {
        val bySize = files.filter { it.size >= minBytes }.groupBy { it.size }.filterValues { it.size > 1 }
        val result = mutableListOf<List<FileRec>>()
        var i = 0
        for ((size, group) in bySize) {
            if (++i % 20 == 0) onProgress("${t("Comparando")} $i ${t("de")} ${bySize.size} ${t("grupos…")}")
            val partial = group.groupBy { hash(it.path, 64 * 1024) }.filterKeys { it != null }.values.filter { it.size > 1 }
            for (p in partial) {
                val full = if (size <= 64 * 1024) listOf(p)
                else p.groupBy { hash(it.path, Long.MAX_VALUE) }.filterKeys { it != null }.values.filter { it.size > 1 }
                full.forEach { g -> result.add(g.sortedBy { it.modified }) }
            }
        }
        return result.sortedByDescending { it.first().size * (it.size - 1) }
    }

    private fun hash(path: String, limit: Long): String? = try {
        val md = MessageDigest.getInstance("MD5")
        FileInputStream(path).use { input ->
            val buf = ByteArray(64 * 1024)
            var total = 0L
            while (total < limit) {
                val r = input.read(buf)
                if (r <= 0) break
                md.update(buf, 0, r)
                total += r
            }
        }
        md.digest().joinToString("") { "%02x".format(it) }
    } catch (_: Exception) { null }

    companion object {
        /** Borra archivos y carpetas. Devuelve (bytes liberados, elementos borrados). */
        fun delete(files: List<FileRec>, folders: List<String> = emptyList()): Pair<Long, Int> {
            var bytes = 0L; var count = 0
            for (f in files) {
                val file = File(f.path)
                if (file.exists() && file.delete()) { bytes += f.size; count++ }
            }
            for (d in folders.sortedByDescending { it.length }) {
                val dir = File(d)
                if (dir.exists() && dir.deleteRecursively()) count++
            }
            return bytes to count
        }
    }
}
