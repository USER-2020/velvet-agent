# Desarrollo local en macOS Apple Silicon

## Requisitos

.NET 10 SDK, Node 24 LTS o versión compatible con Vite, npm, Rust estable y Xcode Command Line Tools. Para QA visual opcional: Google Chrome instalado. No se requieren tokens de Master para el modo de desarrollo.

Desde la raíz:

```sh
dotnet restore
npm --prefix apps/desktop ci
```

Terminal 1:

```sh
./scripts/dev-service.sh
```

Terminal 2:

```sh
./scripts/dev-desktop.sh
```

El primer script activa explícitamente VELVET_DEVELOPMENT=1 y usa .data (ignorada por Git). La identidad/configuración se conservan entre arranques. No ejecutar dos instancias del servicio simultáneamente. Service y Desktop deben usar el mismo usuario y TMPDIR para compartir el socket Unix de .NET. En Windows el transporte sigue siendo Named Pipe con ACL y autorización.

La ventana muestra métricas reales y señala «Modo de desarrollo». No realiza enrolamiento ni comunica con Master real. Puedes detener Service y observar cómo Desktop marca la última lectura, registra el fallo y se recupera al iniciarlo otra vez. Cerrar la ventana la oculta en bandeja; para terminar Desktop usa «Salir» desde su menú.

Para producción/pruebas con Master, ejecutar Service sin VELVET_DEVELOPMENT y con VELVET_MASTER_URL=https://tu-master; los contratos Laravel deben estar implementados primero. La URL es de solo lectura en la interfaz; no se muestran credenciales de dispositivo.

## Verificaciones

```sh
dotnet build --configuration Release
dotnet test --configuration Release
npm --prefix apps/desktop test
npm --prefix apps/desktop run check
npm --prefix apps/desktop run build
cargo fmt --manifest-path apps/desktop/src-tauri/Cargo.toml --check
cargo check --locked --manifest-path apps/desktop/src-tauri/Cargo.toml
npm --prefix apps/desktop run tauri -- build --debug --no-bundle
```

Para aplicar/comprobar formato: `npm --prefix apps/desktop run format` / `npm --prefix apps/desktop run format:check`.

## Capturas y QA con datos reales

Mantén Service activo. En otra terminal:

```sh
cd apps/desktop
npm run dev -- --port 5187 --strictPort
```

Luego, desde apps/desktop:

```sh
node visual-check.mjs
```

El script usa Chrome headless y un binding Playwright de lectura hacia el socket real. Esto es una herramienta de QA, no forma parte de la aplicación ni expone un servidor administrativo. Genera capturas y diagnóstico sanitizado en artifacts/ui. Si usas otro puerto, define VELVET_PREVIEW_URL=http://127.0.0.1:PUERTO. Comprueba ventanas 1100×720, 850×600 y 1280×800 con DPR 1.5, exportación y desconexión/reconexión controlada en el puente. No usa métricas ficticias. Este script requiere macOS y Chrome; no se ejecuta en CI Windows.

Abrir Vite directamente en navegador sin ese puente muestra servicio no disponible: el producto se comunica mediante Tauri, no mediante HTTP desde React.

## Entornos restringidos

En esta sesión se usaron cachés temporales:

```sh
export DOTNET_CLI_HOME=/tmp/velvet-dotnet
export NUGET_PACKAGES=/tmp/velvet-nuget
export CARGO_HOME=/tmp/velvet-cargo
```

Son opcionales y no cambian HOME. El sandbox puede requerir permisos adicionales para restaurar paquetes, abrir sockets locales o arrancar GUI. Si 5173 está ocupado, detener únicamente tu propio proceso de desarrollo o liberar ese puerto antes de `tauri dev`, que exige el puerto fijo. Para QA se usa 5187 sin alterar procesos existentes.

## Limitaciones

RAM disponible/uso en macOS puede ser desconocida. OBS informa instalación no disponible y distingue proceso de conexión WebSocket; solo admite consulta local sin contraseña por ahora. No hay controles de transmisión habilitados. Los diagnósticos no incluyen logs internos del servicio ni historiales de sesiones anteriores. Arranque automático de Desktop aún no está configurado. Validación manual Windows pendiente en DEPLOYMENT.md; procedencia visual y tokens en DESIGN_SYSTEM.md.
