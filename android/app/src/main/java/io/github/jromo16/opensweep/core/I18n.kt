package io.github.jromo16.opensweep.core

import android.content.Context
import org.json.JSONObject
import java.util.Locale

/**
 * Traducciones sencillas: el texto en español es la clave.
 * Los idiomas están en la carpeta i18n/ del repositorio (compartida con Windows)
 * y se empaquetan como assets: para añadir uno basta con crear i18n/xx.json.
 */
object I18n {
    var lang: String = "es"
        private set
    private var map: Map<String, String> = emptyMap()

    fun init(ctx: Context) {
        val available = ctx.assets.list("")?.filter { it.endsWith(".json") }?.map { it.removeSuffix(".json") } ?: emptyList()
        val pref = Prefs.language(ctx)
        val system = Locale.getDefault().language
        lang = when {
            pref != "auto" -> pref
            system == "es" -> "es"
            system in available -> system
            else -> "en"
        }
        map = if (lang == "es") emptyMap() else try {
            val o = JSONObject(ctx.assets.open("$lang.json").bufferedReader().use { it.readText() })
            o.keys().asSequence().associateWith { o.getString(it) }
        } catch (_: Exception) { emptyMap() }
    }

    fun t(es: String): String = if (lang == "es") es else map[es] ?: es
}

/** Atajo: t("Texto en español"). */
fun t(es: String): String = I18n.t(es)
