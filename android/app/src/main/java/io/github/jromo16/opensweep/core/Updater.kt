package io.github.jromo16.opensweep.core

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

data class UpdateInfo(val version: String, val pageUrl: String, val apkUrl: String?)

/** Comprueba si hay una versión nueva en las releases de GitHub. */
object Updater {
    private const val API = "https://api.github.com/repos/jromo16/OpenSweep/releases/latest"

    fun check(currentVersion: String): UpdateInfo? = try {
        val conn = (URL(API).openConnection() as HttpURLConnection).apply {
            connectTimeout = 8000; readTimeout = 8000
            setRequestProperty("Accept", "application/vnd.github+json")
            setRequestProperty("User-Agent", "OpenSweep")
        }
        val json = conn.inputStream.bufferedReader().use { it.readText() }
        conn.disconnect()
        val o = JSONObject(json)
        val tag = o.getString("tag_name").removePrefix("v")
        val assets = o.optJSONArray("assets")
        var apk: String? = null
        if (assets != null) for (i in 0 until assets.length()) {
            val a = assets.getJSONObject(i)
            if (a.getString("name").endsWith(".apk")) apk = a.getString("browser_download_url")
        }
        if (isNewer(tag, currentVersion)) UpdateInfo(tag, o.getString("html_url"), apk) else null
    } catch (_: Exception) { null }

    fun isNewer(remote: String, local: String): Boolean {
        val r = remote.split('.', '-').mapNotNull { it.toIntOrNull() }
        val l = local.split('.', '-').mapNotNull { it.toIntOrNull() }
        for (i in 0 until maxOf(r.size, l.size)) {
            val a = r.getOrElse(i) { 0 }; val b = l.getOrElse(i) { 0 }
            if (a != b) return a > b
        }
        return false
    }
}
