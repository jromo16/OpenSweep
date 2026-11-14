# Cómo contribuir a OpenSweep

¡Gracias por querer ayudar! Así puedes colaborar.

## Reportar errores o pedir funciones

Abre un [issue](https://github.com/jromo16/OpenSweep/issues) e incluye:

- Plataforma y versión (por ejemplo, Windows 11 24H2 o Android 14).
- Qué hiciste, qué esperabas que pasara y qué pasó.
- Capturas de pantalla, si ayudan.

## Enviar cambios

1. Haz un fork del repositorio y crea una rama: `git checkout -b mi-mejora`.
2. Haz tus cambios y comprueba que el proyecto compila:
   - Windows: `cd windows/OpenSweep && dotnet build`
   - Android: `cd android && ./gradlew assembleDebug`
3. Prueba tu cambio en un equipo real o en una máquina virtual.
4. Abre un Pull Request explicando qué cambia y por qué.

## Reglas importantes

- **La seguridad va primero.** Todo lo que borre datos debe:
  - mostrar antes lo que se va a eliminar (analizar → confirmar → limpiar);
  - respetar `PathHelper.IsProtectedFolder` y las exclusiones del usuario;
  - en el registro, hacer copia de seguridad antes de modificar.
- Ante la duda, una regla nueva debe venir **desactivada por defecto** (`DefaultOn = false`).
- Nada de telemetría, anuncios ni conexiones a servidores de terceros.
- Textos de la interfaz en español, claros y sin tecnicismos innecesarios.

## Añadir una regla de limpieza (Windows)

En `windows/OpenSweep/Services/JunkCleaner.cs`, dentro de `BuildRules()`:

```csharp
App("miapp", "Mi App", "Caché", ["MiApp"], Path.Combine(L, @"MiApp\Cache"));
```

Los parámetros son: id, sección, nombre, procesos que deben estar cerrados y carpetas.

## Publicar una versión

Los mantenedores crean una etiqueta `vX.Y.Z`. GitHub Actions compila el `.exe` y el `.apk` y los adjunta a la release automáticamente.
