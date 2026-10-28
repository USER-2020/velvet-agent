import { invoke } from '@tauri-apps/api/core';
export type Metrics = {
  CpuCount: number;
  CpuPercent: number | null;
  RamTotalBytes: number | null;
  RamAvailableBytes: number | null;
  RamUsedPercent: number | null;
  DiskAvailableBytes: number | null;
  OperatingSystem: string;
  Architecture: string;
  Hostname: string;
};
export type Obs = {
  ProcessRunning: boolean;
  WebSocketConnected: boolean;
  Streaming: boolean | null;
  CurrentScene: string | null;
  Scenes: string[] | null;
  Error: string | null;
};
export type Status = {
  Identity: { InstallationId: string; Hostname: string };
  Enrollment: number;
  MasterConnected: boolean;
  LastSync: string | null;
  Version: string;
  UptimeSeconds: number;
  Metrics: Metrics;
  Obs: Obs;
  Mode: 'development-adapter' | 'master-api';
  UserAuthenticated?: boolean;
  UserId?: string | null;
  UserDisplayName?: string | null;
  UserRole?: string | null;
  UserPermissions?: string[];
  UserRoles?: string[];
  ConfigurationAvailable?: boolean;
  SessionId?: string | null;
  SessionStartedAt?: string | null;
};
export type Config = {
  MasterUrl: string;
  HeartbeatSeconds: number;
  Development: boolean;
  StudioId?: string | null;
};
export type LogEntry = {
  at: string;
  kind: 'info' | 'warning';
  message: string;
};
type Guard<T> = (value: unknown) => value is T;
const record = (v: unknown): v is Record<string, unknown> =>
  !!v && typeof v === 'object';
const finite = (v: unknown): v is number =>
  typeof v === 'number' && Number.isFinite(v);
const nullableNumber = (v: unknown) => v === null || finite(v);
const nullableText = (v: unknown) => v === null || typeof v === 'string';
export const isObs: Guard<Obs> = (v): v is Obs =>
  record(v) &&
  typeof v.ProcessRunning === 'boolean' &&
  typeof v.WebSocketConnected === 'boolean' &&
  (v.Streaming === null || typeof v.Streaming === 'boolean') &&
  nullableText(v.CurrentScene) &&
  nullableText(v.Error) &&
  (v.Scenes === null ||
    (Array.isArray(v.Scenes) && v.Scenes.every((s) => typeof s === 'string')));
export const isStatus: Guard<Status> = (v): v is Status => {
  if (!record(v) || !record(v.Identity) || !record(v.Metrics)) return false;
  const m = v.Metrics;
  return (
    typeof v.Identity.InstallationId === 'string' &&
    typeof v.Identity.Hostname === 'string' &&
    [0, 1, 2].includes(v.Enrollment as number) &&
    typeof v.MasterConnected === 'boolean' &&
    nullableText(v.LastSync) &&
    typeof v.Version === 'string' &&
    finite(v.UptimeSeconds) &&
    ['development-adapter', 'master-api'].includes(v.Mode as string) &&
    isObs(v.Obs) &&
    ['Hostname', 'OperatingSystem', 'Architecture'].every(
      (k) => typeof m[k] === 'string',
    ) &&
    finite(m.CpuCount) &&
    [
      'CpuPercent',
      'RamTotalBytes',
      'RamAvailableBytes',
      'RamUsedPercent',
      'DiskAvailableBytes',
    ].every((k) => nullableNumber(m[k]))
  );
};
export const isConfig: Guard<Config> = (v): v is Config =>
  record(v) &&
  typeof v.MasterUrl === 'string' &&
  finite(v.HeartbeatSeconds) &&
  typeof v.Development === 'boolean';
export async function request<T>(
  operation: string,
  guard: Guard<T>,
  token?: string,
): Promise<T> {
  const response: unknown = await invoke('agent_request', {
    operation,
    token: token ?? null,
  });
  if (!record(response) || response.Version !== 1)
    throw Error('Respuesta del servicio no válida');
  if (response.Success !== true) {
    const message = typeof response.Error === 'string' ? response.Error : null;
    throw Error(message || 'El servicio rechazó la operación');
  }
  if (!guard(response.Data)) throw Error('Respuesta del servicio no válida');
  return response.Data;
}
export async function enroll(token: string) {
  await request(
    'RequestEnrollment',
    (v): v is { Enrolled: boolean } => record(v) && v.Enrolled === true,
    token,
  );
}
export async function inspectObs(): Promise<Obs> {
  const response: unknown = await invoke('obs_status');
  if (!isObs(response)) throw Error('Respuesta OBS no válida');
  return response;
}
export async function reportObsStatus(status: Obs): Promise<void> {
  await invoke('report_obs_status', { status });
}
export async function setObsStreaming(start: boolean): Promise<void> {
  await invoke('obs_set_streaming', { start });
  try {
    const response: unknown = await invoke('agent_request', {
      operation: start ? 'StartTransmission' : 'StopTransmission',
      token: null,
    });
    if (!record(response) || response.Success !== true)
      throw Error(
        start
          ? 'Master no registró el inicio de transmisión'
          : 'Master no registró el fin de transmisión',
      );
  } catch (error) {
    if (start) {
      try {
        await invoke('obs_set_streaming', { start: false });
      } catch {
        /* conserva el error original */
      }
    }
    throw error;
  }
}
export async function loginWithMaster(
  config: Config,
  installationId: string,
  hostname: string,
): Promise<void> {
  if (!config.StudioId)
    throw Error('La estación no tiene StudioId configurado');
  await invoke('oauth_login', {
    masterUrl: config.MasterUrl,
    installationId,
    hostname,
    studioId: config.StudioId,
  });
}
export function connectionLabel(
  status: Status | null,
  connected: boolean,
): string {
  if (!connected) return 'Servicio no disponible';
  if (!status) return 'Conectando';
  if (status.Mode === 'development-adapter') return 'Modo de desarrollo';
  if (status.Enrollment === 0) return 'No enrolado';
  if (status.Enrollment === 2) return 'Estación revocada';
  return status.MasterConnected
    ? 'Conectado a Master'
    : 'Sin conexión a Master';
}
export function roleLabel(role: string | null | undefined): string | null {
  if (!role) return null;
  const labels: Record<string, string> = {
    model: 'Modelo',
    admin: 'Administrador',
    administrator: 'Administrador',
    superadmin: 'Superadministrador',
    'super-admin': 'Superadministrador',
    monitor: 'Monitor',
    recruiter: 'Reclutamiento',
    recruitment: 'Reclutamiento',
  };
  return labels[role.toLowerCase()] ?? role;
}
export const bytes = (n: number | null | undefined) =>
  n == null ? 'No disponible' : `${(n / 1024 ** 3).toFixed(1)} GB`;
export const date = (value: string | null | undefined) =>
  value
    ? new Date(value).toLocaleTimeString('es', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      })
    : 'Sin registro';
export function diagnostics(
  status: Status | null,
  connected: boolean,
  logs: LogEntry[],
) {
  // Explicit allowlist: excludes credentials, configuration URLs, hostname and installation ID.
  return {
    schemaVersion: 1,
    exportedAt: new Date().toISOString(),
    serviceAvailable: connected,
    agent: status
      ? {
          version: status.Version,
          mode: status.Mode,
          enrollment: status.Enrollment,
          lastSync: status.LastSync,
          masterConnected: status.MasterConnected,
          uptimeSeconds: status.UptimeSeconds,
          operatingSystem: status.Metrics.OperatingSystem,
          architecture: status.Metrics.Architecture,
          cpuCount: status.Metrics.CpuCount,
          cpuPercent: status.Metrics.CpuPercent,
          ramTotalBytes: status.Metrics.RamTotalBytes,
          diskAvailableBytes: status.Metrics.DiskAvailableBytes,
          obsProcessRunning: status.Obs.ProcessRunning,
        }
      : null,
    events: logs.map(({ at, kind, message }) => ({ at, kind, message })),
  };
}
