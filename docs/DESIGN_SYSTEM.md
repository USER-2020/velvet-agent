# Sistema visual de VELVET Agent

## Auditoría y origen

Se inspeccionó `../app_velvet` exclusivamente en lectura. No se modificó ni se conectó su backend. Se revisaron `resources/css/app.css`, `tailwind.config.js`, `resources/views/app.blade.php`, BrandMark.jsx, ApplicationLogo.jsx y los recursos públicos.

Master define `--velvet-purple: #883499`, `--velvet-magenta: #c23bea`, `--velvet-ink: #f7f1fb`, `--velvet-paper: #090a10`, `--velvet-surface: #11121a`, `--velvet-border: #292936`. Se reutilizan papel, tinta, superficie, borde y púrpura. El magenta no se incorpora como acento general para evitar saturación. `LOGO_OVERLAY.svg`, usado por Master en app.jsx, se copia sin modificaciones a `apps/desktop/public/brand/velvet-studio.svg`. No se usa ApplicationLogo.jsx porque corresponde al símbolo de Laravel del scaffold. No se inventa una nueva marca.

Tipografía: Figtree 400/500/600 distribuida localmente con @fontsource, sin petición a proveedores externos. Títulos editoriales Georgia como Master. Licencia Figtree SIL OFL en `docs/licenses/Figtree-OFL.txt`.

## Tokens y componentes

`apps/desktop/src/tokens.css` centraliza colores semánticos, tipografía, espaciado, radios, sombras y transición de 180 ms. Los colores de estado, superficie elevada y matices suaves son decisiones de Agent: sustituirlos allí si se recibe un design system oficial completo. Radios/sombras se ajustaron a Desktop; no se presentan como tokens oficiales de Master. Los estilos están en style.css; se conservó CSS nativo para evitar una segunda capa de estilos o migración innecesaria a Tailwind.

Componentes: AppShell, Sidebar, TopBar, StationIdentity, StatusIndicator, MetricCard, ConnectionStatus, DeviceInfo, ObsStatusCard, SettingsSection, EmptyState, ErrorState, LoadingState, ConfirmDialog, ToastNotification y LogViewer. App.tsx coordina las cinco vistas; useAgent maneja polling y transiciones; lib/agent.ts valida respuestas y prepara diagnósticos por lista permitida.

Ventana inicial 1100 × 720, mínimo 850 × 600. Sidebar compacta automática bajo 1000 px y con control manual por encima. El panel principal se desplaza verticalmente. Se respeta reduced-motion; foco visible, navegación por teclado, labels, diálogo nativo modal y regiones de estado. No hay gráficos históricos porque el servicio no entrega series históricas.

## Marca nativa

PNG e ICO derivan del SVG oficial, conservando el viewBox y proporciones, sobre lienzo transparente con margen. La bandeja reutiliza el icono nativo de la app; se eliminó el cuadrado dorado provisional. El recurso completo puede perder legibilidad en tamaños de bandeja: incorporar una variante oficial compacta cuando el equipo de marca la suministre; no redibujar el símbolo. El script `apps/desktop/brand-icons.mjs` permite generar PNG e ICO con Chrome; el ICO se empaqueta con el mismo PNG de 256 px. El instalador actual no contiene un splash ni una marca alternativa.

## Datos y privacidad

Cada métrica procede del servicio. Si este se desconecta, los datos conservados se señalan como última lectura; Master nunca se presenta conectado cuando la respuesta corresponde al adaptador de desarrollo. Los eventos de diagnósticos son transiciones observadas por Desktop durante la sesión, no un acceso a los logs internos de Service. La reconexión se comprueba cada cinco segundos sin solapar solicitudes de actualización.

La exportación excluye por lista permitida las credenciales, la configuración completa, URL Master, hostname e installationId. El token temporal de enrolamiento solo existe en el campo de entrada en memoria, se oculta, no se persiste y se borra tras confirmar, cancelar o navegar. Las credenciales de dispositivo nunca llegan a React. El enrolamiento exige confirmación y mantiene la autorización del servidor IPC existente.

Preferencias: las notificaciones informativas se guardan localmente; los errores operativos siguen visibles. Cerrar ventana conserva el comportamiento real de bandeja. El inicio automático de Desktop se identifica como no configurado, sin un interruptor ficticio. La URL se muestra en solo lectura y se administra en Service. Controles OBS de escritura y autenticación de OBS siguen pendientes explícitos.

## Validación de esta iteración

Pruebas automatizadas React: validación de contratos, distinción de conectividad, privacidad de exportación, navegación, enrolamiento deshabilitado en desarrollo, confirmación antes de enviar token y limpieza posterior, fallo/reconexión y fallo de OBS. .NET conserva sus 19 pruebas aprobadas y una omitida que requiere DPAPI Windows.

QA visual con Chrome local: datos reales del Unix socket mediante binding de Playwright exclusivamente de lectura. No se añadió una API HTTP al producto ni un adaptador simulado. Se verificaron las cinco vistas, 1100×720, 850×600 y 1280×800 con DPR 1.5, ausencia de desbordamiento horizontal, exportación sanitizada, fallo y recuperación de conexión, sin errores JavaScript. La prueba de desconexión controla el puente de QA; no corta red ni mata servicios del usuario. Capturas en artifacts/ui (ignoradas por Git), a escala CSS. Se revisaron visualmente las capturas de Inicio y Configuración; el resto tiene comprobación automatizada.

Pendiente: inspección visual nativa en Windows a 100/125/150/200 %, lector de pantalla, bandeja en ambos temas del sistema, enrolamiento elevado real y OBS real/autenticado. No se han ejecutado GitHub Actions ni el MSI durante este cambio.
