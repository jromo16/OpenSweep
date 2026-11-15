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
}
