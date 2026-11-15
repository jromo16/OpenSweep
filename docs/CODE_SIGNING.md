# Code signing policy / Política de firma de código

*English below.*

## Español

Los ejecutables de Windows de OpenSweep (`OpenSweep-Setup.exe` y `OpenSweep-windows.exe`) se compilan a partir de este
repositorio con GitHub Actions (`.github/workflows/build.yml`) y, cuando la firma está configurada, se firman en ese mismo
proceso. Ningún ejecutable se firma fuera de esa compilación automática.

Firma de código gratuita proporcionada por [SignPath.io](https://about.signpath.io), certificado de
[SignPath Foundation](https://signpath.org) *(cuando se apruebe la solicitud)*.

### Responsables

| Función | Personas |
|---|---|
| Autores y responsables del código | [jromo16](https://github.com/jromo16) |
| Revisión de cambios de terceros | [jromo16](https://github.com/jromo16) (todo Pull Request se revisa antes de unirlo) |
| Aprobación de cada firma | [jromo16](https://github.com/jromo16) |

Las cuentas con permisos sobre el repositorio y la firma usan autenticación en dos pasos.

### Privacidad

OpenSweep no envía información personal ni datos de uso a ningún servidor. Solo se conecta a internet para:

- comprobar si hay una versión nueva en las releases de GitHub del proyecto (se puede desactivar en Opciones);
- descargar las reglas de la comunidad winapp2 desde su repositorio de GitHub, solo si el usuario lo pide;
- abrir en el navegador las páginas que el usuario elige (sitio web de un programa, búsqueda en Internet).

## English

OpenSweep's Windows executables (`OpenSweep-Setup.exe` and `OpenSweep-windows.exe`) are built from this repository by
GitHub Actions (`.github/workflows/build.yml`) and, when signing is configured, signed in that same pipeline. No executable
is signed outside that automated build.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) *(once the application is approved)*.

### Team roles

| Role | Members |
|---|---|
| Authors / committers | [jromo16](https://github.com/jromo16) |
| Reviewers (third-party contributions) | [jromo16](https://github.com/jromo16) (every pull request is reviewed before merging) |
| Approvers (each signing request) | [jromo16](https://github.com/jromo16) |

Accounts with access to the repository and to signing use multi-factor authentication.

### Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or
the person installing or operating it. The only network connections are:

- checking for a new version in the project's GitHub releases (can be turned off in Options);
- downloading the winapp2 community cleaning rules from their GitHub repository, only when the user asks for it;
- opening web pages the user chooses (a program's website, a web search) in the browser.
