package io.github.jromo16.opensweep.core

import android.content.Context
import android.media.MediaScannerConnection
import android.os.Environment
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

data class TrashEntry(
    val id: String,
    val original: String,
    val size: Long,
    val deletedAt: Long,
    val isDir: Boolean,
) {
    val name get() = original.substringAfterLast('/')
    val daysLeft get() = (Trash.KEEP_DAYS - (System.currentTimeMillis() - deletedAt) / 86_400_000L).coerceAtLeast(0)
}

/**
 * Papelera propia de OpenSweep: lo que se elimina de tu contenido (fotos, vídeos, descargas,
 * archivos de WhatsApp, restos de apps…) se mueve aquí y se puede restaurar durante 30 días.
 * Está en /sdcard/.OpenSweepTrash con un .nomedia para que la galería no muestre su contenido.
 */
object Trash {
    const val KEEP_DAYS = 30L
    private val lock = Any()

    val root: File get() = File(Environment.getExternalStorageDirectory(), ".OpenSweepTrash")
    private val filesDir get() = File(root, "files")
    private val indexFile get() = File(root, "index.json")

    private fun ensure() {
        filesDir.mkdirs()
        File(root, ".nomedia").let { if (!it.exists()) it.createNewFile() }
    }

    private fun read(): MutableList<TrashEntry> {
        if (!indexFile.exists()) return mutableListOf()
        return try {
            val arr = JSONArray(indexFile.readText())
            MutableList(arr.length()) { i ->
                val o = arr.getJSONObject(i)
                TrashEntry(o.getString("id"), o.getString("original"), o.getLong("size"), o.getLong("deletedAt"), o.optBoolean("isDir"))
            }
        } catch (_: Exception) {
            // Índice dañado: se guarda aparte en vez de perderlo (la siguiente escritura lo sobrescribiría)
            try { indexFile.renameTo(File(root, "index.json.dañado-${System.currentTimeMillis()}")) } catch (_: Exception) { }
            mutableListOf()
        }
    }

    private fun write(list: List<TrashEntry>) {
        ensure()
        val arr = JSONArray()
        list.forEach {
            arr.put(JSONObject().put("id", it.id).put("original", it.original).put("size", it.size)
                .put("deletedAt", it.deletedAt).put("isDir", it.isDir))
        }
        // Escritura atómica: primero a un temporal y luego se renombra
        val tmp = File(root, "index.json.tmp")
        tmp.writeText(arr.toString())
        if (!tmp.renameTo(indexFile)) { indexFile.delete(); tmp.renameTo(indexFile) }
    }

    fun entries(): List<TrashEntry> = synchronized(lock) { read().sortedByDescending { it.deletedAt } }

    fun totalBytes(): Long = entries().sumOf { it.size }

    /** Mueve un archivo o carpeta a la papelera. Devuelve true si se movió. */
    fun move(ctx: Context, path: String): Boolean = synchronized(lock) {
        val src = File(path)
        if (!src.exists()) return false
        // Nunca mover la propia papelera ni carpetas raíz del almacenamiento
        if (src.absolutePath.startsWith(root.absolutePath) || src.parentFile == null) return false
        ensure()
        val size = if (src.isDirectory) src.walkTopDown().filter { it.isFile }.sumOf { it.length() } else src.length()
        val id = UUID.randomUUID().toString()
        val dest = File(filesDir, id)
        val moved = src.renameTo(dest) || copyAndDelete(src, dest)
        if (!moved) return false
        val list = read()
        list.add(TrashEntry(id, src.absolutePath, size, System.currentTimeMillis(), dest.isDirectory))
        write(list)
        notifyGallery(ctx, src.absolutePath)
        true
    }

    /** Si renombrar no es posible (otra unidad, p. ej. tarjeta SD), se copia y luego se borra el original. */
    private fun copyAndDelete(src: File, dest: File): Boolean = try {
        if (src.isDirectory) {
            src.copyRecursively(dest, overwrite = false)
            // La copia está completa: aunque el original no se borre del todo, se registra para poder recuperarlo
            src.deleteRecursively()
            true
        } else {
            src.copyTo(dest, overwrite = false)
            // Si el original no se puede borrar, se quita la copia: no debe quedar nada huérfano en la papelera
            if (src.delete()) true else { dest.delete(); false }
        }
    } catch (_: Exception) {
        dest.deleteRecursively(); false
    }

    /** Restaura a su ubicación original (si ya existe algo con ese nombre, añade «(restaurado)»). */
    fun restore(ctx: Context, e: TrashEntry): Boolean = synchronized(lock) {
        val stored = File(filesDir, e.id)
        if (!stored.exists()) { write(read().filter { it.id != e.id }); return false }
        var target = File(e.original)
        if (target.exists()) {
            // Las carpetas conservan su nombre completo («com.foo.bar», no «com.foo»)
            val base = if (e.isDir) target.name else target.nameWithoutExtension
            val ext = if (e.isDir) "" else target.extension
            var n = 1
            do {
                val suffix = if (n == 1) " (restaurado)" else " (restaurado $n)"
                target = File(target.parentFile, base + suffix + if (ext.isNotEmpty() && !e.isDir) ".$ext" else "")
                n++
            } while (target.exists())
        }
        target.parentFile?.mkdirs()
        val ok = stored.renameTo(target) || copyAndDelete(stored, target)
        if (ok) {
            write(read().filter { it.id != e.id })
            notifyGallery(ctx, target.absolutePath)
        }
        ok
    }

    /** Elimina definitivamente. */
    fun purge(e: TrashEntry): Boolean = synchronized(lock) {
        val stored = File(filesDir, e.id)
        val ok = !stored.exists() || stored.deleteRecursively()
        if (ok) write(read().filter { it.id != e.id })
        ok
    }

    /** Borra definitivamente lo que lleva más de 30 días en la papelera. */
    fun purgeExpired(): Int {
        val limit = System.currentTimeMillis() - KEEP_DAYS * 86_400_000L
        return entries().filter { it.deletedAt < limit }.count { purge(it) }
    }

    /** Avisa a la galería de que un archivo (o carpeta) cambió de sitio. */
    private fun notifyGallery(ctx: Context, path: String) {
        try { MediaScannerConnection.scanFile(ctx.applicationContext, arrayOf(path), null, null) } catch (_: Exception) { }
    }
}
