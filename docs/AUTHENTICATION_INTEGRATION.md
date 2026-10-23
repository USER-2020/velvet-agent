# Autenticación de usuario en VELVET Agent

El Agent mantiene dos identidades independientes:

- **Estación:** identidad persistente del computador y credencial usada por el servicio para heartbeats.
- **Usuario:** sesión temporal de la modelo o administradora, autorizada por VELVET Master.

El flujo previsto para Master es OAuth 2.1 Authorization Code + PKCE:

1. Desktop genera `state` y `code_verifier` y abre el navegador del sistema.
2. Master autentica al usuario y devuelve un código de un solo uso mediante loopback local.
3. Agent intercambia el código por tokens usando TLS.
4. El refresh token se guarda en Keychain/DPAPI; nunca se expone a React ni se escribe en logs.
5. Master vincula la sesión de usuario con la estación y sus permisos.

Master debe publicar y documentar antes de activar el cliente real:

```text
GET  /oauth/agent/authorize
POST /oauth/agent/token
POST /oauth/agent/revoke
GET  /api/agent/me
GET  /api/agent/station
GET  /api/agent/station/configuration
POST /api/agent/session/start
POST /api/agent/session/end
```

El Agent no implementa un segundo formulario de contraseña. El enrolamiento por token temporal se conserva como compatibilidad de estación hasta que Master publique OAuth.
