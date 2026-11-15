package io.github.jromo16.opensweep.core

/**
 * Decide si dos imágenes son «la misma foto» (copias, reenvíos, ráfagas).
 * La huella dHash (64 bits) sola no basta: dos capturas de apps distintas, casi todo fondo blanco,
 * dan huellas casi iguales. Por eso se compara también una miniatura en gris de 32×32,
 * y con las capturas de pantalla se exige que sean prácticamente idénticas.
 */
object Similarity {
    const val FINE = 32

    /** Diferencia media por píxel (0-255) entre dos miniaturas en gris del mismo tamaño. */
    fun meanAbsDiff(a: ByteArray, b: ByteArray): Double {
        if (a.size != b.size || a.isEmpty()) return 255.0
        var sum = 0L
        for (i in a.indices) sum += kotlin.math.abs((a[i].toInt() and 0xFF) - (b[i].toInt() and 0xFF))
        return sum.toDouble() / a.size
    }

    fun similar(hashA: Long, hashB: Long, fineA: ByteArray?, fineB: ByteArray?, screenshots: Boolean, threshold: Int): Boolean {
        val maxBits = if (screenshots) minOf(threshold, 2) else threshold
        if (java.lang.Long.bitCount(hashA xor hashB) > maxBits) return false
        if (fineA == null || fineB == null) return !screenshots
        return meanAbsDiff(fineA, fineB) <= if (screenshots) 3.0 else 12.0
    }
}
