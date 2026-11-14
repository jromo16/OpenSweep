package io.github.jromo16.opensweep.core

import android.app.ActivityManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.os.Build
import android.os.Environment
import android.os.StatFs
import android.os.SystemClock
import java.io.File

data class StorageStatus(val total: Long, val free: Long) {
    val used get() = total - free
    val percent get() = if (total == 0L) 0f else used.toFloat() / total
}

data class MemoryStatus(val total: Long, val available: Long, val lowMemory: Boolean) {
    val used get() = total - available
    val percent get() = if (total == 0L) 0f else used.toFloat() / total
}

data class BatteryStatus(
    val level: Int,
    val charging: Boolean,
    val temperatureC: Float,
    val voltage: Int,
    val health: String,
    val technology: String,
    val plugged: String,
)

object DeviceInfo {
    fun storage(): StorageStatus {
        val stat = StatFs(Environment.getDataDirectory().path)
        return StorageStatus(stat.totalBytes, stat.availableBytes)
    }

    fun memory(ctx: Context): MemoryStatus {
        val am = ctx.getSystemService(Context.ACTIVITY_SERVICE) as ActivityManager
        val mi = ActivityManager.MemoryInfo()
        am.getMemoryInfo(mi)
        return MemoryStatus(mi.totalMem, mi.availMem, mi.lowMemory)
    }

    fun battery(ctx: Context): BatteryStatus? {
        val i = ctx.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return null
        val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100)
        val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val health = when (i.getIntExtra(BatteryManager.EXTRA_HEALTH, 0)) {
            BatteryManager.BATTERY_HEALTH_GOOD -> "Buena"
            BatteryManager.BATTERY_HEALTH_OVERHEAT -> "Sobrecalentada"
            BatteryManager.BATTERY_HEALTH_DEAD -> "Agotada"
            BatteryManager.BATTERY_HEALTH_OVER_VOLTAGE -> "Sobretensión"
            BatteryManager.BATTERY_HEALTH_COLD -> "Fría"
            BatteryManager.BATTERY_HEALTH_UNSPECIFIED_FAILURE -> "Fallo"
            else -> "Desconocida"
        }
        val plugged = when (i.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0)) {
            BatteryManager.BATTERY_PLUGGED_AC -> "Cargador"
            BatteryManager.BATTERY_PLUGGED_USB -> "USB"
            BatteryManager.BATTERY_PLUGGED_WIRELESS -> "Inalámbrica"
            else -> "Desconectado"
        }
        return BatteryStatus(
            level = if (scale > 0) level * 100 / scale else level,
            charging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL,
            temperatureC = i.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) / 10f,
            voltage = i.getIntExtra(BatteryManager.EXTRA_VOLTAGE, 0),
            health = health,
            technology = i.getStringExtra(BatteryManager.EXTRA_TECHNOLOGY) ?: "—",
            plugged = plugged,
        )
    }

    fun uptimeText(): String {
        val s = SystemClock.elapsedRealtime() / 1000
        val d = s / 86400; val h = (s % 86400) / 3600; val m = (s % 3600) / 60
        return if (d > 0) "$d d $h h $m min" else "$h h $m min"
    }

    fun cpuInfo(): String {
        val hw = try {
            File("/proc/cpuinfo").readLines().firstOrNull { it.startsWith("Hardware") }?.substringAfter(":")?.trim()
        } catch (_: Exception) { null }
        val soc = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) "${Build.SOC_MANUFACTURER} ${Build.SOC_MODEL}".trim() else null
        return listOfNotNull(soc?.takeIf { it.isNotBlank() && it != "unknown unknown" }, hw, Build.HARDWARE).first()
    }

    fun rows(ctx: Context): List<Pair<String, String>> {
        val mem = memory(ctx)
        val st = storage()
        return listOf(
            "Fabricante" to Build.MANUFACTURER.replaceFirstChar { it.uppercase() },
            "Modelo" to Build.MODEL,
            "Android" to "${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
            "Parche de seguridad" to Build.VERSION.SECURITY_PATCH,
            "Procesador" to cpuInfo(),
            "Núcleos" to Runtime.getRuntime().availableProcessors().toString(),
            "Arquitectura" to Build.SUPPORTED_ABIS.joinToString(", "),
            "Memoria RAM" to "${formatBytes(mem.used)} usados de ${formatBytes(mem.total)}",
            "Almacenamiento" to "${formatBytes(st.used)} usados de ${formatBytes(st.total)}",
            "Tiempo encendido" to uptimeText(),
        )
    }
}
