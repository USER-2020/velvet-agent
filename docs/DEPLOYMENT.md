# Despliegue Windows

CI automático y release corren únicamente en Windows. Los Pull Requests hacia `main` ejecutan la validación; el release se dispara únicamente con un push a `main` en `windows-latest`. El workflow toma el último tag `vX.Y.Z`, incrementa el patch, sincroniza package.json, Cargo.toml y tauri.conf.json, crea el commit/tag correspondiente y publica el MSI sin repetir la suite de pruebas del CI. Requiere permiso `contents:write` para actualizar `main`, crear el tag y publicar el Release. No se deben crear tags manuales para este flujo. Las ramas se validan mediante Pull Request, pero no publican releases. macOS continúa siendo compatible para desarrollo y pruebas locales, pero ya no se ejecuta en GitHub Actions.

El build publica .NET autocontenido win-x64 en archivo único con librerías nativas incluidas y Desktop Tauri sin su instalador individual. WiX 6 empaqueta ambos, registra LocalService con inicio automático y recuperación tras fallo, crea acceso en Inicio y soporta MajorUpgrade. Mantiene UpgradeCode estable y GUID de componentes. No borra el perfil de LocalService ni credenciales al desinstalar. No hay arranque automático de Desktop: abrir desde Inicio; cierre oculta en bandeja.

Prerequisito externo: Microsoft Edge WebView2 Runtime instalado. Este MSI no lo descarga ni lo incorpora. Verificarlo antes de instalar. El MSI es x64 y sin firma; no asumir que Windows SmartScreen confiará en él.

Configuración productiva del servicio: `launchSettings.json` solo aplica a `dotnet run` y no configura el servicio instalado. El MSI escribe por defecto el valor MULTI_SZ `Environment` en `HKLM\SYSTEM\CurrentControlSet\Services\VelvetAgent`; puede personalizarse durante la instalación con propiedades MSI y se conserva durante upgrades:

```text
VELVET_MASTER_URL=https://dev.thevelvetstudio.co
VELVET_STUDIO_ID=studio-manizales
VELVET_HEARTBEAT_SECONDS=30
VELVET_LOG_DIR=C:\ProgramData\VELVET Agent\logs
```

Para una estación con otra sede o Master, usar propiedades MSI (no incluir tokens):

```powershell
msiexec /i VelvetAgent.msi VELVET_MASTER_URL="https://dev.thevelvetstudio.co" VELVET_STUDIO_ID="studio-manizales" /qb
```

Las propiedades son seguras para la secuencia elevada del MSI, pero no son secretos. El administrador debe validar que la sede y la URL correspondan a la estación antes de instalar.

`VELVET_STUDIO_ID` debe corresponder a la sede real de la estación; no fijar una sede incorrecta en un MSI que se distribuirá a varias sedes. No poner tokens allí. Por defecto, los datos protegidos quedan en `C:\Windows\ServiceProfiles\LocalService\AppData\Local\VelvetAgent`; verificar la ruta efectiva y sus ACL. `VELVET_DATA_DIR` es opcional y debe tener permisos exclusivos para LocalService y administradores. No cambiar la identidad del servicio después del enrolamiento: DPAPI CurrentUser no se podrá descifrar bajo otra cuenta.

El MSI crea `C:\ProgramData\VELVET Agent\logs\` y concede a `NT AUTHORITY\LocalService` únicamente permisos de lectura y creación/escritura de archivos. El servicio crea logs JSON rotativos allí (o en `VELVET_LOG_DIR` si se define), conserva hasta 14 archivos y rota al superar aproximadamente 5 MB. Los logs no deben contener tokens, contraseñas, códigos OAuth ni `code_verifier`.

Enrolar abriendo Desktop como administrador. Validar acceso efectivo al pipe e ingresar un token de un solo uso generado por Master mediante `POST /api/agent/enroll`. Master devuelve una credencial permanente para la estación; el token temporal no se usa en cada inicio de sesión. Si el token es inválido, consumido o Master no está disponible, Desktop muestra el motivo mediante una notificación y el servicio lo registra sin exponer credenciales. La configuración se lee al iniciar el servicio; cambiar URL, sede o variables requiere reiniciarlo.

Después del enrolamiento, el login de la modelo usa OAuth PKCE. El Agent abre `/oauth/agent/authorize` con todos los parámetros requeridos y canjea el código en `/api/agent/oauth/token`. Cerrar sesión revoca únicamente la sesión OAuth y no debe borrar la credencial permanente ni exigir un nuevo enrolamiento. Un heartbeat sin sesión usa la credencial de dispositivo; un heartbeat que contiene `sessionId` usa el access token OAuth.

Firma futura: reservar secretos `WINDOWS_CERTIFICATE_BASE64`, `WINDOWS_CERTIFICATE_PASSWORD` y variable `WINDOWS_TIMESTAMP_URL` en un environment protegido de GitHub. No se consumen actualmente ni se presume certificado. Antes de habilitar firma, importar certificado temporalmente, firmar Service y Desktop con signtool SHA256 + timestamp HTTPS, construir MSI, firmarlo y verificar Authenticode y cadena del editor. Solo entonces cambiar nombre del artefacto unsigned. No incluir contraseñas en logs. SHA256SUMS actual no sustituye Authenticode.

## Matriz manual obligatoria

1. Instalar MSI elevado en Windows x64 limpio con WebView2; comprobar archivos y servicio LocalService.
2. Reiniciar Windows: servicio inicia sin Desktop ni sesión interactiva. Verificar heartbeat y UUID persistente.
3. Abrir Desktop estándar: leer estado real; el enrolamiento debe mostrar un error claro de permisos. Abrir elevado: enrolar con token válido; rechazar token caducado/consumido y revisar el log en `C:\ProgramData\VELVET Agent\logs\`.
4. `Stop-Service VelvetAgent`, verificar desconexión visible; `Start-Service VelvetAgent`, verificar reconexión automática.
5. Cortar/restaurar red: estado offline, reintentos sin secretos en logs, recuperación conservando UUID.
6. Revocar dispositivo en Master: 401/403, credencial eliminada y reenrolamiento explícito. Cerrar sesión OAuth en Master no debe borrar el enrolamiento.
7. Abrir OBS en usuario interactivo: detectar proceso, consultar escenas/streaming; rechazar autenticación pendiente. No cambiar escena ni transmitir automáticamente.
8. Probar pipe desde usuario remoto y rechazar; mensajes de versión inválida, desconocidos, grandes y lentos no deben ejecutar acciones.
9. Actualizar MSI con versión superior; preservar UUID/configuración/DPAPI y el valor `Environment`; impedir downgrade. Verificar que el servicio anterior se detiene y que `VelvetAgent.Service.exe` queda presente después del upgrade.
10. Desinstalar: servicio y archivos de programa desaparecen; datos operativos permanecen. Reinstalar: confirmar política de identidad persistente.
11. Probar reinicio tras fallo y permisos sobre datos bajo cuenta LocalService. Revisar logs del SCM y del instalador.

No hay evidencia de ejecución de esta matriz todavía. No etiquetar instalador como validado ni firmado.

Referencia de configuración de recuperación del servicio: https://docs.firegiant.com/wix/schema/util/serviceconfig/

WiX 6.0.2 se fija en el pipeline para reproducibilidad. Revisar sus condiciones de mantenimiento comercial antes de distribuir el instalador: https://www.nuget.org/packages/wix/6.0.2 (Open Source Maintenance Fee del proveedor).
