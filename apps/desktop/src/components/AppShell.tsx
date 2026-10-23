import type { ReactNode } from 'react';
import {
  Activity,
  AudioLines,
  ChevronLeft,
  ChevronRight,
  LayoutDashboard,
  Settings2,
  ShieldCheck,
  Stethoscope,
} from 'lucide-react';
import { StatusIndicator } from './ui';
import { roleLabel } from '../lib/agent';
export type Page = 'dashboard' | 'monitor' | 'obs' | 'settings' | 'diagnostics';
const navigation = [
  { id: 'dashboard', label: 'Inicio', icon: LayoutDashboard },
  { id: 'monitor', label: 'Monitoreo', icon: Activity },
  { id: 'obs', label: 'OBS Studio', icon: AudioLines },
  { id: 'settings', label: 'Configuración', icon: Settings2 },
  { id: 'diagnostics', label: 'Diagnósticos', icon: Stethoscope },
] as const;
export function Sidebar({
  page,
  onNavigate,
  compact,
  onCompact,
  version,
  canConfigure,
  userRole,
}: {
  page: Page;
  onNavigate: (page: Page) => void;
  compact: boolean;
  onCompact: () => void;
  version: string;
  canConfigure: boolean;
  userRole?: string | null;
}) {
  return (
    <aside className={`sidebar ${compact ? 'sidebar--compact' : ''}`}>
      <div className="brand">
        <img src="/brand/velvet-studio.svg" alt="The Velvet Studio" />
        <span>
          AGENT <small>ESTACIÓN LOCAL</small>
        </span>
      </div>
      {roleLabel(userRole) && (
        <div className="sidebar-role-badge" aria-label={`Rol: ${roleLabel(userRole)}`}>
          {roleLabel(userRole)}
        </div>
      )}
      <div className="nav-caption">ESPACIO DE TRABAJO</div>
      <nav aria-label="Navegación principal">
        {navigation.filter(({ id }) => id !== 'settings' || canConfigure).map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            className={`nav-item ${page === id ? 'is-active' : ''}`}
            aria-current={page === id ? 'page' : undefined}
            onClick={() => onNavigate(id)}
            title={label}
            aria-label={label}
          >
            <Icon size={18} />
            <span>{label}</span>
          </button>
        ))}
      </nav>
      <div className="sidebar-bottom">
        <div className="privacy-note">
          <ShieldCheck size={18} />
          <p>
            Tu estación, protegida.<small>Solo información técnica.</small>
          </p>
        </div>
        <div className="sidebar-version">
          <span>Agent · {version}</span>
          <button
            className="icon-button"
            onClick={onCompact}
            aria-label={compact ? 'Expandir navegación' : 'Contraer navegación'}
          >
            {compact ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
          </button>
        </div>
      </div>
    </aside>
  );
}
export function TopBar({
  hostname,
  userRole,
  connected,
  loading,
}: {
  hostname: string;
  userRole?: string | null;
  connected: boolean;
  loading: boolean;
}) {
  return (
    <header className="topbar">
      <div className="topbar-station">
        <span className="station-dot" />
        <span>{hostname}</span>
        <small>ESTACIÓN</small>
        {roleLabel(userRole) && <small className="topbar-role">{roleLabel(userRole)}</small>}
      </div>
      <StatusIndicator
        label={
          loading
            ? 'Conectando'
            : connected
              ? 'Servicio activo'
              : 'Servicio no disponible'
        }
        tone={loading ? 'neutral' : connected ? 'success' : 'danger'}
      />
    </header>
  );
}
export function AppShell({
  sidebar,
  topbar,
  children,
  compact,
}: {
  sidebar: ReactNode;
  topbar: ReactNode;
  children: ReactNode;
  compact: boolean;
}) {
  return (
    <div className={`app-shell ${compact ? 'app-shell--compact' : ''}`}>
      <a className="skip-link" href="#main-content">
        Saltar al contenido
      </a>
      {sidebar}
      <div className="workspace">
        {topbar}
        <main id="main-content" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
