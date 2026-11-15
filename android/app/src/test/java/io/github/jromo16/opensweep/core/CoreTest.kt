package io.github.jromo16.opensweep.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Locale

/** Pruebas de la lógica que no depende del teléfono (se ejecutan en el PC con ./gradlew test). */
class CoreTest {

    @Test
    fun `una version mayor es nueva`() {
        assertTrue(Updater.isNewer("1.2.0", "1.1.0"))
        assertTrue(Updater.isNewer("1.10.0", "1.9.9"))   // se compara como número, no como texto
        assertTrue(Updater.isNewer("2.0", "1.9.9"))
    }

    @Test
    fun `la misma version o una anterior no es nueva`() {
        assertFalse(Updater.isNewer("1.2.0", "1.2.0"))
        assertFalse(Updater.isNewer("1.1.9", "1.2.0"))
        assertFalse(Updater.isNewer("1.2", "1.2.0"))
    }

    @Test
    fun `los tamaños se muestran en la unidad adecuada`() {
        Locale.setDefault(Locale.US)
        assertEquals("512 B", formatBytes(512))
        assertEquals("1.5 KB", formatBytes(1536))
        assertEquals("100 MB", formatBytes(100L * 1024 * 1024))
        assertEquals("2.0 GB", formatBytes(2L * 1024 * 1024 * 1024))
    }

    @Test
    fun `los archivos enviados de whatsapp se reconocen`() {
        assertTrue(FileRec("/sdcard/WhatsApp/Media/WhatsApp Video/Sent/v.mp4", 1, 0).isSent)
        assertFalse(FileRec("/sdcard/WhatsApp/Media/WhatsApp Video/v.mp4", 1, 0).isSent)
    }

    // ---- Fotos similares ----
    private fun img(fill: (Int) -> Int) = ByteArray(Similarity.FINE * Similarity.FINE) { fill(it).toByte() }

    @Test
    fun `la misma foto recomprimida es similar`() {
        val a = img { (it * 7) % 256 }
        val b = img { ((it * 7) % 256 + 2).coerceAtMost(255) }   // pequeñas diferencias de compresión
        assertTrue(Similarity.similar(0b1010L, 0b1011L, a, b, screenshots = false, threshold = 10))
    }

    @Test
    fun `huellas iguales pero contenido distinto no son similares`() {
        // Dos capturas casi blancas: la huella coincide, pero una tiene un bloque oscuro grande
        val formulario = img { 240 }
        val bienvenida = img { i -> if (i % Similarity.FINE in 4..27 && i / Similarity.FINE in 10..20) 60 else 240 }
        assertFalse(Similarity.similar(42L, 42L, formulario, bienvenida, screenshots = true, threshold = 10))
        assertFalse(Similarity.similar(42L, 42L, formulario, bienvenida, screenshots = false, threshold = 10))
    }

    @Test
    fun `las capturas exigen un parecido casi exacto`() {
        val a = img { 240 }
        val b = img { i -> if (i < 512) 230 else 240 }            // texto distinto en la mitad de arriba (media 5)
        assertTrue(Similarity.similar(42L, 42L, a, b, screenshots = false, threshold = 10))
        assertFalse(Similarity.similar(42L, 42L, a, b, screenshots = true, threshold = 10))
        assertFalse(Similarity.similar(0L, 0b111L, a, a, screenshots = true, threshold = 10))   // 3 bits: demasiados para una captura
    }

    // ---- Mapa del almacenamiento ----
    @Test
    fun `el mapa reparte el area de forma proporcional`() {
        val area = Box(0f, 0f, 400f, 300f)
        val items = listOf("a" to 50.0, "b" to 25.0, "c" to 15.0, "d" to 10.0)
        val layout = Treemap.layout(items, area)
        assertEquals(4, layout.size)
        for ((item, r) in layout) {
            val expected = items.first { it.first == item }.second / 100.0 * 400 * 300
            assertTrue(kotlin.math.abs(r.w * r.h - expected) < expected * 0.01)
            assertTrue(r.x >= -0.01f && r.y >= -0.01f && r.x + r.w <= 400.01f && r.y + r.h <= 300.01f)
        }
    }

    @Test
    fun `los tamanos de carpeta suman todo su contenido`() {
        val files = listOf(
            FileRec("/sdcard/DCIM/Camera/a.jpg", 100, 0),
            FileRec("/sdcard/DCIM/Camera/b.jpg", 50, 0),
            FileRec("/sdcard/DCIM/c.jpg", 10, 0),
            FileRec("/sdcard/Download/d.zip", 40, 0),
        )
        val s = FolderSizes("/sdcard", files)
        assertEquals(200L, s.total["/sdcard"])
        assertEquals(160L, s.total["/sdcard/DCIM"])
        assertEquals(150L, s.total["/sdcard/DCIM/Camera"])
        val root = s.entries("/sdcard")
        assertEquals(listOf("DCIM", "Download"), root.map { it.name })
        // En DCIM: la subcarpeta Camera y los archivos sueltos (c.jpg)
        val dcim = s.entries("/sdcard/DCIM")
        assertEquals(2, dcim.size)
        assertEquals(10L, dcim.single { it.isFiles }.bytes)
    }
}
