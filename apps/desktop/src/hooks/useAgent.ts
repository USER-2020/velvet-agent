import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Config,
  LogEntry,
  Status,
  isConfig,
  isStatus,
  request,
} from '../lib/agent';
export function useAgent() {
  const [status, setStatus] = useState<Status | null>(null),
    [config, setConfig] = useState<Config | null>(null);
  const [connected, setConnected] = useState(false),
    [loading, setLoading] = useState(true),
    [refreshing, setRefreshing] = useState(false);
  const [logs, setLogs] = useState<LogEntry[]>([]),
    [reconnections, setReconnections] = useState(0);
  const active = useRef(true),
    pending = useRef<Promise<void> | null>(null),
    previous = useRef<boolean | null>(null),
    everConnected = useRef(false);
  const refresh = useCallback(() => {
    if (pending.current) return pending.current;
    setRefreshing(true);
    const task = (async () => {
      try {
        // Sequential requests respect the single-connection service and preserve status/config coherence.
        const next = await request('GetAgentStatus', isStatus);
        const configuration = await request('GetConfiguration', isConfig);
        if (!active.current) return;
        setStatus(next);
        setConfig(configuration);
        setConnected(true);
        if (previous.current !== true) {
          if (everConnected.current) setReconnections((n) => n + 1);
          setLogs((items) =>
            [
              {
                at: new Date().toISOString(),
                kind: 'info' as const,
                message: everConnected.current
                  ? 'Comunicación local restablecida'
                  : 'Comunicación local establecida',
              },
              ...items,
            ].slice(0, 100),
          );
        }
        previous.current = true;
        everConnected.current = true;
      } catch {
        if (!active.current) return;
        setConnected(false);
        if (previous.current !== false)
          setLogs((items) =>
            [
              {
                at: new Date().toISOString(),
                kind: 'warning' as const,
                message:
                  'Servicio local no disponible. Reconexión automática pendiente.',
              },
              ...items,
            ].slice(0, 100),
          );
        previous.current = false;
      } finally {
        if (active.current) {
          setLoading(false);
          setRefreshing(false);
        }
        pending.current = null;
      }
    })();
    pending.current = task;
    return task;
  }, []);
  useEffect(() => {
    active.current = true;
    void refresh();
    const timer = setInterval(() => void refresh(), 5000);
    return () => {
      active.current = false;
      clearInterval(timer);
    };
  }, [refresh]);
  return {
    status,
    config,
    connected,
    loading,
    refreshing,
    refresh,
    logs,
    reconnections,
  };
}
