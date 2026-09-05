import type {
  MatchSnapshot,
  MatchSummary,
  PlayerSession,
  ProblemDetails,
} from "../types/game";

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");

async function readResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ProblemDetails | null;
    throw new Error(problem?.detail ?? problem?.title ?? "İstek tamamlanamadı.");
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  return readResponse<T>(response);
}

export function listMatches(): Promise<MatchSummary[]> {
  return request<MatchSummary[]>("/api/matches");
}

export function createMatch(
  matchName: string,
  playerName: string,
  playAgainstBot = false,
): Promise<PlayerSession> {
  return request<PlayerSession>("/api/matches", {
    method: "POST",
    body: JSON.stringify({ matchName, playerName, playAgainstBot }),
  });
}

export function joinMatch(matchId: string, playerName: string): Promise<PlayerSession> {
  return request<PlayerSession>(`/api/matches/${matchId}/players`, {
    method: "POST",
    body: JSON.stringify({ playerName }),
  });
}

export async function restorePlayerSession(): Promise<PlayerSession | null> {
  const response = await fetch(`${apiBaseUrl}/api/session`, {
    credentials: "include",
  });

  if (response.status === 401) {
    return null;
  }

  return readResponse<PlayerSession>(response);
}

export function clearPlayerSession(): Promise<void> {
  return request<void>("/api/session", { method: "DELETE" });
}

type PlaceUnitInput = {
  matchId: string;
  unitId: string;
  targetColumn: number;
  targetRow: number;
  expectedVersion: number;
};

export function placeUnit(input: PlaceUnitInput): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${input.matchId}/deployment/placements`, {
    method: "POST",
    body: JSON.stringify({
      unitId: input.unitId,
      targetColumn: input.targetColumn,
      targetRow: input.targetRow,
      expectedVersion: input.expectedVersion,
    }),
  });
}

export function readyForBattle(matchId: string, expectedVersion: number): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${matchId}/deployment/ready`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

type MoveUnitInput = {
  matchId: string;
  unitId: string;
  targetColumn: number;
  targetRow: number;
  expectedVersion: number;
};

export function moveUnit(input: MoveUnitInput): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${input.matchId}/moves`, {
    method: "POST",
    body: JSON.stringify({
      unitId: input.unitId,
      targetColumn: input.targetColumn,
      targetRow: input.targetRow,
      expectedVersion: input.expectedVersion,
    }),
  });
}

type AttackUnitInput = {
  matchId: string;
  attackerUnitId: string;
  targetUnitId: string;
  expectedVersion: number;
};

export function attackUnit(input: AttackUnitInput): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${input.matchId}/attacks`, {
    method: "POST",
    body: JSON.stringify({
      attackerUnitId: input.attackerUnitId,
      targetUnitId: input.targetUnitId,
      expectedVersion: input.expectedVersion,
    }),
  });
}

export function endTurn(matchId: string, expectedVersion: number): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${matchId}/turns/end`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

export function requestRematch(matchId: string, expectedVersion: number): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${matchId}/rematch/request`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

export function acceptRematch(matchId: string, expectedVersion: number): Promise<PlayerSession> {
  return request<PlayerSession>(`/api/matches/${matchId}/rematch/accept`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

export function enterRematch(matchId: string): Promise<PlayerSession> {
  return request<PlayerSession>(`/api/matches/${matchId}/rematch/enter`, {
    method: "POST",
  });
}
