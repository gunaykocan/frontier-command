export function remainingTurnSeconds(
  deadlineUtc: string | null,
  serverTimeUtc: string,
  elapsedMilliseconds: number,
  durationSeconds: number,
): number {
  if (!deadlineUtc) return 0;
  const remaining = Date.parse(deadlineUtc) - Date.parse(serverTimeUtc) - Math.max(0, elapsedMilliseconds);
  if (!Number.isFinite(remaining)) return 0;
  return Math.max(0, Math.min(durationSeconds, Math.ceil(remaining / 1_000)));
}

export function formatTurnSeconds(seconds: number): string {
  return `${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
}
