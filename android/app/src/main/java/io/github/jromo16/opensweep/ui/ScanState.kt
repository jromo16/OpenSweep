package io.github.jromo16.opensweep.ui

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.jromo16.opensweep.core.StorageScanner
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Resultado compartido del análisis del almacenamiento (lo usan Limpieza y Archivos). */
object ScanState {
    var scanner by mutableStateOf<StorageScanner?>(null)
        private set
    var scanning by mutableStateOf(false)
        private set
    var progress by mutableStateOf("")
        private set
    var version by mutableStateOf(0)
        private set

    suspend fun scan(ctx: Context): StorageScanner {
        scanning = true
        try {
            val s = StorageScanner(ctx.applicationContext)
            withContext(Dispatchers.IO) { s.scan { msg -> progress = msg } }
            scanner = s
            version++
            return s
        } finally {
            scanning = false
        }
    }

    /** Marca el análisis como desactualizado tras borrar archivos. */
    fun invalidate() {
        scanner = null
        version++
    }
}
