import type { FormEvent } from "react";
import type { MatchSummary } from "../../shared/types/game";
import styles from "./LobbyPanel.module.css";

type LobbyPanelProps = {
  playerName: string;
  matchName: string;
  matches: MatchSummary[];
  busy: boolean;
  error: string | null;
  onPlayerNameChange: (value: string) => void;
  onMatchNameChange: (value: string) => void;
  onCreate: (playAgainstBot: boolean) => Promise<void>;
  onJoin: (matchId: string) => Promise<void>;
  onRefresh: () => Promise<void>;
};

export function LobbyPanel({
  playerName,
  matchName,
  matches,
  busy,
  error,
  onPlayerNameChange,
  onMatchNameChange,
  onCreate,
  onJoin,
  onRefresh,
}: LobbyPanelProps) {
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void onCreate(false);
  };

  const openMatches = matches.filter((match) => match.status === "WaitingForPlayers");

  return (
    <aside className={styles.lobby} aria-label="Oyun lobisi">
      <section className={styles.enlistCard}>
        <p className={styles.eyebrow}>Oyuncu kimliği</p>
        <label>
          Çağrı adın
          <input
            value={playerName}
            maxLength={40}
            placeholder="Örn. Günay"
            onChange={(event) => onPlayerNameChange(event.target.value)}
          />
        </label>
      </section>

      <form className={styles.createCard} onSubmit={handleSubmit}>
        <p className={styles.eyebrow}>Yeni operasyon</p>
        <label>
          Oyun adı
          <input
            value={matchName}
            maxLength={120}
            placeholder="Kuzey Geçidi"
            onChange={(event) => onMatchNameChange(event.target.value)}
          />
        </label>
        <div className={styles.createActions}>
          <button type="submit" disabled={busy || !playerName.trim() || !matchName.trim()}>
            İki oyunculu oluştur
          </button>
          <button
            className={styles.botButton}
            type="button"
            disabled={busy || !playerName.trim() || !matchName.trim()}
            onClick={() => void onCreate(true)}
          >
            Yapay zekâya karşı oyna
          </button>
        </div>
      </form>

      <section className={styles.openCard}>
        <header>
          <div>
            <p className={styles.eyebrow}>Açık oyunlar</p>
            <strong>{openMatches.length.toString().padStart(2, "0")}</strong>
          </div>
          <button
            type="button"
            className={styles.refreshButton}
            disabled={busy}
            onClick={() => void onRefresh()}
          >
            Yenile
          </button>
        </header>

        {error && <p className={styles.error} role="alert">{error}</p>}

        {openMatches.length === 0 ? (
          <p className={styles.empty}>Katılabileceğin bir oyun yok. Yeni bir operasyon başlat.</p>
        ) : (
          <ul>
            {openMatches.map((match) => (
              <li key={match.id}>
                <div>
                  <strong>{match.name}</strong>
                  <span>{match.playerCount}/{match.playerCapacity} oyuncu</span>
                </div>
                <button
                  type="button"
                  disabled={busy || !playerName.trim()}
                  onClick={() => void onJoin(match.id)}
                >
                  Katıl
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </aside>
  );
}
