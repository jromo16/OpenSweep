package io.github.jromo16.opensweep.core

import kotlin.math.max
import kotlin.math.min

/** Rectángulo en píxeles (o en cualquier unidad). */
data class Box(val x: Float, val y: Float, val w: Float, val h: Float)

/**
 * Treemap «cuadrado» (Bruls, Huizing y van Wijk), igual que en la versión de Windows: reparte un rectángulo
 * en rectángulos proporcionales a cada valor, lo más parecidos posible a un cuadrado.
 */
object Treemap {
    fun <T> layout(source: List<Pair<T, Double>>, area: Box): List<Pair<T, Box>> {
        val items = source.filter { it.second > 0 }.sortedByDescending { it.second }
        val out = ArrayList<Pair<T, Box>>()
        if (items.isEmpty() || area.w <= 0 || area.h <= 0) return out
        val scale = area.w * area.h / items.sumOf { it.second }
        val queue = items.map { it.first to it.second * scale }
        var rect = area
        var row = ArrayList<Pair<T, Double>>()
        var i = 0
        while (i < queue.size) {
            val side = min(rect.w, rect.h).toDouble()
            val next = queue[i]
            if (row.isEmpty() || worst(row, side) >= worst(row + next, side)) {
                row.add(next); i++
            } else {
                rect = placeRow(row, rect, out)
                row = ArrayList()
            }
        }
        if (row.isNotEmpty()) placeRow(row, rect, out)
        return out
    }

    private fun <T> worst(row: List<Pair<T, Double>>, side: Double): Double {
        val sum = row.sumOf { it.second }
        val mx = row.maxOf { it.second }
        val mn = row.minOf { it.second }
        val s2 = side * side
        val sum2 = sum * sum
        return max(s2 * mx / sum2, sum2 / (s2 * mn))
    }

    private fun <T> placeRow(row: List<Pair<T, Double>>, rect: Box, out: MutableList<Pair<T, Box>>): Box {
        val sum = row.sumOf { it.second }.toFloat()
        return if (rect.w >= rect.h) {
            // Columna a la izquierda
            val w = if (rect.h > 0) sum / rect.h else 0f
            var y = rect.y
            row.forEach { (item, a) -> val h = if (w > 0) a.toFloat() / w else 0f; out.add(item to Box(rect.x, y, w, h)); y += h }
            Box(rect.x + w, rect.y, max(0f, rect.w - w), rect.h)
        } else {
            // Fila arriba
            val h = if (rect.w > 0) sum / rect.w else 0f
            var x = rect.x
            row.forEach { (item, a) -> val w = if (h > 0) a.toFloat() / h else 0f; out.add(item to Box(x, rect.y, w, h)); x += w }
            Box(rect.x, rect.y + h, rect.w, max(0f, rect.h - h))
        }
    }
}

/**
 * Tamaño de cada carpeta a partir de la lista de archivos del análisis: lo que ocupa con todo su contenido,
 * sus subcarpetas y lo que ocupan sus archivos sueltos. Sirve para dibujar el mapa del almacenamiento.
 */
class FolderSizes(val root: String, files: List<FileRec>) {
    val total = HashMap<String, Long>()
    val direct = HashMap<String, Long>()
    val children = HashMap<String, MutableSet<String>>()

    init {
        val r = root.trimEnd('/')
        for (f in files) {
            var dir = f.folder
            if (!dir.startsWith(r)) continue
            direct[dir] = (direct[dir] ?: 0L) + f.size
            while (true) {
                total[dir] = (total[dir] ?: 0L) + f.size
                if (dir.length <= r.length) break
                val parent = dir.substringBeforeLast('/', r).ifEmpty { r }
                children.getOrPut(parent) { HashSet() }.add(dir)
                dir = if (parent.length < r.length) r else parent
            }
        }
    }

    /** Elementos para dibujar dentro de una carpeta: sus subcarpetas y, aparte, sus archivos sueltos. */
    fun entries(dir: String): List<MapEntry> {
        val list = children[dir].orEmpty().map { MapEntry(it, it.substringAfterLast('/'), total[it] ?: 0L, isFiles = false) }.toMutableList()
        val loose = direct[dir] ?: 0L
        if (loose > 0) list.add(MapEntry(dir, "", loose, isFiles = true))
        return list.sortedByDescending { it.bytes }
    }
}

data class MapEntry(val path: String, val name: String, val bytes: Long, val isFiles: Boolean)
