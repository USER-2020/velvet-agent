# Validación local — 21 de octubre de 2026

Entorno: macOS Apple Silicon, .NET SDK 10.0.402, Node 26.11.1, Cargo 1.99.1. Dependencias npm y Cargo resueltas con lockfiles incluidos.

| Comprobación | Resultado observado |
| --- | --- |
| dotnet restore | correcto usando NuGet.Config del repositorio |
| dotnet build -c Release | correcto, 0 advertencias y 0 errores |
| dotnet test -c Release --logger trx | 19 aprobadas, 1 omitida (DPAPI exige Windows), 0 fallos |
| npm ci/install | lockfile generado; auditoría npm: 0 vulnerabilidades en ese momento |
| npm run check | correcto, TypeScript estricto |
| npm run build | correcto, Vite genera dist |
| cargo fmt --check | correcto |
| cargo check --locked | correcto en macOS ARM64 |
| cargo build --locked | correcto |
| npm run tauri build -- --debug --no-bundle | correcto, frontend integrado en ejecutable |
| Arranque de Desktop | proceso vivo cuatro segundos, stderr vacío; terminado después de la comprobación |
| Consola Service | inició, confirmó varios heartbeats de desarrollo y cerró ordenadamente con Ctrl+C |
| Consulta IPC externa | respuesta GetAgentStatus con identidad UUID, modo development-adapter, CPU real, RAM total real y disco real |
| Named Pipe en pruebas | intercambio de solicitud/respuesta JSON v1 verificado |
| dotnet publish win-x64 autocontenido/single-file | correcto; ejecutable generado en artifacts/service/VelvetAgent.Service.exe; no ejecutado en Windows |
| GitHub Actions | archivos creados; no ejecutados en GitHub |
| MSI Windows | fuente WiX preparada; no compilado ni instalado en Windows |
| Master real / OBS real | sin backend/credenciales ni sesión OBS disponible para validar |

Comandos de entorno restringido usados:

```sh
DOTNET_CLI_HOME=/tmp/velvet-dotnet NUGET_PACKAGES=/tmp/velvet-nuget dotnet restore --configfile NuGet.Config
DOTNET_CLI_HOME=/tmp/velvet-dotnet NUGET_PACKAGES=/tmp/velvet-nuget dotnet build --no-restore -c Release -m:1
DOTNET_CLI_HOME=/tmp/velvet-dotnet NUGET_PACKAGES=/tmp/velvet-nuget dotnet test --no-restore -c Release -m:1 --logger trx
DOTNET_CLI_HOME=/tmp/velvet-dotnet NUGET_PACKAGES=/tmp/velvet-nuget dotnet publish src/VelvetAgent.Service -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o artifacts/service -m:1
npm install --prefix apps/desktop --cache /tmp/velvet-npm
npm --prefix apps/desktop run check
npm --prefix apps/desktop run build
CARGO_HOME=/tmp/velvet-cargo cargo fmt --manifest-path apps/desktop/src-tauri/Cargo.toml --check
CARGO_HOME=/tmp/velvet-cargo cargo check --locked --manifest-path apps/desktop/src-tauri/Cargo.toml
CARGO_HOME=/tmp/velvet-cargo npm --prefix apps/desktop run tauri -- build --debug --no-bundle
VELVET_DEVELOPMENT=1 VELVET_HEARTBEAT_SECONDS=5 VELVET_DATA_DIR=/tmp/velvet-smoke dotnet run --no-build --project src/VelvetAgent.Service
```

La primera restauración dentro del sandbox no pudo leer la configuración global de NuGet. Se usó un HOME de CLI y caché temporales sin modificar HOME del usuario. Las verificaciones que requieren descarga, sockets locales o arranque GUI se ejecutaron con los permisos aprobados por el entorno.

Cobertura: identidad persistente, serialización, configuración insegura y persistida, backoff, HTTP 400/401/503, idempotencia entre reintentos, heartbeat de desarrollo, política y expiración de comandos, repetición persistente tras recrear ejecutor, métricas y comunicación local. DPAPI queda para Windows CI. No se afirma que la GUI fue inspeccionada visualmente ni que se validaron permisos/instalación Windows. Ver matriz manual en DEPLOYMENT.md.

## Iteración visual y design system

Se incorporaron cinco vistas, componentes reutilizables, tokens derivados del Master local, su SVG sin modificar (SHA256 idéntico), Figtree local y Lucide. No se modificó el repositorio Master.

- React/Vitest: **8 pruebas aprobadas**; contratos, privacidad, estados de conexión, navegación, bloqueo de enrolamiento en desarrollo, confirmación/limpieza de token, reconexión y errores OBS.
- .NET Release: **19 aprobadas, 1 omitida (DPAPI Windows)**; el servicio y sus contratos se conservan.
- TypeScript estricto, Vite build, Prettier y rustfmt: correctos.
- Cargo check y `npm run tauri build -- --debug --no-bundle`: correctos con el nuevo frontend, fuentes y marca nativa.
- Playwright/Chrome: cinco vistas, tamaños 850×600, 1100×720 y 1280×800 con DPR 1.5; sin desbordamiento horizontal ni errores JS; datos obtenidos del socket real, exportación y reconexión comprobadas. Se controló la desconexión únicamente en el puente de QA, sin interrumpir servicios del usuario.
- Capturas: `artifacts/ui/`; revisadas visualmente Inicio (1100 y 850) y Configuración. Las capturas a escala CSS evitan artefactos de Chrome al redimensionar con DPI fraccionario.
- La inspección del frontend solo encontró el campo password del token temporal de enrolamiento, sin credenciales de dispositivo. La exportación usa lista permitida.
- Vite avisa que ignora las directivas `use client` de Lucide; los builds finalizan correctamente.

No se han ejecutado GitHub Actions, generado un MSI nuevo ni validado Windows real en esta iteración. También quedan pendientes OBS autenticado, inicio automático de Desktop, acceso a logs internos de Service y pruebas manuales de accesibilidad/DPI nativas. Ver LOCAL_DEVELOPMENT.md y DESIGN_SYSTEM.md.

Arranque nativo posterior al build visual: ejecutable Tauri activo durante cuatro segundos, sin salida stderr; terminado después de la comprobación. Service y Vite iniciados para QA se cerraron; el proceso preexistente en 5173 no se modificó.
