# Firma de código

Sin firma, Windows SmartScreen muestra «Windows protegió su PC» la primera vez que alguien abre el instalador,
y Google Play no acepta APK firmados con la clave de depuración. Las releases se pueden firmar automáticamente
configurando estos **secretos del repositorio** (GitHub › Settings › Secrets and variables › Actions).

## Windows

| Secreto | Contenido |
|---|---|
| `WINDOWS_CERT_PFX` | El certificado de firma de código `.pfx` codificado en base64 |
| `WINDOWS_CERT_PASSWORD` | La contraseña del `.pfx` |

Para codificarlo en base64 (PowerShell):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("certificado.pfx")) | Set-Clipboard
```

Opciones para conseguir un certificado:

- **SignPath Foundation**: firma gratuita para proyectos de código abierto (hay que solicitarla y usar su acción de GitHub en lugar de `signtool`).
- **Azure Trusted Signing**: económico (unos pocos dólares al mes) y sin hardware.
- Un certificado comercial de firma de código (Sectigo, DigiCert, etc.).

La reputación de SmartScreen se acumula con las descargas: aun firmado, el aviso puede aparecer durante las primeras semanas.

## Android

| Secreto | Contenido |
|---|---|
| `ANDROID_KEYSTORE` | El keystore `.jks` codificado en base64 |
| `ANDROID_KEYSTORE_PASSWORD` | Contraseña del keystore |
| `ANDROID_KEY_ALIAS` | Alias de la clave |
| `ANDROID_KEY_PASSWORD` | Contraseña de la clave |

Crear un keystore:

```bash
keytool -genkeypair -v -keystore opensweep.jks -alias opensweep -keyalg RSA -keysize 4096 -validity 10000
```

**Guarda el keystore y sus contraseñas en un lugar seguro**: si se pierde, no se podrán publicar actualizaciones
con la misma firma. Nunca lo subas al repositorio.
