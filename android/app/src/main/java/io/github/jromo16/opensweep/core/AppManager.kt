package io.github.jromo16.opensweep.core

import android.app.usage.StorageStatsManager
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.os.Environment
import android.os.Process
import android.os.storage.StorageManager
import androidx.core.graphics.drawable.toBitmap
import java.io.File

data class AppEntry(
    val packageName: String,
    val label: String,
    val versionName: String,
    val installed: Long,
    val updated: Long,
    val isSystem: Boolean,
    val apkBytes: Long,
    val appBytes: Long,     // app (apk + libs) según StorageStats
    val dataBytes: Long,
    val cacheBytes: Long,
    val lastUsed: Long,
) {
    val totalBytes get() = if (appBytes > 0) appBytes + dataBytes else apkBytes
}

object AppManager {

    fun loadApps(ctx: Context, includeSystem: Boolean): List<AppEntry> {
        val pm = ctx.packageManager
        val usage = Permissions.hasUsageAccess(ctx)
        val ssm = ctx.getSystemService(Context.STORAGE_STATS_SERVICE) as StorageStatsManager
        val lastUsed = if (usage) lastUsedMap(ctx) else emptyMap()
        val user = Process.myUserHandle()

        return pm.getInstalledApplications(0).mapNotNull { ai ->
            val system = (ai.flags and ApplicationInfo.FLAG_SYSTEM) != 0 && (ai.flags and ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) == 0
            if (system && !includeSystem) return@mapNotNull null
            if (ai.packageName == ctx.packageName) return@mapNotNull null
            val pi = try { pm.getPackageInfo(ai.packageName, 0) } catch (_: Exception) { null }
            var app = 0L; var data = 0L; var cache = 0L
            if (usage) {
                try {
                    val st = ssm.queryStatsForPackage(StorageManager.UUID_DEFAULT, ai.packageName, user)
                    app = st.appBytes; data = st.dataBytes; cache = st.cacheBytes
                } catch (_: Exception) { }
            }
            val apk = (listOf(ai.sourceDir) + (ai.splitSourceDirs?.toList() ?: emptyList()))
                .sumOf { p -> p?.let { File(it).length() } ?: 0L }
            AppEntry(
                packageName = ai.packageName,
                label = ai.loadLabel(pm).toString(),
                versionName = pi?.versionName ?: "",
                installed = pi?.firstInstallTime ?: 0,
                updated = pi?.lastUpdateTime ?: 0,
                isSystem = system,
                apkBytes = apk,
                appBytes = app, dataBytes = data, cacheBytes = cache,
                lastUsed = lastUsed[ai.packageName] ?: 0,
            )
        }.sortedBy { it.label.lowercase() }
    }

    /** Suma de la caché de todas las apps (requiere acceso de uso). */
    fun totalCacheBytes(ctx: Context): Long? {
        if (!Permissions.hasUsageAccess(ctx)) return null
        val ssm = ctx.getSystemService(Context.STORAGE_STATS_SERVICE) as StorageStatsManager
        val user = Process.myUserHandle()
        return ctx.packageManager.getInstalledApplications(0).sumOf { ai ->
            try { ssm.queryStatsForPackage(StorageManager.UUID_DEFAULT, ai.packageName, user).cacheBytes } catch (_: Exception) { 0L }
        }
    }

    private fun lastUsedMap(ctx: Context): Map<String, Long> {
        val usm = ctx.getSystemService(Context.USAGE_STATS_SERVICE) as UsageStatsManager
        val end = System.currentTimeMillis()
        val start = end - 365L * 86_400_000L
        return try {
            usm.queryAndAggregateUsageStats(start, end).mapValues { it.value.lastTimeUsed }
        } catch (_: Exception) { emptyMap() }
    }

    fun icon(ctx: Context, pkg: String): Bitmap? = try {
        ctx.packageManager.getApplicationIcon(pkg).toBitmap(96, 96)
    } catch (_: Exception) { null }

    fun isInstalled(ctx: Context, pkg: String): Boolean = try {
        ctx.packageManager.getPackageInfo(pkg, 0); true
    } catch (_: PackageManager.NameNotFoundException) { false }

    /** Carpetas que una app desinstalada puede haber dejado en el almacenamiento compartido. */
    fun leftovers(pkg: String, label: String): List<String> {
        val root = Environment.getExternalStorageDirectory().absolutePath
        val candidates = mutableListOf(
            "$root/Android/media/$pkg", "$root/Android/data/$pkg", "$root/Android/obb/$pkg",
        )
        // Carpetas en la raíz con el nombre de la app (p. ej. /sdcard/Telegram)
        val cleanLabel = label.filter { it.isLetterOrDigit() }.lowercase()
        if (cleanLabel.length >= 4) {
            File(root).listFiles()?.forEach { d ->
                if (d.isDirectory && d.name.filter { it.isLetterOrDigit() }.lowercase() == cleanLabel) candidates.add(d.absolutePath)
            }
        }
        return candidates.filter { File(it).exists() }
    }
}
