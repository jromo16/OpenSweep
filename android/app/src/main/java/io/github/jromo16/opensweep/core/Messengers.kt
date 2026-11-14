package io.github.jromo16.opensweep.core

import android.os.Environment

/** Categoría de archivos de una app de mensajería (p. ej. «Vídeos de WhatsApp»). */
data class MessengerCategory(
    val app: String,
    val name: String,
    val files: List<FileRec>,
) {
    val bytes get() = files.sumOf { it.size }
    val sentBytes get() = files.filter { it.isSent }.sumOf { it.size }
    val key get() = "$app/$name"
}

val FileRec.isSent get() = path.contains("/Sent/", ignoreCase = true)

/** Limpiador de WhatsApp, WhatsApp Business y Telegram a partir de los archivos ya analizados. */
object Messengers {

    private data class AppDef(val app: String, val roots: List<String>, val folders: Map<String, String>)

    private fun defs(): List<AppDef> {
        val r = Environment.getExternalStorageDirectory().absolutePath
        val wa = mapOf(
            "Images" to "Fotos", "Video" to "Vídeos", "Audio" to "Audios", "Voice Notes" to "Notas de voz",
            "Documents" to "Documentos", "Stickers" to "Stickers", "Animated Gifs" to "GIF", ".Statuses" to "Estados",
            "Video Notes" to "Videomensajes", "Profile Photos" to "Fotos de perfil", "Wallpaper" to "Fondos",
        )
        val tg = mapOf(
            "Images" to "Fotos", "Video" to "Vídeos", "Audio" to "Audios", "Documents" to "Documentos", "Files" to "Archivos",
        )
        return listOf(
            AppDef("WhatsApp", listOf("$r/Android/media/com.whatsapp/WhatsApp/Media", "$r/WhatsApp/Media"), wa),
            AppDef("WhatsApp Business", listOf("$r/Android/media/com.whatsapp.w4b/WhatsApp Business/Media", "$r/WhatsApp Business/Media"), wa),
            AppDef("Telegram", listOf("$r/Android/media/org.telegram.messenger/Telegram", "$r/Telegram", "$r/Download/Telegram"), tg),
        )
    }

    fun categories(files: List<FileRec>): List<MessengerCategory> {
        val result = mutableListOf<MessengerCategory>()
        for (def in defs()) {
            val appFiles = files.filter { f -> def.roots.any { f.path.startsWith("$it/") } }
            if (appFiles.isEmpty()) continue
            val byCat = appFiles.groupBy { f ->
                val root = def.roots.first { f.path.startsWith("$it/") }
                val first = f.path.removePrefix("$root/").substringBefore('/')
                // "WhatsApp Images" -> "Images", "Telegram Video" -> "Video"
                val key = first.removePrefix("WhatsApp ").removePrefix("WhatsApp Business ").removePrefix("Telegram ")
                def.folders[key] ?: if (first == f.name) "Otros" else first
            }
            byCat.forEach { (cat, fs) -> result.add(MessengerCategory(def.app, cat, fs.filter { !it.name.startsWith(".nomedia") })) }
        }
        return result.filter { it.files.isNotEmpty() }.sortedByDescending { it.bytes }
    }
}
