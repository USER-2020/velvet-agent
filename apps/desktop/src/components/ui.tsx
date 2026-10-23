import { useEffect, useRef, type ReactNode } from 'react';
import {
  AlertCircle,
  ArrowUpRight,
  Info,
  LoaderCircle,
  Monitor,
  X,
} from 'lucide-react';
export function StatusIndicator({
  label,
  tone = 'neutral',
}: {
  label: string;
  tone?: 'success' | 'warning' | 'danger' | 'neutral';
}) {
  return (
    <span className={`status status--${tone}`}>
      <i aria-hidden="true" />
      {label}
    </span>
  );
}
export function MetricCard({
  label,
  value,
  detail,
  icon,
  percent,
}: {
  label: string;
  value: string;
  detail: string;
  icon: ReactNode;
  percent?: number | null;
}) {
  return (
    <article className="metric-card">
      <div className="metric-label">
        {label}
        {icon}
      </div>
      <div className="metric-value">{value}</div>
      <div className="meter" aria-hidden="true">
        <span
          style={{
            width:
              percent == null
                ? '0%'
                : `${Math.max(0, Math.min(100, percent))}%`,
          }}
        />
      </div>
      <p>{detail}</p>
    </article>
  );
}
export function SettingsSection({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <section className="panel settings-section">
      <div className="section-heading">
        <h2>{title}</h2>
        {description && <p>{description}</p>}
      </div>
      {children}
    </section>
  );
}
export function DeviceInfo({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="device-info">
      {items.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
export function EmptyState({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <div className="empty-state">
      <Monitor size={26} />
      <h3>{title}</h3>
      <p>{children}</p>
    </div>
  );
}
export function LoadingState() {
  return (
    <div
      role="status"
      aria-label="Cargando estado de la estación"
      className="loading-grid"
    >
      {[1, 2, 3, 4].map((n) => (
        <div className="skeleton" key={n} />
      ))}
      <span className="sr-only">Cargando estado de la estación</span>
    </div>
  );
}
export function ErrorState({
  onRetry,
  busy,
}: {
  onRetry: () => void;
  busy: boolean;
}) {
  return (
    <div className="error-state" role="status">
      <AlertCircle size={18} />
      <div>
        <strong>No podemos consultar el servicio local</strong>
        <p>
          Comprobaremos la conexión automáticamente. Los datos anteriores se
          muestran como última lectura.
        </p>
      </div>
      <button
        className="button button--ghost"
        disabled={busy}
        onClick={onRetry}
      >
        Reintentar
      </button>
    </div>
  );
}
export function ToastNotification({
  message,
  onClose,
}: {
  message: string;
  onClose: () => void;
}) {
  useEffect(() => {
    const id = setTimeout(onClose, 6000);
    return () => clearTimeout(id);
  }, [message, onClose]);
  return (
    <div className="toast" role="status">
      <Info size={17} />
      <span>{message}</span>
      <button
        className="icon-button"
        aria-label="Cerrar notificación"
        onClick={onClose}
      >
        <X size={16} />
      </button>
    </div>
  );
}
export function ConfirmDialog({
  title,
  children,
  onConfirm,
  onClose,
  busy,
}: {
  title: string;
  children: ReactNode;
  onConfirm: () => void;
  onClose: () => void;
  busy: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal();
    return () => dialog?.close();
  }, []);
  return (
    <dialog
      ref={ref}
      className="confirm-dialog"
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onClose();
      }}
      aria-labelledby="confirm-title"
    >
      <h2 id="confirm-title">{title}</h2>
      <div>{children}</div>
      <div className="dialog-actions">
        <button
          className="button button--ghost"
          disabled={busy}
          onClick={onClose}
        >
          Cancelar
        </button>
        <button
          autoFocus
          className="button"
          disabled={busy}
          onClick={onConfirm}
        >
          {busy ? <LoaderCircle size={16} /> : <ArrowUpRight size={16} />}
          Confirmar enrolamiento
        </button>
      </div>
    </dialog>
  );
}
export function LogViewer({
  entries,
}: {
  entries: { at: string; kind: string; message: string }[];
}) {
  return entries.length ? (
    <ol className="log-viewer">
      {entries.map((entry, i) => (
        <li key={`${entry.at}-${i}`}>
          <time>{new Date(entry.at).toLocaleTimeString()}</time>
          <StatusIndicator
            label={entry.kind === 'warning' ? 'Aviso' : 'Info'}
            tone={entry.kind === 'warning' ? 'warning' : 'neutral'}
          />
          <span>{entry.message}</span>
        </li>
      ))}
    </ol>
  ) : (
    <EmptyState title="Sin eventos en esta sesión">
      Los cambios de conexión aparecerán aquí.
    </EmptyState>
  );
}
