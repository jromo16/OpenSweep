# Cómo probar OpenSweep

Hay tres niveles de prueba. Los dos primeros no cambian nada en tu PC.

| Nivel | Qué cubre | Cómo |
|---|---|---|
| Pruebas automáticas | Reglas de limpieza, protección de carpetas, winapp2, mapa del disco, restos, privacidad | `cd windows/OpenSweep.Tests && dotnet test` · `cd android && ./gradlew test` |
| Prueba completa en Windows Sandbox | Todo lo que borra o cambia el sistema, de verdad, en una máquina desechable | `tools/sandbox/probar-en-sandbox.ps1` |
| Lista manual | Lo que necesita ojos: la interfaz, los temas, Android | Esta página, más abajo |

## Prueba completa en Windows Sandbox

Windows Sandbox es un Windows limpio que se borra al cerrarlo. OpenSweep instala ahí un programa
falso, lo desinstala y comprueba que no quedan restos, limpia, repara el registro, aplica y
restaura la privacidad y crea y quita las tareas programadas.

**Activarlo (una sola vez).** Necesitas Windows 10/11 Pro, Education o Enterprise.
Abre PowerShell **como administrador**, ejecuta lo siguiente y reinicia:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
```

**Lanzar la prueba** desde la carpeta del repositorio, con el exe portable ya publicado en `dist`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\sandbox\probar-en-sandbox.ps1          # unos 3 minutos
powershell -ExecutionPolicy Bypass -File tools\sandbox\probar-en-sandbox.ps1 -Full    # añade cleanmgr y DISM (lento)
```

Al terminar, el informe se abre en el Bloc de notas del sandbox y queda guardado en
`dist\pruebas-sandbox\informe_<fecha>.txt` en tu PC. Cada línea empieza por `[OK]`, `[FALLO]` o `[AVISO]`.
La app queda abierta dentro del sandbox para seguir probando a mano.

`OpenSweep.exe --selftest` solo funciona dentro de Windows Sandbox. En cualquier otro equipo se niega
y no cambia nada.

### Qué comprueba

1. **Monitor de instalaciones**: detecta la carpeta y la entrada de desinstalación del programa falso.
2. **Desinstalación silenciosa** y **restos**: servicio, tarea programada, regla del firewall, menú
   contextual, asociación de archivos, carpetas de Program Files y AppData y claves del registro.
   Comprueba que no se marca nada ajeno al programa y que todo se borra de verdad, con copia de seguridad.
3. **Limpieza** con las reglas activadas por defecto. Comprueba que las carpetas del usuario y del sistema siguen ahí.
4. **Registro**: detecta una entrada rota de prueba, la repara y la copia `.reg` la restaura.
5. **Privacidad**: aplica los 12 ajustes y los restaura.
6. **Tareas programadas**: limpieza automática y monitor de la bandeja.
7. **winapp2**: descarga y lectura de las reglas de la comunidad (necesita internet).
8. Con `-Full`: Liberador de espacio de Windows y limpieza de componentes (DISM).

## Lista manual antes de publicar una versión

**Windows**

- [ ] El instalador instala, crea el acceso directo y se desinstala desde Configuración.
- [ ] Tema claro y oscuro: cambia con Windows y desde Opciones.
- [ ] En una pantalla pequeña (1366×768 o escalado al 150 %) se ven los botones de la ventana y el menú.
- [ ] Inglés: `OpenSweep.exe --lang en` no muestra textos en español.
- [ ] Limpieza: Analizar → la lista tiene sentido → Limpiar muestra el progreso y el resultado.
- [ ] Desinstalador: iconos, búsqueda, desinstalación de un programa real y sus restos.
- [ ] Analizador de disco: el mapa se dibuja, doble clic entra en una carpeta y «Subir» vuelve.
- [ ] Privacidad: marcar un ajuste, cerrar y abrir la app (sigue marcado), «Restaurar valores de Windows».
- [ ] Opciones: descargar y quitar las reglas de winapp2; aparecen en Limpieza bajo «Comunidad (winapp2)».

**Android**

- [ ] Primer inicio: pide los permisos y explica para qué.
- [ ] Limpieza → los archivos van a la papelera de OpenSweep y se pueden recuperar.
- [ ] Fotos: duplicados, similares y borrosas.
- [ ] Tema claro y oscuro, e inglés si el teléfono está en inglés.
- [ ] El widget muestra el espacio y abre la app.
