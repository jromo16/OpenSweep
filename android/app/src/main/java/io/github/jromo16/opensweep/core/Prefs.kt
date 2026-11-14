package io.github.jromo16.opensweep.core

import android.content.Context

/** Ajustes simples de la app. */
object Prefs {
    private fun sp(ctx: Context) = ctx.getSharedPreferences("opensweep", Context.MODE_PRIVATE)

    fun junkAlerts(ctx: Context) = sp(ctx).getBoolean("junk_alerts", true)
    fun setJunkAlerts(ctx: Context, v: Boolean) = sp(ctx).edit().putBoolean("junk_alerts", v).apply()

    fun updateCheck(ctx: Context) = sp(ctx).getBoolean("update_check", true)
    fun setUpdateCheck(ctx: Context, v: Boolean) = sp(ctx).edit().putBoolean("update_check", v).apply()

    fun language(ctx: Context) = sp(ctx).getString("language", "auto") ?: "auto"
    fun setLanguage(ctx: Context, v: String) = sp(ctx).edit().putString("language", v).apply()

    fun theme(ctx: Context) = sp(ctx).getString("theme", "auto") ?: "auto"
    fun setTheme(ctx: Context, v: String) = sp(ctx).edit().putString("theme", v).apply()
}
