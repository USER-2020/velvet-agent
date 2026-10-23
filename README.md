# VELVET Agent

MVP de agente local para estaciones de THE VELVET STUDIO. Servicio .NET 10 independiente de Desktop Tauri 2 + React. **La API de Master es una propuesta; no existe integración productiva validada.**

La interfaz incluye Inicio, Monitoreo, OBS Studio, Configuración y Diagnósticos, con la marca de Master, Figtree local y tokens semánticos. Ver [sistema visual](docs/DESIGN_SYSTEM.md) y [manual de desarrollo local](docs/LOCAL_DEVELOPMENT.md).

## Requisitos y arranque en macOS

.NET 10 SDK, Node 24 LTS o posterior compatible con Vite, npm, Rust estable y Xcode Command Line Tools. Windows necesita WebView2 Runtime para Desktop.

```sh
dotnet restore
dotnet test
npm --prefix apps/desktop ci
npm --prefix apps/desktop run build
```

Terminal 1, desde la raíz:

```sh
VELVET_DEVELOPMENT=1 VELVET_DATA_DIR="$PWD/.data" dotnet run --project src/VelvetAgent.Service
```

Terminal 2:

```sh
cd apps/desktop
npm run tauri dev
```

El dashboard consulta el servicio cada cinco segundos y muestra explícitamente el adaptador de desarrollo. Cerrar la ventana la oculta en la bandeja; «Salir» termina solamente Desktop. `npm run dev` sirve la web para diseño, pero la conexión real al servicio requiere Tauri.

Para validar Desktop sin el servidor Vite:

```sh
npm --prefix apps/desktop run build
npm --prefix apps/desktop run tauri -- build --debug --no-bundle
./apps/desktop/src-tauri/target/debug/velvet-agent-desktop
```

No ejecutar simultáneamente dos instancias del servicio: comparten el nombre del pipe y el directorio de datos.

## Configuración

| Variable | Valor predeterminado | Función |
| --- | --- | --- |
| `VELVET_DEVELOPMENT` | ausente | `1` activa exclusivamente el adaptador local, sin enrolamiento ni credenciales reales |
| `VELVET_MASTER_URL` | `https://localhost` | URL HTTPS de Master; no admite usuario/contraseña en URL |
| `VELVET_HEARTBEAT_SECONDS` | `30` | Entre 5 y 3600 segundos |
| `VELVET_DATA_DIR` | LocalApplicationData/VelvetAgent | Identidad, configuración y credencial cifrada de Windows |

`configuration.json` se crea en el directorio de datos; las variables de URL e intervalo tienen precedencia. El modo desarrollo siempre exige la variable explícita y no se activa por el archivo. No incluir secretos en este JSON. Editar con el servicio detenido y reiniciar. La interfaz muestra esta configuración; el cambio de URL se realiza en el servicio, evitando que un usuario local redirija credenciales.

Para Master real, configurar `VELVET_MASTER_URL`, omitir `VELVET_DEVELOPMENT` y enrolar mediante Desktop. En Windows hay que abrir Desktop elevado para esta operación. El token de dispositivo nunca se devuelve a React. Un 401/403 revoca el estado local y elimina la credencial; se requiere nuevo enrolamiento.

En este entorno restringido se usaron `DOTNET_CLI_HOME=/tmp/velvet-dotnet`, `NUGET_PACKAGES=/tmp/velvet-nuget`, `CARGO_HOME=/tmp/velvet-cargo` y `npm --cache /tmp/velvet-npm`. No son requisitos del producto.

## Estructura real

```text
src/VelvetAgent.Core/             modelos y políticas sin dependencias de plataforma
src/VelvetAgent.Infrastructure/   HTTP, métricas, secretos, identidad y ejecutor restringido
src/VelvetAgent.Service/          Host, Worker, estado y servidor IPC
apps/desktop/src/                React y estilos
apps/desktop/src-tauri/          Rust, Named Pipe/Unix socket y consulta OBS
contracts/openapi/               contrato propuesto para Laravel
tests/VelvetAgent.Tests/          pruebas de políticas, HTTP, persistencia e IPC
installer/windows/              paquete WiX conjunto
.github/workflows/              CI macOS/Windows y release MSI
docs/                           arquitectura, seguridad, integración y despliegue
```

## Verificaciones

```sh
dotnet build --configuration Release
dotnet test --configuration Release
npm --prefix apps/desktop test
npm --prefix apps/desktop run check
npm --prefix apps/desktop run build
cargo fmt --manifest-path apps/desktop/src-tauri/Cargo.toml --check
cargo check --locked --manifest-path apps/desktop/src-tauri/Cargo.toml
```

CI ejecuta estas verificaciones y compila Tauri en macOS y Windows. El workflow de release publica Service win-x64 autocontenido y Desktop, y construye un único MSI WiX. No se ha ejecutado GitHub Actions desde esta sesión ni generado/instalado un MSI en Windows. Ver [despliegue](docs/DEPLOYMENT.md) y [evidencia local](docs/VALIDATION.md).

## Límites del MVP

- CPU es una muestra entre consultas; primera muestra `null`. RAM total y disco son reales. RAM disponible/utilización en macOS son `null`; no se inventan valores.
- OBS: el Service consulta el WebSocket local en cada heartbeat y Desktop actualiza el estado interactivo. Autenticación OBS pendiente; no desactivar una contraseña existente para producción.
- Autenticación de modelo: los contratos de sesión, estados de preparación y generación PKCE ya están en Core. El intercambio OAuth real espera las rutas de autorización de VELVET Master; el enrolamiento por token temporal continúa como compatibilidad.
- Master: enrolamiento, heartbeat, configuración, eventos y resultados tienen cliente/contrato. El Worker envía heartbeats; sincronización remota de configuración y envío de eventos operativos aún no están conectados al ciclo del Worker.
- Ejecutor permite únicamente `RefreshStatus`, valida expiración y reserva identificadores en disco antes del efecto. No hay canal de recepción de comandos ni Reverb habilitado. No se ejecutan shells ni operaciones de transmisión.
- No hay actualizador automático. Actualización manual por MSI, sin firma hasta disponer de certificado. La firma futura requiere el proceso documentado en despliegue.
- Keychain y DPAPI requieren validación funcional bajo la identidad real de producción; el adaptador de desarrollo no escribe secretos.
- Se necesita validar visualmente la ventana/bandeja en un escritorio interactivo, y validar instalación, permisos y persistencia bajo LocalService en Windows real.

Próximo paso: implementar los contratos en Laravel y ejecutar la matriz manual Windows en una estación piloto. No modificar VELVET Master desde este repositorio.
