package io.github.jromo16.opensweep.core

import android.content.ContentUris
import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import android.os.Build
import android.provider.MediaStore
import android.util.Size
import java.io.File
import kotlin.math.ln
import kotlin.math.sqrt

data class MediaRec(
    val uri: Uri,
    val path: String,
    val name: String,
    val size: Long,
    val modified: Long,
    val isVideo: Boolean,
    val bucket: String,
) {
    var hash: Long = 0
    var sharpness = 0.0
    var brightness = 0.0
    var isScreenshot = false
    var isBest = false
    var group = 0
}

data class MediaResult(
    val total: Int,
    val similar: List<List<MediaRec>>,
    val similarVideos: List<List<MediaRec>>,
    val blurry: List<MediaRec>,
    val dark: List<MediaRec>,
    val screenshots: List<MediaRec>,
)

/** Fotos similares (dHash), borrosas, oscuras, capturas y vídeos similares usando MediaStore. */
object MediaAnalyzer {

    fun analyze(ctx: Context, threshold: Int, onProgress: (String) -> Unit): MediaResult {
        val items = query(ctx, false) + query(ctx, true)
        val ok = ArrayList<MediaRec>(items.size)
        items.forEachIndexed { i, m ->
            val bmp = thumbnail(ctx, m, 128)
            if (bmp != null) {
                val g9 = gray(bmp, 9, 8)
                val g64 = gray(bmp, 64, 64)
                m.hash = dHash(g9)
                val (s, b) = metrics(g64, 64)
                m.sharpness = s; m.brightness = b
                m.isScreenshot = !m.isVideo && looksLikeScreenshot(m)
                ok.add(m)
                bmp.recycle()
            }
            if (i % 20 == 0) onProgress("${t("Analizando")} ${i + 1} ${t("de")} ${items.size}…")
        }
        val photos = ok.filter { !it.isVideo }
        val similar = group(photos, threshold)
        val videos = group(ok.filter { it.isVideo }, minOf(threshold, 6))
        val normal = photos.filter { !it.isScreenshot }
        val blurry = if (normal.isEmpty()) emptyList() else {
            val median = normal.map { it.sharpness }.sorted()[normal.size / 2]
            val limit = minOf(60.0, median * 0.25)
            normal.filter { it.sharpness < limit }.sortedBy { it.sharpness }
        }
        return MediaResult(
            total = ok.size,
            similar = similar,
            similarVideos = videos,
            blurry = blurry,
            dark = normal.filter { it.brightness < 28 }.sortedBy { it.brightness },
            screenshots = photos.filter { it.isScreenshot }.sortedBy { it.modified },
        )
    }

    private fun query(ctx: Context, video: Boolean): List<MediaRec> {
        val collection = if (video) MediaStore.Video.Media.EXTERNAL_CONTENT_URI else MediaStore.Images.Media.EXTERNAL_CONTENT_URI
        @Suppress("DEPRECATION")
        val projection = arrayOf(
            MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.DISPLAY_NAME,
            MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATE_MODIFIED, MediaStore.MediaColumns.BUCKET_DISPLAY_NAME,
        )
        val list = mutableListOf<MediaRec>()
        try {
            ctx.contentResolver.query(collection, projection, "${MediaStore.MediaColumns.SIZE} > ?", arrayOf("10240"), null)?.use { c ->
                while (c.moveToNext()) {
                    val id = c.getLong(0)
                    list.add(
                        MediaRec(
                            uri = ContentUris.withAppendedId(collection, id),
                            path = c.getString(1) ?: "",
                            name = c.getString(2) ?: "",
                            size = c.getLong(3),
                            modified = c.getLong(4) * 1000,
                            isVideo = video,
                            bucket = c.getString(5) ?: "",
                        )
                    )
                }
            }
        } catch (_: Exception) { }
        return list
    }

    fun thumbnail(ctx: Context, m: MediaRec, size: Int): Bitmap? = try {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) ctx.contentResolver.loadThumbnail(m.uri, Size(size, size), null)
        else {
            @Suppress("DEPRECATION")
            val id = ContentUris.parseId(m.uri)
            @Suppress("DEPRECATION")
            if (m.isVideo) MediaStore.Video.Thumbnails.getThumbnail(ctx.contentResolver, id, MediaStore.Video.Thumbnails.MINI_KIND, null)
            else MediaStore.Images.Thumbnails.getThumbnail(ctx.contentResolver, id, MediaStore.Images.Thumbnails.MINI_KIND, null)
        }
    } catch (_: Exception) { null }

    private fun gray(src: Bitmap, w: Int, h: Int): IntArray {
        val s = Bitmap.createScaledBitmap(src, w, h, true)
        val px = IntArray(w * h)
        s.getPixels(px, 0, w, 0, 0, w, h)
        if (s != src) s.recycle()
        return IntArray(px.size) { i ->
            val p = px[i]
            ((p shr 16 and 0xFF) * 299 + (p shr 8 and 0xFF) * 587 + (p and 0xFF) * 114) / 1000
        }
    }

    private fun dHash(g: IntArray): Long {
        var h = 0L
        for (y in 0 until 8) for (x in 0 until 8) {
            h = h shl 1
            if (g[y * 9 + x] > g[y * 9 + x + 1]) h = h or 1L
        }
        return h
    }

    private fun metrics(g: IntArray, n: Int): Pair<Double, Double> {
        var sumL = 0.0; var sumL2 = 0.0; var count = 0
        for (y in 1 until n - 1) for (x in 1 until n - 1) {
            val c = y * n + x
            val l = 4.0 * g[c] - g[c - 1] - g[c + 1] - g[c - n] - g[c + n]
            sumL += l; sumL2 += l * l; count++
        }
        val mean = sumL / count
        return (sumL2 / count - mean * mean) to g.average()
    }

    private fun looksLikeScreenshot(m: MediaRec): Boolean {
        val b = m.bucket.lowercase(); val n = m.name.lowercase(); val p = m.path.lowercase()
        return b.contains("screenshot") || b.contains("captura") || n.startsWith("screenshot") || p.contains("/screenshots/")
    }

    /** Agrupa por semilla (la foto más pesada) para no encadenar fotos distintas. */
    private fun group(items: List<MediaRec>, threshold: Int): List<List<MediaRec>> {
        val ordered = items.filter { it.hash != 0L && it.hash != -1L }.sortedByDescending { it.size }
        val assigned = BooleanArray(ordered.size)
        val groups = mutableListOf<List<MediaRec>>()
        for (a in ordered.indices) {
            if (assigned[a]) continue
            val g = mutableListOf(ordered[a])
            for (b in a + 1 until ordered.size) {
                if (!assigned[b] && java.lang.Long.bitCount(ordered[a].hash xor ordered[b].hash) <= threshold) {
                    g.add(ordered[b]); assigned[b] = true
                }
            }
            if (g.size > 1) groups.add(g)
        }
        groups.forEachIndexed { gi, g ->
            val best = g.maxBy { ln(it.size + 1.0) * sqrt(it.sharpness + 1) }
            g.forEach { it.group = gi + 1; it.isBest = it == best }
        }
        return groups.sortedByDescending { g -> g.sumOf { it.size } }.map { g -> g.sortedByDescending { it.isBest } }
    }

    /** Borra un archivo multimedia (MediaStore + archivo). */
    fun delete(ctx: Context, m: MediaRec): Boolean = try {
        val rows = ctx.contentResolver.delete(m.uri, null, null)
        rows > 0 || (m.path.isNotEmpty() && File(m.path).delete())
    } catch (_: Exception) {
        m.path.isNotEmpty() && File(m.path).delete()
    }
}
