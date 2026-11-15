plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "io.github.jromo16.opensweep"
    compileSdk = 37

    defaultConfig {
        applicationId = "io.github.jromo16.opensweep"
        minSdk = 26
        targetSdk = 36
        versionCode = 5
        versionName = "1.3.1"
        // Compilación para F-Droid («-Pfdroid»): sin buscar versiones nuevas en GitHub (se actualiza desde F-Droid)
        buildConfigField("boolean", "UPDATE_CHECK", (!project.hasProperty("fdroid")).toString())
    }

    // Firma de publicación: se usa si existen las variables de entorno (por ejemplo, secretos de GitHub).
    // Si no, el APK se firma con la clave de depuración para poder instalarlo directamente.
    val releaseKeystore = System.getenv("OPENSWEEP_KEYSTORE")
    signingConfigs {
        if (releaseKeystore != null && file(releaseKeystore).exists()) {
            create("release") {
                storeFile = file(releaseKeystore)
                storePassword = System.getenv("OPENSWEEP_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("OPENSWEEP_KEY_ALIAS")
                keyPassword = System.getenv("OPENSWEEP_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // F-Droid firma con su propia clave: su compilación sale sin firmar
            signingConfig = if (project.hasProperty("fdroid")) null
                else signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    // Traducciones compartidas con la app de Windows (carpeta i18n/ del repositorio)
    sourceSets.getByName("main").assets.directories.add("../../i18n")
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2026.09.00")
    implementation(composeBom)
    implementation("androidx.core:core-ktx:1.18.0")
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("androidx.work:work-runtime-ktx:2.11.2")
    testImplementation("junit:junit:4.13.2")
}
