import type {
  MatchEventSnapshot,
  MatchSnapshot,
  PlayerSnapshot,
} from "../../shared/types/game";
import styles from "./AfterActionReport.module.css";

type PlayerReport = {
  player: PlayerSnapshot;
  moves: number;
  damage: number;
  destroyed: number;
  reinforcements: number;
  mines: number;
};

function countEvents(
  events: MatchEventSnapshot[],
  playerId: string,
  type: MatchEventSnapshot["type"],
): MatchEventSnapshot[] {
  return events.filter((event) => event.actorPlayerId === playerId && event.type === type);
}

function buildPlayerReport(match: MatchSnapshot, player: PlayerSnapshot): PlayerReport {
  const attacks = countEvents(match.events, player.id, "UnitAttacked");
  return {
    player,
    moves: countEvents(match.events, player.id, "UnitMoved").length,
    damage: attacks.reduce((total, event) => total + (event.amount ?? 0), 0),
    destroyed: attacks.filter((event) => event.wasDestroyed).length,
    reinforcements: countEvents(match.events, player.id, "ReinforcementTriggered").length,
    mines: countEvents(match.events, player.id, "MineTriggered").length,
  };
}

function formatDuration(match: MatchSnapshot): string {
  if (!match.completedAtUtc) return "—";

  const durationSeconds = Math.max(
    0,
    Math.round((Date.parse(match.completedAtUtc) - Date.parse(match.createdAtUtc)) / 1_000),
  );
  const minutes = Math.floor(durationSeconds / 60);
  const seconds = durationSeconds % 60;
  return `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
}

type AfterActionReportProps = {
  match: MatchSnapshot;
  viewerPlayerId: string;
  busy: boolean;
  error: string | null;
  onRequestRematch: () => void;
  onAcceptRematch: () => void;
  onNewGame: () => void;
  onLobby: () => void;
};

export function AfterActionReport({
  match,
  viewerPlayerId,
  busy,
  error,
  onRequestRematch,
  onAcceptRematch,
  onNewGame,
  onLobby,
}: AfterActionReportProps) {
  const reports = match.players
    .slice()
    .sort((left, right) => left.seat - right.seat)
    .map((player) => buildPlayerReport(match, player));
  const winner = match.players.find((player) => player.id === match.winnerPlayerId);
  const viewerWon = winner?.id === viewerPlayerId;
  const requestedByViewer = match.rematchRequestedByPlayerId === viewerPlayerId;
  const canAccept = Boolean(
    match.rematchRequestedByPlayerId
    && match.rematchRequestedByPlayerId !== viewerPlayerId,
  );
  const botMatch = match.players.some((player) => player.isBot);

  return (
    <section className={styles.report} aria-labelledby="after-action-title">
      <header className={styles.reportHeader}>
        <div>
          <p className={styles.eyebrow}>Harekât sonrası rapor · {match.name}</p>
          <h2 id="after-action-title">{viewerWon ? "Zafer teyit edildi" : "Operasyon sona erdi"}</h2>
          <p className={styles.verdict}>
            {winner?.name ?? "Komuta"} muharebe hedefini tamamladı. Saha kayıtları arşivlendi.
          </p>
        </div>
        <div className={styles.outcomeStamp} data-outcome={viewerWon ? "victory" : "defeat"}>
          <span>{viewerWon ? "SONUÇ / ZAFER" : "SONUÇ / KAYIP"}</span>
          <strong>{winner?.name ?? "—"}</strong>
        </div>
      </header>

      <div className={styles.battleLine}>
        {reports.map((report, index) => (
          <article
            key={report.player.id}
            className={styles.playerReport}
            data-winner={report.player.id === match.winnerPlayerId}
            data-side={report.player.seat === 1 ? "west" : "east"}
            style={{ gridArea: index === 0 ? "west" : "east" }}
          >
            <div className={styles.playerIdentity}>
              <span>{report.player.seat === 1 ? "Batı komutası" : "Doğu komutası"}</span>
              <h3>{report.player.name}</h3>
              <small>{report.player.id === viewerPlayerId
                ? "Sen"
                : report.player.isBot
                  ? "Yapay zekâ"
                  : "Rakip"}</small>
            </div>
            <dl>
              <div><dt>Hareket</dt><dd>{report.moves}</dd></div>
              <div><dt>Verilen hasar</dt><dd>{report.damage}</dd></div>
              <div><dt>İmha</dt><dd>{report.destroyed}</dd></div>
              <div><dt>Takviye</dt><dd>{report.reinforcements}</dd></div>
              <div><dt>Mayın teması</dt><dd>{report.mines}</dd></div>
            </dl>
          </article>
        ))}

        <div className={styles.engagementSummary}>
          <span>MUHAREBE ÇİZGİSİ</span>
          <div><strong>{match.turnNumber}</strong><small>toplam tur</small></div>
          <i aria-hidden="true" />
          <div><strong>{formatDuration(match)}</strong><small>operasyon süresi</small></div>
          <span>#{match.events.length.toString().padStart(3, "0")} saha kaydı</span>
        </div>
      </div>

      <footer className={styles.reportActions}>
        <div className={styles.rematchState}>
          <span>Rövanş kanalı</span>
          <strong>{botMatch
            ? "Yeni bir yapay zekâ operasyonu başlatabilirsin"
            : match.rematchMatchId
            ? "Yeni operasyona aktarılıyorsunuz"
            : requestedByViewer
              ? "Rakibin yanıtı bekleniyor"
              : canAccept
                ? "Rakibin rövanş isteği var"
                : "Taraflar değiştirilerek yeniden oynayın"}</strong>
        </div>
        {error && <p className={styles.error} role="alert">{error}</p>}
        <div className={styles.buttons}>
          {!botMatch && !match.rematchMatchId && (
            <button
              className={styles.primaryButton}
              type="button"
              disabled={busy || requestedByViewer}
              onClick={canAccept ? onAcceptRematch : onRequestRematch}
            >
              {requestedByViewer
                ? "Yanıt bekleniyor"
                : canAccept
                  ? "Rövanşı kabul et"
                  : "Rövanş iste"}
            </button>
          )}
          <button type="button" disabled={busy} onClick={onNewGame}>Yeni oyun oluştur</button>
          <button type="button" disabled={busy} onClick={onLobby}>Lobiye dön</button>
        </div>
      </footer>
    </section>
  );
}
