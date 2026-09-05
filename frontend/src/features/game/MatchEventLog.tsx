import type {
  MatchEventSnapshot,
  PlayerSnapshot,
  UnitType,
} from "../../shared/types/game";
import styles from "./MatchEventLog.module.css";

const unitLabels: Record<UnitType, string> = {
  Scout: "İzci",
  Infantry: "Piyade",
  Armor: "Zırhlı",
};

function coordinate(column: number | null, row: number | null): string {
  if (column === null || row === null) return "—";
  return `${String.fromCharCode(65 + column)}${row + 1}`;
}

function playerName(players: PlayerSnapshot[], playerId: string | null): string {
  return players.find((player) => player.id === playerId)?.name ?? "Komuta";
}

export function describeMatchEvent(
  event: MatchEventSnapshot,
  players: PlayerSnapshot[],
  viewerPlayerId?: string,
): { title: string; detail: string } {
  const actor = playerName(players, event.actorPlayerId);
  const related = playerName(players, event.relatedPlayerId);
  const unit = event.unitType ? unitLabels[event.unitType] : "Birlik";
  const target = event.targetUnitType ? unitLabels[event.targetUnitType] : "hedef";
  const from = coordinate(event.fromColumn, event.fromRow);
  const to = coordinate(event.toColumn, event.toRow);

  switch (event.type) {
    case "DeploymentStarted":
      return { title: "Yerleşim başladı", detail: "İki ordu başlangıç hatlarına ulaştı." };
    case "UnitPlaced":
      return { title: `${actor} · ${unit}`, detail: `${from} → ${to} konuşlandırıldı.` };
    case "PlayerReady":
      return { title: `${actor} hazır`, detail: "Yerleşim düzeni kilitlendi." };
    case "BattleStarted":
      return {
        title: event.relatedPlayerId === viewerPlayerId ? "Savaş başladı · sıra sende" : "Savaş başladı",
        detail: `${related} ilk komut hakkını aldı.`,
      };
    case "UnitMoved":
      return {
        title: `${actor} · ${unit}`,
        detail: to === "—"
          ? `${from} bölgesinden görüş dışına çıktı.`
          : from === "—"
            ? `${to} bölgesinde görüldü.`
            : `${from} → ${to} ilerledi.`,
      };
    case "ReinforcementTriggered":
      return {
        title: "Takviye bölgesi keşfedildi",
        detail: `${actor} için yeni bir ${target} birliği ${to} bölgesine ulaştı.`,
      };
    case "MineTriggered":
      return {
        title: event.wasDestroyed ? `${unit} mayında kaybedildi` : `Mayın · -${event.amount ?? 0} can`,
        detail: `${actor} · ${unit}, ${to} bölgesindeki mayına bastı.`,
      };
    case "UnitAttacked":
      return {
        title: event.wasDestroyed
          ? `${target} imha edildi`
          : event.defenseBonus > 0
            ? `${event.amount ?? 0} hasar · siper +${event.defenseBonus}`
            : `${event.amount ?? 0} hasar`,
        detail: event.defenseBonus > 0
          ? `${actor} · ${unit} vurdu; ${target} arazi korumasından yararlandı.`
          : `${actor} · ${unit}, ${target} birliğini vurdu.`,
      };
    case "TurnEnded":
      return {
        title: event.relatedPlayerId === viewerPlayerId ? "Sıra sende" : `${related} turu devraldı`,
        detail: `${actor} turunu tamamladı.`,
      };
    case "TurnTimedOut":
      return {
        title: event.relatedPlayerId === viewerPlayerId ? "Süre doldu · sıra sende" : "Süre doldu · tur devredildi",
        detail: `${actor} için 90 saniye doldu. Sıra ${related} oyuncusuna geçti.`,
      };
    case "MatchCompleted":
      return {
        title: event.relatedPlayerId === viewerPlayerId ? "Zafer" : "Operasyon tamamlandı",
        detail: `${related} muharebeyi kazandı.`,
      };
    case "RematchRequested":
      return {
        title: "Rövanş istendi",
        detail: `${actor} aynı rakiple yeniden karşılaşmak istiyor.`,
      };
    case "RematchAccepted":
      return {
        title: "Rövanş kabul edildi",
        detail: "Taraflar değiştirilerek yeni operasyon oluşturuldu.",
      };
  }
}

type MatchEventLogProps = {
  events: MatchEventSnapshot[];
  players: PlayerSnapshot[];
  viewerPlayerId: string;
};

export function MatchEventLog({ events, players, viewerPlayerId }: MatchEventLogProps) {
  const visibleEvents = events.slice(-10).reverse();

  return (
    <section className={styles.card} aria-labelledby="event-log-title">
      <header>
        <div>
          <p className={styles.eyebrow}>Canlı kronoloji</p>
          <h2 id="event-log-title">Saha günlüğü</h2>
        </div>
        <span className={styles.counter}>{events.length.toString().padStart(2, "0")}</span>
      </header>
      {visibleEvents.length === 0 ? (
        <p className={styles.empty}>İlk saha olayı bekleniyor.</p>
      ) : (
        <ol className={styles.timeline} aria-live="polite">
          {visibleEvents.map((event) => {
            const message = describeMatchEvent(event, players, viewerPlayerId);
            return (
              <li key={event.sequence} data-type={event.type}>
                <span className={styles.marker} aria-hidden="true" />
                <div>
                  <span className={styles.turn}>T{event.turnNumber.toString().padStart(2, "0")} · #{event.sequence}</span>
                  <strong>{message.title}</strong>
                  <p>{message.detail}</p>
                </div>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}
