export type MatchStatus =
  | "WaitingForPlayers"
  | "InProgress"
  | "Completed"
  | "Abandoned";

export type PlayerSnapshot = {
  id: string;
  name: string;
  seat: number;
};

export type UnitSnapshot = {
  id: string;
  ownerPlayerId: string;
  column: number;
  row: number;
};

export type MatchSnapshot = {
  id: string;
  name: string;
  status: MatchStatus;
  turnNumber: number;
  version: number;
  activePlayerId: string | null;
  winnerPlayerId: string | null;
  players: PlayerSnapshot[];
  units: UnitSnapshot[];
  createdAtUtc: string;
};

export type MatchSummary = {
  id: string;
  name: string;
  status: MatchStatus;
  turnNumber: number;
  version: number;
  playerCount: number;
  playerCapacity: number;
  createdAtUtc: string;
};

export type PlayerSession = {
  playerId: string;
  match: MatchSnapshot;
};

export type ProblemDetails = {
  title?: string;
  detail?: string;
  status?: number;
};
