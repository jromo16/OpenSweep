package io.github.jromo16.opensweep.core

import android.content.Context
import java.util.Locale

/**
 * Traducciones sencillas: el texto en español es la clave.
 * Para añadir un idioma, crea un mapa como [En] y regístralo en [languages].
 */
object I18n {
    var lang: String = "es"
        private set

    private val languages: Map<String, Map<String, String>> = mapOf("en" to En.map)

    fun init(ctx: Context) {
        val pref = Prefs.language(ctx)
        lang = if (pref == "auto") (if (Locale.getDefault().language == "es") "es" else "en") else pref
    }

    fun t(es: String): String = if (lang == "es") es else languages[lang]?.get(es) ?: es
}

/** Atajo: t("Texto en español"). */
fun t(es: String): String = I18n.t(es)
