# Arquitectura

Core define contratos sin HTTP, Windows ni Laravel. Infrastructure implementa persistencia, métricas, cliente Master y almacenamiento seguro. Service hospeda Worker y transporte local. Desktop contiene React y un puente Rust restringido, más el adaptador de lectura OBS que pertenece a la sesión interactiva.

Service sigue funcionando al cerrar Desktop. En producción corre como LocalService en Session 0. No inicia OBS ni accede a cámaras, micrófonos, pantallas o teclado. Los modelos/turnos y toda administración central pertenecen a Master.

IPC v1: una línea JSON UTF-8 por conexión, solicitud `Version`, `Operation`, `Token` opcional; respuesta `Version`, `Success`, `Data`, `Error`. Los nombres del IPC son PascalCase; HTTP usa camelCase. Operaciones: GetAgentStatus, GetSystemMetrics, GetObsStatus, GetConfiguration y RequestEnrollment. Service limita solicitudes a 16 KiB y diez segundos; Rust limita respuestas a 64 KiB y doce segundos. No registrar el cuerpo de solicitudes.

Windows: Named Pipe `velvet-agent-v1`, ACL explícita para LocalService, administradores y lectura/escritura de usuarios autenticados. Se deniega Network SID. Enrolamiento requiere token de impersonación con pertenencia efectiva a administradores; una cuenta admin sin elevar puede ser rechazada. Los usuarios estándar pueden leer estado y configuración no secreta. No existe comando local de shell, escritura de URL o lectura de credenciales.

macOS: NamedPipe de .NET se implementa como Unix socket `CoreFxPipe_velvet-agent-v1` en el directorio temporal; CurrentUserOnly limita permisos al usuario. Rust conecta al mismo archivo. Service y Desktop deben compartir usuario y TMPDIR. No hay servidor TCP local.

La identidad UUID se conserva en identity.json. Se asume un solo proceso Service; SCM lo garantiza en Windows. No ejecutar dos consolas sobre los mismos datos. Escritura de identidad/configuración mediante archivo temporal y rename. El ejecutor persiste comandos antes de aplicarlos; tras caída puede quedar un resultado reservado no concluyente, que se debe reconciliar con Master. El ejecutor es infraestructura preparada, sin recepción remota habilitada. La bitácora de comandos todavía requiere una política de retención antes de habilitar entrega continua.

Heartbeats: intervalo configurable 30 s, timeout HTTP 15 s por intento, tres reintentos adicionales para transporte/timeout/5xx/429 con backoff exponencial y jitter. Los rechazos 4xx no se reintentan; 401/403 exigen reenrolar. Cada lote conserva Idempotency-Key entre reintentos. Una desconexión conserva la identidad/credencial y el Worker vuelve a intentar. Última sincronización es la del último envío confirmado.

Reverb futuro: WSS con canales privados `private-agent.{installationId}`, autorización de dispositivo por Master y eventos con AgentCommand. Implementar IRealtimeCommandSource; mantener comandos expirables, política local y confirmaciones HTTP. No conectar automáticamente a canales públicos. Renovación de autenticación, reconexión y reconciliación deben verificarse antes de habilitar este transporte.

OBS se consulta según el protocolo oficial obs-websocket v5: https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md. El adaptador Rust hace Hello/Identify/Identified y GetStreamStatus/GetSceneList. Actualmente solo conexiones loopback sin autenticación, sin cambios automáticos. La abstracción IObsController del servicio devuelve detección de proceso y explica que WebSocket corresponde a Desktop.
