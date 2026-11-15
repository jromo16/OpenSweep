# Distribución

OpenSweep se publica en GitHub Releases automáticamente al crear una etiqueta `vX.Y.Z`
(ver `.github/workflows/build.yml`). Desde ahí se puede llevar a las tiendas de software libre.

## winget (Windows)

Permite instalar con `winget install jromo16.OpenSweep`.

1. Publica la versión en GitHub Releases (el instalador `OpenSweep-Setup.exe` debe existir).
2. Genera el manifiesto con el hash del instalador:
   `powershell -ExecutionPolicy Bypass -File packaging\winget\generar-manifiesto.ps1 -Version 1.2.0`
3. Valídalo: `winget validate --manifest packaging\winget\manifests\j\jromo16\OpenSweep\1.2.0`
4. Haz un fork de [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), copia la carpeta
   `manifests\j\jromo16\OpenSweep\1.2.0` al mismo sitio y abre un Pull Request.
   Los robots de Microsoft instalan el programa en una máquina virtual y lo revisan (1–3 días).

Para las versiones siguientes basta repetir los pasos 2–4 con el número nuevo.

Sin firma de código, SmartScreen puede mostrar un aviso al instalar. Ver `docs/FIRMA.md`.

## F-Droid (Android)

F-Droid compila la app desde el código fuente, por eso solo acepta apps sin componentes privativos
(OpenSweep no usa Google Play Services ni rastreadores).

1. Publica la etiqueta `v1.2.0`.
2. Haz un fork de [fdroiddata](https://gitlab.com/fdroid/fdroiddata) en GitLab y copia
   `packaging/fdroid/io.github.jromo16.opensweep.yml` a `metadata/`.
3. Comprueba la receta con `fdroid lint` y `fdroid build -v -l io.github.jromo16.opensweep`
   (o deja que lo haga su integración continua) y abre un Merge Request.

La descripción, el icono y las capturas de la ficha están en `fastlane/metadata/android/`.
Las notas de cada versión van en `changelogs/<versionCode>.txt`.

Si F-Droid todavía no admite la versión de Android Gradle Plugin del proyecto, la revisión lo indicará;
en ese caso se puede publicar antes en [IzzyOnDroid](https://apt.izzysoft.de/fdroid/), que usa el APK de
GitHub Releases directamente.
