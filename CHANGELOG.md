# Historial de cambios

Todas las versiones de OpenSweep. El formato de versión es **MAYOR.MENOR.CORRECCIÓN**:

- **CORRECCIÓN** (1.2.**1**): arreglos de fallos.
- **MENOR** (1.**3**.0): funciones nuevas o cambios grandes.
- **MAYOR** (**2**.0.0): cambios enormes que lo transforman todo.

## [1.2.0] - 2026-11-14

### Nuevo
- **Diseño nuevo inspirado en las apps de macOS**: barra lateral translúcida (Mica) agrupada por secciones,
  iconos de colores, barra de título integrada, títulos grandes y listas limpias con esquinas redondeadas.
- **Modo claro y oscuro** en Windows y Android: automático según el sistema (cambia en vivo) o a elección en Opciones / Ajustes.
- Argumento `--theme light|dark` para probar los temas en Windows.
- **Diálogos modernos** en lugar de los cuadros clásicos de Windows: icono de color, botón principal con la acción
  («Enviar a la papelera», «Eliminar»…) y rojo cuando se borra algo.
- **Progreso al eliminar**: barra con porcentaje y archivo actual, resumen final con marca verde y aviso breve
  en la parte inferior (fotos, duplicados, analizador de disco, restos, registro y destructor de archivos).

### Corregido
- Windows: el menú lateral no cambiaba de sección al hacer clic.
- Windows: error «Index was out of range» al eliminar fotos (los archivos sí se borraban, fallaba el refresco de la lista).
- Windows: las casillas de las tablas se marcan con un solo clic.
- Windows: la fila seleccionada en las listas ahora se distingue bien (verde con texto blanco).
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
