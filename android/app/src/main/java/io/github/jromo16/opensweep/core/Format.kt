package io.github.jromo16.opensweep.core

import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

fun formatBytes(bytes: Long): String {
    if (bytes < 1024) return "$bytes B"
    val units = arrayOf("KB", "MB", "GB", "TB")
    var v = bytes / 1024.0
    var i = 0
    while (v >= 1024 && i < units.size - 1) {
        v /= 1024; i++
    }
    return String.format(Locale.getDefault(), if (v >= 100) "%.0f %s" else "%.1f %s", v, units[i])
}

fun formatDate(millis: Long): String =
    if (millis <= 0) "—" else SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(millis))

fun formatAgo(millis: Long): String {
    if (millis <= 0) return "Nunca / sin datos"
    val days = (System.currentTimeMillis() - millis) / 86_400_000L
    return when {
        days < 1 -> "Hoy"
        days < 2 -> "Ayer"
        days < 30 -> "Hace $days días"
        days < 365 -> "Hace ${days / 30} meses"
        else -> "Hace más de un año"
    }
}
