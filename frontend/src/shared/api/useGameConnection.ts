import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { useCallback, useEffect, useRef, useState } from "react";
import type { MatchSnapshot } from "../types/game";
import type {
  ConnectionState,
  ServerReadyMessage,
} from "../types/realtime";

type GameConnection = {
  state: ConnectionState;
  serverReady: ServerReadyMessage | null;
  matchUpdate: MatchSnapshot | null;
  joinMatchChannel: (matchId: string) => Promise<void>;
  leaveMatchChannel: (matchId: string) => Promise<void>;
};

const reconnectDelays = [0, 2_000, 5_000, 10_000];

export function useGameConnection(): GameConnection {
  const connectionRef = useRef<HubConnection | null>(null);
  const joinedMatchRef = useRef<string | null>(null);
  const [state, setState] = useState<ConnectionState>("connecting");
  const [serverReady, setServerReady] = useState<ServerReadyMessage | null>(null);
  const [matchUpdate, setMatchUpdate] = useState<MatchSnapshot | null>(null);

  useEffect(() => {
    const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/game`)
      .withAutomaticReconnect(reconnectDelays)
      .configureLogging(LogLevel.Error)
      .build();

    let disposed = false;
    connectionRef.current = connection;

    connection.on("ServerReady", (message: ServerReadyMessage) => {
      if (!disposed) {
        setServerReady(message);
        setState("connected");
      }
    });

    connection.on("MatchUpdated", (match: MatchSnapshot) => {
      if (!disposed) {
        setMatchUpdate(match);
      }
    });

    connection.onreconnecting(() => {
      if (!disposed) setState("reconnecting");
    });

    connection.onreconnected(async () => {
      if (disposed) return;
      setState("connected");

      if (joinedMatchRef.current) {
        await connection.invoke("JoinMatch", joinedMatchRef.current);
      }
    });

    connection.onclose(() => {
      if (!disposed) setState("offline");
    });

    void connection.start().catch(() => {
      if (!disposed) setState("offline");
    });

    return () => {
      disposed = true;
      connectionRef.current = null;
      void connection.stop();
    };
  }, []);

  const joinMatchChannel = useCallback(async (matchId: string) => {
    const connection = connectionRef.current;

    if (!connection || connection.state !== "Connected") {
      throw new Error("Gerçek zamanlı bağlantı henüz hazır değil.");
    }

    await connection.invoke("JoinMatch", matchId);
    joinedMatchRef.current = matchId;
  }, []);

  const leaveMatchChannel = useCallback(async (matchId: string) => {
    const connection = connectionRef.current;

    if (connection?.state === "Connected") {
      await connection.invoke("LeaveMatch", matchId);
    }

    if (joinedMatchRef.current === matchId) {
      joinedMatchRef.current = null;
    }
  }, []);

  return {
    state,
    serverReady,
    matchUpdate,
    joinMatchChannel,
    leaveMatchChannel,
  };
}
