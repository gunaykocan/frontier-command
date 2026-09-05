import { useEffect, useState } from "react";
import type { MatchSnapshot } from "../../shared/types/game";
import { remainingTurnSeconds } from "./turnClock";

export function useTurnClock(match: MatchSnapshot | null): number {
  const deadline = match?.status === "InProgress" ? match.turnExpiresAtUtc : null;
  const serverTime = match?.serverTimeUtc ?? "";
  const duration = match?.turnDurationSeconds ?? 90;
  const key = `${match?.id}:${deadline}:${serverTime}`;
  const [sample, setSample] = useState({ key: "", seconds: 0 });

  useEffect(() => {
    if (!deadline) return;
    // Count elapsed time, not ticks: background-tab throttling must not extend a turn.
    // The server timestamp also avoids depending on the player's local clock setting.
    const receivedAt = performance.now();
    const tick = () => {
      const seconds = remainingTurnSeconds(deadline, serverTime, performance.now() - receivedAt, duration);
      setSample(current => current.key === key && current.seconds === seconds ? current : { key, seconds });
    };
    tick();
    const interval = window.setInterval(tick, 250);
    return () => window.clearInterval(interval);
  }, [deadline, duration, key, serverTime]);

  return !deadline ? 0 : sample.key === key
    ? sample.seconds
    : remainingTurnSeconds(deadline, serverTime, 0, duration);
}
