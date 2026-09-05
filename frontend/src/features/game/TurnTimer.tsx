import { formatTurnSeconds } from "./turnClock";
import styles from "./TurnTimer.module.css";

type TurnTimerProps = {
  remainingSeconds: number;
  durationSeconds: number;
  isOwnTurn: boolean;
  connected: boolean;
};

export function TurnTimer({ remainingSeconds, durationSeconds, isOwnTurn, connected }: TurnTimerProps) {
  const expired = remainingSeconds === 0;
  const urgent = remainingSeconds <= 15;
  const message = !connected
    ? "Bağlantı kesilse de süre devam eder."
    : expired
      ? "Süre doldu. Tur devrediliyor…"
      : urgent
        ? isOwnTurn ? "Son 15 saniye · hamleni tamamla." : "Rakibin son 15 saniyesi."
        : "Süre dolunca tur otomatik devredilir.";

  return (
    <section className={styles.timer} data-urgent={urgent} aria-label="Tur süresi">
      <div className={styles.readout}>
        <span>{isOwnTurn ? "Senin süren" : "Rakibin süresi"}</span>
        <strong role="timer" aria-label="Kalan tur süresi" aria-live="off">
          {formatTurnSeconds(remainingSeconds)}
        </strong>
      </div>
      <div className={styles.track} aria-hidden="true">
        <span style={{ width: `${Math.max(0, Math.min(100, remainingSeconds / durationSeconds * 100))}%` }} />
      </div>
      <p role="status">{message}</p>
    </section>
  );
}
