# Integración pendiente con VELVET Master

Los endpoints siguientes son propuestos, no se presume que existen. Contrato completo: `contracts/openapi/agent.openapi.json`. JSON camelCase. HTTPS obligatorio. No enviar secretos a logs, telemetría ni eventos Reverb.

| Método/ruta | Controlador sugerido | Permiso/resultado |
| --- | --- | --- |
| POST /api/agent/enroll | AgentEnrollmentController@store | token temporal de un uso; devuelve deviceToken una sola vez |
| POST /api/agent/heartbeat | AgentHeartbeatController@store | agent:heartbeat; 204 |
| GET /api/agent/configuration | AgentConfigurationController@show | agent:configuration; 200 con configuración |
| POST /api/agent/events | AgentEventController@store | agent:events; 204 |
| POST /api/agent/commands/{id}/result | AgentCommandResultController@store | agent:results; 204 |

Modelos/migraciones sugeridos: AgentStation (installation_id único, hostname, studio_id, versión, last_seen_at, revoked_at); AgentEnrollmentToken (hash, studio_id, expires_at, used_at, issued_by); credenciales de dispositivo con hashes y abilities restringidas (Sanctum o mecanismo equivalente); AgentHeartbeat (UUID único, station_id, métricas JSON, sent_at, received_at); AgentConfiguration (station_id, configuración versionada); AgentEvent (UUID único, station_id, tipo, mensaje saneado); AgentCommand (UUID único, estación, operación, expires_at, estado, actor, resultado); AgentIdempotencyKey (estación/ruta/clave únicos, hash del cuerpo, resultado protegido y TTL).

Enrolamiento: emitir token temporal desde un controlador administrativo protegido por permiso `stations.enroll`; almacenar hash, vincular al estudio y caducidad. Dentro de transacción bloquear token, validar que no esté consumido y hacer upsert de estación sin aceptar asociación arbitraria a estudios/modelos. Generar credencial aleatoria criptográfica y devolverla solo por TLS. Para reintentos de enrolamiento con misma instalación y token, devolver respuesta idempotente cifrada por una ventana corta; nunca consumir otro token por la misma clave ni devolver credenciales de una inscripción anterior a un token diferente. Conflictos de cuerpo bajo misma clave: 409.

Middleware de dispositivo: comprobar credencial, ability, estación activa y revocación en cada solicitud. La instalación del heartbeat debe coincidir con el dispositivo autenticado; no confiar en el UUID del cuerpo. Rate limiting por estación e IP. Validar longitud, precisión de tiempos y tamaño máximo del payload. 401/403 se consideran revocación en Agent; 429 y 5xx se reintentan.

Idempotencia: exigir Idempotency-Key en POST. No duplicar heartbeats, eventos ni resultados. Agent deriva la clave de enrolamiento de installationId y SHA256 del token temporal para mantenerla estable en reintentos y distinta al reenrolar con otro token. Ruta de resultados debe pertenecer a la estación, UUID del cuerpo debe coincidir con la URL. Un resultado terminal ya registrado no se sobrescribe por una repetición; conciliar reservas desconocidas sin reejecutar.

Comandos: solo RefreshStatus en MVP; Master también debe validar permisos administrativos y expiración. No hay entrega implementada en Agent. No crear endpoints de shell remoto. Operaciones OBS de escritura quedan fuera del MVP y requieren permisos específicos y futura validación local. Asociación modelo/turno se conserva en Master y no exige replicar módulos administrativos.

Eventos futuros Laravel: AgentEnrolled, AgentHeartbeatReceived, AgentWentOffline, AgentRevoked, AgentCommandRequested y AgentCommandCompleted. Jobs calculan offline con tolerancia al intervalo. Reverb usa WSS, canal privado `private-agent.{installationId}` y endpoint de autorización de broadcasting con identidad de dispositivo. La autorización debe comparar estación y token, nunca usar un secreto compartido global. Añadir versiones y correlación; reenvíos se filtran por UUID persistente y expiración. El consumidor Reverb todavía no está habilitado en Agent.

Prueba de integración requerida: token válido/consumido/expirado; repetición del enrolamiento tras pérdida de respuesta; aislamiento entre estaciones; revocación; validación de métricas nulas; idempotencia; backoff y recuperación; comandos expirados y no permitidos. No se ha modificado ningún repositorio Master.
