import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';
import type { ConnectionState } from './types';

const configuredHub = import.meta.env.VITE_SIGNALR_HUB_URL as string | undefined;
const HUB_URL = configuredHub || '/hubs/emergency';

export function useSessionConnection(sessionId: string | undefined, online: boolean, onAuthoritativeRefresh: () => void): ConnectionState {
  const [state, setState] = useState<ConnectionState>(online ? 'connecting' : 'offline');
  const refreshRef = useRef(onAuthoritativeRefresh);
  refreshRef.current = onAuthoritativeRefresh;

  useEffect(() => {
    if (!sessionId || !online) {
      setState(online ? 'polling' : 'offline');
      return;
    }

    let connection: HubConnection | undefined;
    let disposed = false;

    async function connect(): Promise<void> {
      connection = new HubConnectionBuilder()
        .withUrl(HUB_URL, { withCredentials: true })
        .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000])
        .configureLogging(LogLevel.Warning)
        .build();

      const handleUpdate = () => refreshRef.current();
      connection.on('SessionUpdated', handleUpdate);
      connection.on('TimelineAdded', handleUpdate);
      connection.on('TaskUpdated', handleUpdate);
      connection.on('ParticipantJoined', handleUpdate);
      connection.onreconnecting(() => setState('connecting'));
      connection.onreconnected(async () => {
        try {
          await connection?.invoke('JoinSession', sessionId);
          refreshRef.current();
          setState('connected');
        } catch {
          setState('polling');
        }
      });
      connection.onclose(() => {
        if (!disposed) setState('polling');
      });

      try {
        setState('connecting');
        await connection.start();
        await connection.invoke('JoinSession', sessionId);
        if (!disposed) {
          refreshRef.current();
          setState('connected');
        }
      } catch {
        if (!disposed) setState('polling');
      }
    }

    void connect();
    return () => {
      disposed = true;
      if (connection?.state !== HubConnectionState.Disconnected) void connection?.stop();
    };
  }, [online, sessionId]);

  return state;
}
