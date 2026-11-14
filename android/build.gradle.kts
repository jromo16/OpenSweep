plugins {
    id("com.android.application") version "9.2.1" apply false
    // AGP 9 incluye Kotlin; fijamos la versión del compilador de Kotlin y del plugin de Compose
    id("org.jetbrains.kotlin.android") version "2.3.21" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.3.21" apply false
}
