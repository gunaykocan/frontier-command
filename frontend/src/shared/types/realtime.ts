export type ServerReadyMessage = {
  connectionId: string;
  connectedAtUtc: string;
  transport: string;
};

export type ConnectionState =
  | "connecting"
  | "connected"
  | "reconnecting"
  | "offline";
