import type {
  MatchSnapshot,
  MatchSummary,
  PlayerSession,
  ProblemDetails,
} from "../types/game";

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ProblemDetails | null;
    throw new Error(problem?.detail ?? problem?.title ?? "İstek tamamlanamadı.");
  }

  return response.json() as Promise<T>;
}

export function listMatches(): Promise<MatchSummary[]> {
  return request<MatchSummary[]>("/api/matches");
}

export function createMatch(matchName: string, playerName: string): Promise<PlayerSession> {
  return request<PlayerSession>("/api/matches", {
    method: "POST",
    body: JSON.stringify({ matchName, playerName }),
  });
}

export function joinMatch(matchId: string, playerName: string): Promise<PlayerSession> {
  return request<PlayerSession>(`/api/matches/${matchId}/players`, {
    method: "POST",
    body: JSON.stringify({ playerName }),
  });
}

type MoveUnitInput = {
  matchId: string;
  playerId: string;
  unitId: string;
  targetColumn: number;
  targetRow: number;
  expectedVersion: number;
};

export function moveUnit(input: MoveUnitInput): Promise<MatchSnapshot> {
  return request<MatchSnapshot>(`/api/matches/${input.matchId}/moves`, {
    method: "POST",
    body: JSON.stringify({
      playerId: input.playerId,
      unitId: input.unitId,
      targetColumn: input.targetColumn,
      targetRow: input.targetRow,
      expectedVersion: input.expectedVersion,
    }),
  });
}
