import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import App from './App';
import {
  connectionLabel,
  diagnostics,
  isStatus,
  type Status,
} from './lib/agent';
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const status: Status = {
  Identity: {
    InstallationId: 'test-installation-id',
    Hostname: 'test-station',
  },
  Enrollment: 0,
  MasterConnected: true,
  LastSync: '2026-10-21T17:00:00Z',
  Version: '0.1.0',
  UptimeSeconds: 120,
  Mode: 'development-adapter',
  ConfigurationAvailable: true,
  Metrics: {
    Hostname: 'test-station',
    OperatingSystem: 'test-os',
    Architecture: 'Arm64',
    CpuCount: 8,
    CpuPercent: 12,
    RamTotalBytes: 17179869184,
    RamAvailableBytes: null,
    RamUsedPercent: null,
    DiskAvailableBytes: 120000000,
  },
  Obs: {
    ProcessRunning: false,
    WebSocketConnected: false,
    Streaming: null,
    CurrentScene: null,
    Scenes: null,
    Error: null,
  },
};
beforeEach(() => {
  HTMLDialogElement.prototype.showModal = function () {
    this.setAttribute('open', '');
  };
  HTMLDialogElement.prototype.close = function () {
    this.removeAttribute('open');
  };
  localStorage.clear();
  vi.mocked(invoke).mockReset();
  vi.mocked(invoke).mockImplementation(async (_command, args) => ({
    Version: 1,
    Success: true,
    Data:
      (args as { operation: string }).operation === 'GetConfiguration'
        ? {
            MasterUrl: 'https://master.invalid',
            HeartbeatSeconds: 30,
            Development: true,
          }
        : status,
  }));
});
afterEach(() => cleanup());
describe('Contratos y privacidad', () => {
  it('rechaza respuestas incompletas y valores no finitos', () => {
    expect(isStatus(status)).toBe(true);
    expect(
      isStatus({
        ...status,
        Metrics: { ...status.Metrics, CpuPercent: Infinity },
      }),
    ).toBe(false);
    expect(isStatus({ ...status, Identity: null })).toBe(false);
  });
  it('distingue desarrollo, enrolamiento, revocación y desconexión', () => {
    expect(connectionLabel(status, true)).toBe('Modo de desarrollo');
    expect(connectionLabel({ ...status, Mode: 'master-api' }, true)).toBe(
      'No enrolado',
    );
    expect(
      connectionLabel({ ...status, Mode: 'master-api', Enrollment: 2 }, true),
    ).toBe('Estación revocada');
    expect(connectionLabel(status, false)).toBe('Servicio no disponible');
    expect(
      connectionLabel(
        { ...status, Mode: 'master-api', Enrollment: 1, MasterConnected: true },
        true,
      ),
    ).toBe('Conectado a Master');
  });
  it('exporta por lista permitida y excluye campos agregados no autorizados', () => {
    const payload = JSON.stringify(
      diagnostics(
        {
          ...status,
          DeviceToken: 'private-value',
          Identity: { ...status.Identity, Hostname: 'private-host' },
        } as Status,
        true,
        [],
      ),
    );
    expect(payload).not.toContain('private-value');
    expect(payload).not.toContain('private-host');
    expect(payload).not.toContain('test-installation-id');
    expect(payload).not.toContain('DeviceToken');
  });
});
describe('Interfaz operativa', () => {
  it('muestra datos reales del contrato y no presenta desarrollo como Master conectado', async () => {
    render(<App />);
    await screen.findByText('12.0 %');
    expect(screen.getAllByText('Modo de desarrollo').length).toBeGreaterThan(0);
    expect(screen.queryByText('Conectado a Master')).toBeNull();
  });
  it('navega con botones accesibles y bloquea enrolamiento en desarrollo', async () => {
    render(<App />);
    await screen.findByText('12.0 %');
    fireEvent.click(screen.getByRole('button', { name: 'Configuración' }));
    const token = screen.getByLabelText(
      'Enrolar esta estación',
    ) as HTMLInputElement;
    expect(token.disabled).toBe(true);
    expect(
      (screen.getByRole('button', { name: /^Enrolar/ }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    fireEvent.click(screen.getByRole('switch'));
    expect(localStorage.getItem('velvet.notifications')).toBe('false');
  });
  it('muestra fallo explícito y recupera la comunicación al reintentar', async () => {
    vi.mocked(invoke).mockRejectedValueOnce(
      Error('sensitive detail must not display'),
    );
    render(<App />);
    await screen.findByText('No podemos consultar el servicio local');
    expect(screen.queryByText('sensitive detail must not display')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await screen.findByText('12.0 %');
    await waitFor(() =>
      expect(
        screen.queryByText('No podemos consultar el servicio local'),
      ).toBeNull(),
    );
  });
  it('no conserva estado OBS conectado después de una consulta fallida', async () => {
    render(<App />);
    await screen.findByText('12.0 %');
    fireEvent.click(screen.getByRole('button', { name: 'OBS Studio' }));
    vi.mocked(invoke).mockRejectedValueOnce(Error('private obs error'));
    fireEvent.click(screen.getByRole('button', { name: 'Consultar OBS' }));
    await screen.findByText(
      'OBS no respondió. Comprueba que esté abierto y que WebSocket esté disponible.',
    );
    expect(screen.queryByText('private obs error')).toBeNull();
  });
  it('requiere confirmación explícita antes de enviar el token y lo borra después', async () => {
    const masterStatus: Status = {
      ...status,
      Mode: 'master-api',
      Enrollment: 1,
      UserAuthenticated: true,
    };
    vi.mocked(invoke).mockImplementation(async (_command, args) => {
      const { operation } = args as { operation: string };
      return {
        Version: 1,
        Success: true,
        Data:
          operation === 'GetConfiguration'
            ? {
                MasterUrl: 'https://master.invalid',
                HeartbeatSeconds: 30,
                Development: false,
                StudioId: 'studio-paris',
              }
            : operation === 'RequestEnrollment'
              ? { Enrolled: true }
              : masterStatus,
      };
    });
    render(<App />);
    await screen.findByText('12.0 %');
    fireEvent.click(screen.getByRole('button', { name: 'Configuración' }));
    fireEvent.change(screen.getByLabelText('Enrolar esta estación'), {
      target: { value: 'one-use-test-token' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^Enrolar/ }));
    expect(
      vi
        .mocked(invoke)
        .mock.calls.filter(
          ([, args]) =>
            (args as { operation: string }).operation === 'RequestEnrollment',
        ),
    ).toHaveLength(0);
    fireEvent.click(
      screen.getByRole('button', { name: 'Confirmar enrolamiento' }),
    );
    await screen.findByText('Estación enrolada correctamente.');
    expect(
      (screen.getByLabelText('Enrolar esta estación') as HTMLInputElement)
        .value,
    ).toBe('');
    expect(
      vi
        .mocked(invoke)
        .mock.calls.filter(
          ([, args]) =>
            (args as { operation: string }).operation === 'RequestEnrollment',
        ),
    ).toHaveLength(1);
  });
});
