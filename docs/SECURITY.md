# Seguridad y amenazas

El MVP está en validación; no declarar despliegue productivo aprobado sin la matriz Windows y Master real.

| Amenaza | Control implementado / pendiente |
| --- | --- |
| Suplantación de estación | UUID no secreto y credencial independiente; MAC no autentica |
| Interceptación Master | HTTPS obligatorio; validación TLS del sistema sin bypass |
| Robo de credenciales locales | DPAPI CurrentUser bajo LocalService en Windows; Keychain en macOS; nunca token de dispositivo en React |
| Cliente IPC sin privilegios | ACL explícita, denegación Network, impersonación para enrolar, lectura no secreta permitida a usuarios autenticados |
| IPC malformado / bloqueo | protocolo v1, lista permitida, límites de tamaño y deadline; conexiones se procesan en serie (riesgo residual de disponibilidad local) |
| Shell remoto | sin endpoint ni operación shell; RefreshStatus es la única operación permitida |
| Repetición | idempotencia HTTP y ledger de comandos persistido antes del efecto; no hay recepción remota habilitada |
| Secretos en logs | logs JSON con tipo de fallo, sin cuerpos, tokens ni mensajes de excepciones HTTP |
| Modificación de configuración | Windows requiere protección del directorio de datos por identidad de servicio; URL no editable por usuario IPC |
| OBS en Session 0 | lectura WebSocket solo desde Desktop interactivo; sin iniciar/parar transmisión |
| Actualización maliciosa | no hay auto-updater; MSI manual. Hash SHA256 detecta alteraciones accidentales, no autentica origen; firma y validación de editor pendientes |

La identidad LocalService minimiza privilegios y mantiene DPAPI estable entre upgrades. Administradores del equipo son parte de la frontera de confianza; pueden cambiar binarios/configuración. Otros procesos ejecutados como LocalService comparten identidad DPAPI, por lo que se debe evaluar una cuenta virtual dedicada antes de producción si existen servicios no confiables. Validar ACL del directorio efectivo en Windows piloto. El MSI no modifica ACL de perfiles compartidos ni borra datos operativos.

En macOS el token de dispositivo se escribe en Keychain mediante `security -i` con stdin, sin incluirlo en argv. No se registra stdout/stderr del comando. Keychain puede pedir acceso del usuario. El modo desarrollo no escribe secretos. La autenticación OBS segura aún no está implementada: el adaptador rechaza el desafío; no recomienda desactivar autenticación.

No se recolectan cámaras, audio, pantallas, pulsaciones ni contraseñas de plataformas webcam. Los eventos deben limitarse a información técnica. Implementar retención de datos y auditoría central en Master. Una revocación elimina credencial local y detiene heartbeats reales hasta reenrolar.

Pendientes antes de producción: probar ACL con usuario estándar/elevado/remoto, DPAPI tras reinicio y upgrade, rotación de credenciales, persistencia del ledger y política de retención, límites de payload OBS, autenticación OBS segura, validación Authenticode de actualizaciones y auditoría externa del instalador.
