# Historial de cambios

Todas las versiones de OpenSweep. El formato de versión es **MAYOR.MENOR.CORRECCIÓN**:

- **CORRECCIÓN** (1.2.**1**): arreglos de fallos.
- **MENOR** (1.**3**.0): funciones nuevas o cambios grandes.
- **MAYOR** (**2**.0.0): cambios enormes que lo transforman todo.

## [1.3.0] - 2026-11-15

### Nuevo
- **Caché de todas las apps de un toque** (Android): OpenSweep abre los ajustes de cada app y pulsa «Borrar caché»
  por ti, con el servicio de accesibilidad (como SD Maid o CCleaner). Lo activas tú y solo actúa mientras limpias.
  Nunca pulsa «Borrar almacenamiento»: si no está seguro de qué botón es, no pulsa nada.
- **Salud de los discos** (Windows): en el Panel de salud, el estado que informa cada disco (S.M.A.R.T./NVMe),
  con temperatura, vida útil restante del SSD y avisos claros si conviene hacer una copia de seguridad.
- **Reparar Windows** (Windows): repara archivos del sistema dañados con DISM y SFC, con progreso y el resultado en tu idioma.
- **Limpiar el navegador al cerrarlo** (Windows): el monitor de la bandeja limpia Chrome, Edge, Firefox… en cuanto los cierras,
  según lo que tengas marcado en «Limpieza».

### Corregido
- Android: «Caché de aplicaciones» usaba el diálogo oficial de Android, que en la práctica no liberaba nada.
- Windows: el monitor de la bandeja ya no se abre dos veces.

## [1.2.0] - 2026-11-14

### Nuevo
- **Diseño nuevo inspirado en las apps de macOS**: barra lateral translúcida (Mica) agrupada por secciones,
  iconos de colores, barra de título integrada, títulos grandes y listas limpias con esquinas redondeadas.
- **Modo claro y oscuro** en Windows y Android: automático según el sistema (cambia en vivo) o a elección en Opciones / Ajustes.
- Argumento `--theme light|dark` para probar los temas en Windows.
- **Diálogos modernos** en lugar de los cuadros clásicos de Windows: icono de color, botón principal con la acción
  («Enviar a la papelera», «Eliminar»…) y rojo cuando se borra algo.
- **Desinstalación por lotes** (Windows): selecciona varios programas con Ctrl o Mayús, se desinstalan seguidos con un solo
  punto de restauración y al final se buscan los restos de todos.
- **Actualizaciones de Windows** (Windows): eliminar `Windows.old`, paquetes de controladores antiguos y componentes de Windows Update
  reemplazados, con el Liberador de espacio y DISM del propio Windows. Desmarcadas por defecto.
- **Papelera de 30 días** (Android): lo que se elimina de tu contenido se puede restaurar durante 30 días.
- **Sugerencias en Inicio** (Android): apps que no usas desde hace 3 meses, vídeos de más de 100 MB y descargas antiguas.
- **Privacidad de Windows** (Windows): 12 ajustes para el ID de publicidad, los datos de diagnóstico, los anuncios de Inicio
  y de la pantalla de bloqueo, Bing en la búsqueda, el historial de actividad, Recall y Copilot. Cada cambio guarda el valor
  original y se puede deshacer uno a uno o todos con «Restaurar valores de Windows».
- **Reglas de la comunidad winapp2** (Windows): desde Opciones se descargan reglas para más de 2.000 programas y juegos.
  Solo aparecen las de los programas instalados, desmarcadas y en su propio grupo. Nunca vacían una carpeta protegida.
- **Restos más profundos** en el desinstalador: servicios, tareas programadas, reglas del firewall, menús contextuales
  y asociaciones de archivos. Solo se marcan los que apuntan a la carpeta del programa; los que coinciden solo por el nombre
  o pueden ser compartidos (`http`, `mailto`…) aparecen sin marcar. Se guarda copia antes de borrarlos.
- **Mapa del disco** (Windows): el analizador dibuja cada carpeta como un rectángulo de tamaño proporcional.
  Doble clic para entrar, «Subir» para volver y clic derecho para abrirla en el Explorador.
- **Prueba completa en Windows Sandbox**: `tools/sandbox/probar-en-sandbox.ps1` instala un programa falso con restos de todo tipo
  en una máquina desechable y comprueba desinstalación, restos, limpieza, registro, privacidad y tareas programadas.
  Ver [docs/PRUEBAS.md](docs/PRUEBAS.md).
- Preparada la publicación en **winget** y **F-Droid** (`packaging/`).
- **Pruebas automáticas** (53) en Windows y Android, ejecutadas en GitHub con cada cambio.
- Plantillas para informar errores y proponer ideas en GitHub; capturas de pantalla en el README.
- **Progreso al eliminar**: barra con porcentaje y archivo actual, resumen final con marca verde y aviso breve
  en la parte inferior (fotos, duplicados, analizador de disco, restos, registro y destructor de archivos).

### Corregido
- Windows: el menú lateral no cambiaba de sección al hacer clic.
- Windows: error «Index was out of range» al eliminar fotos (los archivos sí se borraban, fallaba el refresco de la lista).
- Windows: las casillas de las tablas se marcan con un solo clic.
- Windows: la fila seleccionada en las listas ahora se distingue bien (verde con texto blanco).
- Windows: las casillas de las tablas salían cortadas; las apps de la Store muestran su nombre y fabricante reales
  (antes «4DF9E0F8.Netflix» y un código como fabricante).
- Windows: en portátiles o con el escalado de pantalla al 125-150 % la ventana era más grande que la pantalla y
  no se veían los botones de cerrar y minimizar. Ahora se ajusta al tamaño disponible, al maximizar los botones
  quedan dentro de la pantalla y en ventanas estrechas el menú se estrecha y los botones pasan a otra línea.
- Windows: más programas muestran su icono en el desinstalador (se busca también en el instalador MSI, la carpeta de
  instalación y el menú Inicio); los que no tienen icono propio muestran uno genérico.

### Seguridad (revisión general)
- Restos del desinstalador: lo que otro programa instalado podría estar usando se muestra **sin marcar**
  (antes, desinstalar «Python Launcher» ofrecía borrar la carpeta de Python).
- Duplicados: no se analizan carpetas de programas (AppData, Program Files, ProgramData) ni archivos de OneDrive solo en la nube.
- Fotos: solo se premarcan las copias de fotos similares; borrosas, oscuras y capturas las elige el usuario. Botón «Marcar todo».
- Vídeos similares: además de la miniatura, deben tener un tamaño parecido.
- Limpieza: el historial de navegación y los documentos recientes ya no vienen marcados; se quita la caché de documentos de Office.
- La limpieza automática nunca vacía la papelera; el Panel de salud siempre guarda copia del registro.
- Carpetas protegidas: también las que contienen una carpeta del sistema.
- Android: las carpetas vacías solo se borran si siguen vacías; no se deducen «restos» si la lista de apps está incompleta;
  nunca se ofrece borrar carpetas estándar (Music, Download…) como resto de una app.

## [1.1.0] - 2026-11-14

### Nuevo
- Fotos y vídeos similares, borrosos, oscuros y capturas (Windows y Android), con miniaturas.
- Limpiador de WhatsApp, WhatsApp Business y Telegram (Android).
- Lista blanca de cookies, limpieza programada y monitor en la bandeja (Windows).
- Modo cazador y monitor de instalaciones en el desinstalador (Windows).
- Centro de copias de seguridad (Windows).
- Avisos de basura y widget de escritorio (Android).
- Instalador, actualización automática y firma de código opcional.
- Inglés en ambas apps, con traducciones compartidas en `i18n/`.

## [1.0.0] - 2026-11-14

### Nuevo
- Primera versión: limpiador, limpiador del registro, desinstalador con búsqueda de restos, gestor de inicio,
  extensiones, analizador de disco, duplicados, borrado seguro y actualizador de programas (Windows);
  limpiador, gestor de apps, archivos grandes y duplicados (Android).
