import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Activity,
  ArrowDownToLine,
  ArrowUpRight,
  Cpu,
  Database,
  HardDrive,
  RefreshCw,
  ShieldCheck,
  Wifi,
} from 'lucide-react';
import { AppShell, Page, Sidebar, TopBar } from './components/AppShell';
import {
  ConfirmDialog,
  DeviceInfo,
  EmptyState,
  ErrorState,
  LoadingState,
  LogViewer,
  MetricCard,
  SettingsSection,
  StatusIndicator,
  ToastNotification,
} from './components/ui';
import { useAgent } from './hooks/useAgent';
import {
  bytes,
  connectionLabel,
  date,
  diagnostics,
  enroll,
  inspectObs,
  loginWithMaster,
  Obs,
  reportObsStatus,
  setObsStreaming,
  Status,
} from './lib/agent';
const titles: Record<Page, [string, string]> = {
  dashboard: [
    'Tu estación, en perspectiva.',
    'El estado de tu equipo y sus conexiones, en un solo lugar.',
  ],
  monitor: [
    'Precisión en cada detalle.',
    'Información técnica obtenida directamente del servicio local.',
  ],
  obs: [
    'Tu entorno de transmisión.',
    'Diagnóstico de OBS Studio en tu sesión de escritorio.',
  ],
  settings: [
    'Todo en su lugar.',
    'Conexión, identidad y preferencias de esta instalación.',
  ],
  diagnostics: [
    'Claridad para resolver.',
    'Comprueba la comunicación y consulta los eventos de esta sesión.',
  ],
};
function preference(key: string, fallback: boolean) {
  try {
    const v = localStorage.getItem(key);
    return v === null ? fallback : v === 'true';
  } catch {
    return fallback;
  }
}
export function StationIdentity({ status }: { status: Status | null }) {
  return (
    <div className="station-identity">
      <span className="eyebrow">IDENTIDAD DE ESTACIÓN</span>
      <h2>{status?.Identity.Hostname ?? 'Estación local'}</h2>
      <span className="device-id">
        {status?.Identity.InstallationId ?? 'Identidad pendiente de conexión'}
      </span>
    </div>
  );
}
export function ConnectionStatus({
  status,
  connected,
}: {
  status: Status | null;
  connected: boolean;
}) {
  const label = connectionLabel(status, connected);
  return (
    <StatusIndicator
      label={label}
      tone={
        !connected
          ? 'danger'
          : status?.Mode === 'development-adapter' || !status?.MasterConnected
            ? 'warning'
            : 'success'
      }
    />
  );
}
export function ObsStatusCard({
  status,
  connected,
  onOpen,
}: {
  status: Status | null;
  connected: boolean;
  onOpen: () => void;
}) {
  return (
    <section className="panel obs-summary">
      <div className="panel-title">
        <h2>OBS Studio</h2>
        <StatusIndicator
          label={
            !connected
              ? 'Sin lectura'
              : status?.Obs.ProcessRunning
                ? 'En ejecución'
                : 'No detectado'
          }
          tone={connected && status?.Obs.ProcessRunning ? 'success' : 'neutral'}
        />
      </div>
      <p>La integración se consulta en tu sesión de escritorio.</p>
      <button className="text-button" onClick={onOpen}>
        Abrir diagnóstico OBS <ArrowUpRight size={15} />
      </button>
    </section>
  );
}
export default function App() {
  const agent = useAgent();
  const {
    status,
    config,
    connected,
    loading,
    refreshing,
    refresh,
    logs,
    reconnections,
  } = agent;
  const startupGate = useRef(true);
  const [page, setPage] = useState<Page>('dashboard'),
    [compact, setCompact] = useState(false),
    [toast, setToast] = useState(''),
    [obs, setObs] = useState<Obs | null>(null),
    [obsAt, setObsAt] = useState<string | null>(null),
    [obsError, setObsError] = useState(false),
    [busy, setBusy] = useState(false),
    [token, setToken] = useState(''),
    [confirm, setConfirm] = useState(false),
    [notifications, setNotifications] = useState(() =>
      preference('velvet.notifications', true),
    );
  const closeToast = useCallback(() => setToast(''), []);
  const metric = status?.Metrics;
  const readOnly = !connected;
  const canConfigure = status?.ConfigurationAvailable === true;
  const sync = () => void refresh();
  function navigate(next: Page) {
    if (next === 'settings' && !canConfigure) return;
    setPage(next);
    setToken('');
  }
  async function queryObs(background = false) {
    if (!background) setBusy(true);
    setObsError(false);
    try {
      const fresh = await inspectObs();
      setObs(fresh);
      try {
        await reportObsStatus(fresh);
      } catch {
        // OBS diagnostics remain usable if the local service is restarting.
      }
      setObsAt(new Date().toISOString());
    } catch {
      setObs(null);
      setObsAt(null);
      setObsError(true);
    } finally {
      if (!background) setBusy(false);
    }
  }
  async function toggleObsStreaming() {
    if (!obs?.WebSocketConnected || obs.Streaming == null) return;
    const start = !obs.Streaming;
    if (
      start &&
      !window.confirm('¿Quieres iniciar la transmisión configurada en OBS?')
    )
      return;
    setBusy(true);
    setObsError(false);
    try {
      await setObsStreaming(start);
      await queryObs(true);
    } catch {
      setObsError(true);
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    if (page !== 'obs') return;
    void queryObs(true);
    const timer = window.setInterval(() => void queryObs(true), 5000);
    return () => window.clearInterval(timer);
  }, [page]);
  useEffect(() => {
    if (!connected || !status) return;
    const requiresLogin = status.Mode === 'master-api';
    if (requiresLogin && !status.UserAuthenticated) {
      setPage('settings');
      return;
    }
    if (page === 'settings' && !status.ConfigurationAvailable) {
      setPage('dashboard');
      return;
    }
    if (startupGate.current) {
      setPage('dashboard');
      startupGate.current = false;
    }
  }, [connected, status]);
  async function confirmEnrollment() {
    setBusy(true);
    try {
      await enroll(token);
      if (notifications) setToast('Estación enrolada correctamente.');
      await refresh();
    } catch {
      setToast('No se pudo enrolar. Comprueba token, permisos y conexión.');
    } finally {
      setToken('');
      setConfirm(false);
      setBusy(false);
    }
  }
  async function loginMaster() {
    if (!config || !status) return;
    setBusy(true);
    try {
      await loginWithMaster(
        config,
        status.Identity.InstallationId,
        status.Identity.Hostname,
      );
      setToast('Sesión de VELVET Master iniciada.');
      await refresh();
      setPage('dashboard');
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      setToast(
        message.includes('OAuth en curso')
          ? 'Ya hay un inicio de sesión en curso. Completa la ventana de VELVET Master.'
          : 'No se pudo iniciar sesión con VELVET Master.',
      );
    } finally {
      setBusy(false);
    }
  }
  function exportDiagnostics() {
    const blob = new Blob(
      [JSON.stringify(diagnostics(status, connected, logs), null, 2)],
      { type: 'application/json' },
    );
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'velvet-agent-diagnostics.json';
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    if (notifications)
      setToast(
        'Diagnóstico técnico exportado sin credenciales ni identificadores del equipo.',
      );
  }
  function toggleNotifications() {
    setNotifications((v) => {
      try {
        localStorage.setItem('velvet.notifications', String(!v));
      } catch {
        setToast('No se pudo guardar la preferencia en este equipo.');
      }
      return !v;
    });
  }
  const authRequired =
    !loading &&
    status?.Mode === 'master-api' &&
    status.UserAuthenticated === false;
  if (loading && !status) {
    return (
      <div className="auth-gate" role="status" aria-live="polite">
        <div className="auth-gate__card">
          <img src="/brand/velvet-studio.svg" alt="The Velvet Studio" />
          <span className="eyebrow">VELVET AGENT</span>
          <p>Preparando tu estación…</p>
        </div>
      </div>
    );
  }
  if (authRequired) {
    return (
      <div className="auth-gate">
        <div className="auth-gate__card">
          <div className="auth-gate__icon" aria-hidden="true">
            <ShieldCheck size={25} />
          </div>
          <img src="/brand/velvet-studio.svg" alt="The Velvet Studio" />
          <span className="eyebrow">VELVET AGENT</span>
          <h1>Bienvenida a VELVET</h1>
          <p>Inicia sesión para preparar tu espacio de transmisión.</p>
          <button
            className="button auth-gate__button"
            type="button"
            onClick={() => void loginMaster()}
            disabled={!connected || !config?.StudioId || busy}
          >
            <ArrowUpRight size={16} />
            {busy
              ? 'Abriendo VELVET Master…'
              : 'Iniciar sesión con VELVET Master'}
          </button>
          {!connected && <small>Esperando al servicio local…</small>}
          {!config?.StudioId && connected && (
            <small>Falta configurar VELVET_STUDIO_ID en el servicio.</small>
          )}
          <footer>Estación {status.Identity.Hostname} · Acceso seguro</footer>
        </div>
      </div>
    );
  }
  return (
    <AppShell
      compact={compact}
      sidebar={
        <Sidebar
          page={page}
          onNavigate={navigate}
          compact={compact}
          onCompact={() => setCompact((v) => !v)}
          version={status?.Version ?? '0.1.0'}
          canConfigure={canConfigure}
          userRole={status?.UserRole}
        />
      }
      topbar={
        <TopBar
          hostname={status?.Identity.Hostname ?? 'Estación local'}
          userRole={status?.UserRole}
          connected={connected}
          loading={loading}
        />
      }
    >
      <div className="page-heading">
        <div>
          <span className="eyebrow">
            VELVET AGENT /{' '}
            {
              {
                dashboard: 'INICIO',
                monitor: 'MONITOREO',
                obs: 'OBS STUDIO',
                settings: 'CONFIGURACIÓN',
                diagnostics: 'DIAGNÓSTICOS',
              }[page]
            }
          </span>
          <h1>{titles[page][0]}</h1>
          <p>{titles[page][1]}</p>
        </div>
        <button
          className="button button--ghost refresh-button"
          disabled={refreshing}
          onClick={sync}
          aria-label="Actualizar estado"
        >
          <RefreshCw size={15} className={refreshing ? 'is-spinning' : ''} />
          <span>Actualizar</span>
        </button>
      </div>
      {!loading && !connected && (
        <ErrorState onRetry={sync} busy={refreshing} />
      )}
      {status?.Mode === 'development-adapter' && (
        <div className="development-note">
          <span className="status-dot" />
          Entorno de desarrollo
          <span>Adaptador local · sin conexión real a Master</span>
        </div>
      )}
      {loading ? (
        <LoadingState />
      ) : (
        <>
          {page === 'dashboard' && (
            <>
              <section className="station-panel panel">
                <StationIdentity status={status} />
                <div className="station-connection">
                  <ConnectionStatus status={status} connected={connected} />
                  <p>
                    Último heartbeat <strong>{date(status?.LastSync)}</strong>
                  </p>
                </div>
              </section>
              <div
                className={`metrics-grid ${readOnly ? 'is-stale' : ''}`}
                aria-label={
                  readOnly ? 'Última lectura del equipo' : 'Métricas del equipo'
                }
              >
                <MetricCard
                  label="CPU"
                  icon={<Cpu size={17} />}
                  value={
                    metric?.CpuPercent == null
                      ? '—'
                      : `${metric.CpuPercent.toFixed(1)} %`
                  }
                  detail={`${metric?.CpuCount ?? '—'} procesadores lógicos`}
                  percent={metric?.CpuPercent}
                />
                <MetricCard
                  label="MEMORIA RAM"
                  icon={<Database size={17} />}
                  value={bytes(metric?.RamTotalBytes)}
                  detail={`Disponible: ${bytes(metric?.RamAvailableBytes)}`}
                  percent={metric?.RamUsedPercent}
                />
                <MetricCard
                  label="DISCO LIBRE"
                  icon={<HardDrive size={17} />}
                  value={bytes(metric?.DiskAvailableBytes)}
                  detail="Volumen de instalación"
                />
                <MetricCard
                  label="TIEMPO ACTIVO"
                  icon={<Activity size={17} />}
                  value={
                    status
                      ? `${Math.floor(status.UptimeSeconds / 3600)} h ${Math.floor((status.UptimeSeconds % 3600) / 60)} m`
                      : '—'
                  }
                  detail="Desde el inicio del servicio"
                />
              </div>
              <div className="dashboard-bottom">
                <section className="panel">
                  <div className="panel-title">
                    <h2>Conexión con Master</h2>
                    <Wifi size={17} />
                  </div>
                  <DeviceInfo
                    items={[
                      [
                        'Estado',
                        <ConnectionStatus
                          status={status}
                          connected={connected}
                        />,
                      ],
                      [
                        'Autenticación',
                        status
                          ? ['No enrolado', 'Enrolado', 'Revocado'][
                              status.Enrollment
                            ]
                          : 'Sin lectura',
                      ],
                      [
                        'Heartbeat',
                        config
                          ? `Cada ${config.HeartbeatSeconds} segundos`
                          : 'Sin lectura',
                      ],
                    ]}
                  />
                  <button
                    className="text-button"
                    onClick={() => navigate('settings')}
                  >
                    Administrar conexión <ArrowUpRight size={15} />
                  </button>
                  <button
                    className="button button--secondary"
                    onClick={() => void toggleObsStreaming()}
                    disabled={busy || !obs?.WebSocketConnected}
                  >
                    {obs?.Streaming
                      ? 'Detener transmisión'
                      : 'Iniciar transmisión'}
                  </button>
                </section>
                <ObsStatusCard
                  status={status}
                  connected={connected}
                  onOpen={() => navigate('obs')}
                />
              </div>
            </>
          )}
          {page === 'monitor' && (
            <>
              <SettingsSection
                title="Recursos del equipo"
                description={
                  readOnly
                    ? 'Servicio desconectado · última lectura conservada'
                    : 'Lectura actual del servicio'
                }
              >
                <DeviceInfo
                  items={[
                    [
                      'CPU',
                      metric?.CpuPercent == null
                        ? 'No disponible'
                        : `${metric.CpuPercent.toFixed(1)} %`,
                    ],
                    ['RAM total', bytes(metric?.RamTotalBytes)],
                    ['RAM disponible', bytes(metric?.RamAvailableBytes)],
                    [
                      'Utilización RAM',
                      metric?.RamUsedPercent == null
                        ? 'No disponible'
                        : `${metric.RamUsedPercent.toFixed(1)} %`,
                    ],
                    ['Disco disponible', bytes(metric?.DiskAvailableBytes)],
                  ]}
                />
              </SettingsSection>
              <SettingsSection title="Sistema e instalación">
                <DeviceInfo
                  items={[
                    [
                      'Sistema operativo',
                      metric?.OperatingSystem ?? 'Sin lectura',
                    ],
                    ['Arquitectura', metric?.Architecture ?? 'Sin lectura'],
                    [
                      'Nombre del equipo',
                      status?.Identity.Hostname ?? 'Sin lectura',
                    ],
                    ['Procesadores lógicos', metric?.CpuCount ?? 'Sin lectura'],
                    [
                      'Servicio activo',
                      status
                        ? `${Math.floor(status.UptimeSeconds)} segundos`
                        : 'Sin lectura',
                    ],
                    [
                      'Conectividad',
                      <ConnectionStatus
                        status={status}
                        connected={connected}
                      />,
                    ],
                  ]}
                />
              </SettingsSection>
              <p className="helper">
                Una métrica no disponible se muestra sin estimaciones. No se
                generan gráficos con datos simulados.
              </p>
            </>
          )}
          {page === 'obs' && (
            <>
              <section className="panel">
                <div className="panel-title">
                  <h2>Diagnóstico de sesión</h2>
                  <button
                    className="button"
                    onClick={() => void queryObs()}
                    disabled={busy}
                  >
                    <RefreshCw size={15} />
                    {busy ? 'Consultando…' : 'Consultar OBS'}
                  </button>
                </div>
                <p>
                  Conexión local a OBS WebSocket. La transmisión requiere
                  confirmación.
                </p>
                {obsError && (
                  <div className="inline-warning" role="status">
                    OBS no respondió. Comprueba que esté abierto y que WebSocket
                    esté disponible.
                  </div>
                )}
                <DeviceInfo
                  items={[
                    ['Instalación', 'Detección de instalación no disponible'],
                    [
                      'Proceso OBS',
                      obs?.ProcessRunning
                        ? 'Detectado'
                        : connected
                          ? status?.Obs.ProcessRunning
                            ? 'En ejecución'
                            : 'No detectado'
                          : 'Sin lectura',
                    ],
                    [
                      'WebSocket',
                      obs
                        ? obs.WebSocketConnected
                          ? 'Conectado'
                          : 'Desconectado'
                        : 'Sin consultar',
                    ],
                    [
                      'Transmisión',
                      obs?.Streaming == null
                        ? 'Sin lectura'
                        : obs.Streaming
                          ? 'Activa'
                          : 'Inactiva',
                    ],
                    ['Escena actual', obs?.CurrentScene ?? 'Sin lectura'],
                    ['Última consulta', date(obsAt)],
                  ]}
                />
                {obs?.Error && <p className="inline-warning">{obs.Error}</p>}
              </section>
              <SettingsSection title="Escenas disponibles">
                {obs?.Scenes?.length ? (
                  <ul className="scene-list">
                    {obs.Scenes.map((scene) => (
                      <li key={scene}>
                        {scene}
                        {scene === obs.CurrentScene && (
                          <StatusIndicator label="Actual" tone="success" />
                        )}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <EmptyState title="Sin escenas consultadas">
                    Consulta OBS para ver las escenas disponibles en esta
                    sesión.
                  </EmptyState>
                )}
              </SettingsSection>
              <p className="helper">
                <ShieldCheck size={15} /> Las claves RTMP permanecen en OBS. OBS
                con contraseña aún requiere completar la autenticación del
                Agent.
              </p>
            </>
          )}
          {page === 'settings' && (
            <>
              <SettingsSection
                title="Conexión con VELVET Master"
                description="La configuración de conexión se administra desde el servicio."
              >
                <label htmlFor="master-url">URL de Master</label>
                <input
                  id="master-url"
                  readOnly
                  value={config?.MasterUrl ?? ''}
                  placeholder="Servicio no disponible"
                />
                <DeviceInfo
                  items={[
                    [
                      'Conectividad',
                      <ConnectionStatus
                        status={status}
                        connected={connected}
                      />,
                    ],
                    [
                      'Enrolamiento',
                      status
                        ? ['No enrolado', 'Enrolado', 'Revocado'][
                            status.Enrollment
                          ]
                        : 'Sin lectura',
                    ],
                    [
                      'Identificador',
                      status?.Identity.InstallationId ?? 'Sin lectura',
                    ],
                  ]}
                />
                {status?.UserAuthenticated && (
                  <DeviceInfo
                    items={[
                      [
                        'Usuario',
                        status.UserDisplayName ?? 'Sesión autorizada',
                      ],
                      ['Cuenta', status.UserId ?? 'Identificada en Master'],
                    ]}
                  />
                )}
                {(!status?.UserAuthenticated || !status.UserDisplayName) && (
                  <div className="enrollment-form">
                    <h3>Inicia sesión con VELVET Master</h3>
                    <p>
                      Autoriza tu cuenta desde el navegador oficial para
                      preparar esta estación.
                    </p>
                    <button
                      className="button"
                      type="button"
                      onClick={() => void loginMaster()}
                      disabled={!connected || !config?.StudioId || busy}
                    >
                      Iniciar sesión con VELVET Master{' '}
                      <ArrowUpRight size={15} />
                    </button>
                    {!config?.StudioId && (
                      <p className="helper">
                        Configura VELVET_STUDIO_ID en el servicio para habilitar
                        OAuth.
                      </p>
                    )}
                  </div>
                )}
                <details>
                  <summary>Cómo cambiar la conexión</summary>
                  <p>
                    Define VELVET_MASTER_URL en el entorno del servicio y
                    reinícialo. Se requiere una URL HTTPS y permisos
                    administrativos en Windows.
                  </p>
                </details>
                <form
                  className="enrollment-form"
                  onSubmit={(e) => {
                    e.preventDefault();
                    setConfirm(true);
                  }}
                >
                  <label htmlFor="enrollment-token">
                    Enrolar esta estación
                  </label>
                  <p>
                    Ingresa el token temporal de un solo uso emitido por Master.
                  </p>
                  <div className="input-action">
                    <input
                      id="enrollment-token"
                      type="password"
                      autoComplete="off"
                      spellCheck={false}
                      maxLength={4096}
                      placeholder="Token temporal"
                      value={token}
                      disabled={!connected || config?.Development || busy}
                      onChange={(e) => setToken(e.target.value)}
                    />
                    <button
                      className="button"
                      disabled={
                        !connected ||
                        !token.trim() ||
                        config?.Development ||
                        busy
                      }
                    >
                      Enrolar <ArrowUpRight size={15} />
                    </button>
                  </div>
                  {config?.Development && (
                    <p className="helper">
                      El enrolamiento está deshabilitado en el adaptador de
                      desarrollo.
                    </p>
                  )}
                </form>
              </SettingsSection>
              <SettingsSection title="Preferencias de escritorio">
                <div className="preference">
                  <div>
                    <strong>Notificaciones informativas</strong>
                    <p>
                      Mostrar confirmaciones dentro de la aplicación. Los
                      errores operativos permanecen visibles.
                    </p>
                  </div>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={notifications}
                    aria-label="Notificaciones informativas"
                    className={`switch ${notifications ? 'is-on' : ''}`}
                    onClick={toggleNotifications}
                  >
                    <span />
                  </button>
                </div>
                <DeviceInfo
                  items={[
                    [
                      'Al cerrar la ventana',
                      'Continuar en la bandeja del sistema',
                    ],
                    ['Inicio de Desktop', 'Manual · abrir desde el sistema'],
                    [
                      'Inicio automático de Desktop',
                      'No configurado en esta versión',
                    ],
                    ['Versión instalada', status?.Version ?? '0.1.0'],
                  ]}
                />
              </SettingsSection>
            </>
          )}
          {page === 'diagnostics' && (
            <>
              <section className="panel">
                <div className="panel-title">
                  <h2>Salud de la conexión</h2>
                  <button
                    className="button button--ghost"
                    onClick={exportDiagnostics}
                  >
                    <ArrowDownToLine size={16} />
                    Exportar diagnóstico
                  </button>
                </div>
                <DeviceInfo
                  items={[
                    [
                      'Desktop → Service',
                      connected
                        ? 'Comunicación disponible'
                        : 'Comunicación no disponible',
                    ],
                    ['Protocolo local', 'v1 · Named Pipe / Unix socket'],
                    ['Último heartbeat', date(status?.LastSync)],
                    [
                      'Sincronización',
                      <ConnectionStatus
                        status={status}
                        connected={connected}
                      />,
                    ],
                    ['Reconexiones locales', `${reconnections} en esta sesión`],
                    ['Origen de los registros', 'Desktop · sesión actual'],
                  ]}
                />
                <p className="helper">
                  La exportación excluye credenciales, URL de Master, nombre e
                  identificador del equipo. Los registros internos del servicio
                  no se consultan en esta versión.
                </p>
              </section>
              <SettingsSection
                title="Actividad reciente"
                description="Solo transiciones reales observadas por Desktop."
              >
                <LogViewer entries={logs} />
              </SettingsSection>
            </>
          )}
        </>
      )}
      <footer className="content-footer">
        <span>
          <ShieldCheck size={13} /> Información técnica, sin captura de
          contenido
        </span>
        <span>VELVET Agent · {status?.Version ?? '0.1.0'}</span>
      </footer>
      {toast && <ToastNotification message={toast} onClose={closeToast} />}
      {confirm && (
        <ConfirmDialog
          title="¿Enrolar esta estación?"
          onConfirm={() => void confirmEnrollment()}
          onClose={() => {
            setConfirm(false);
            setToken('');
          }}
          busy={busy}
        >
          <p>
            Se enviará el token temporal a Master para registrar este equipo. En
            Windows se requieren permisos de administrador.
          </p>
        </ConfirmDialog>
      )}
    </AppShell>
  );
}
