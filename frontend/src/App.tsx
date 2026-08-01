import { useCallback, useEffect, useMemo, useState } from "react";
import {
  TacticalBoard,
  type BoardUnit,
  type TileSelection,
} from "./features/game/TacticalBoard";
import { LobbyPanel } from "./features/lobby/LobbyPanel";
import {
  createMatch,
  joinMatch,
  listMatches,
  moveUnit,
} from "./shared/api/gameApi";
import { useGameConnection } from "./shared/api/useGameConnection";
import type {
  MatchSummary,
  PlayerSession,
  UnitSnapshot,
} from "./shared/types/game";
import type { ConnectionState } from "./shared/types/realtime";
import styles from "./App.module.css";

const connectionLabels: Record<ConnectionState, string> = {
  connecting: "Bağlanıyor",
  connected: "Bağlı",
  reconnecting: "Yeniden bağlanıyor",
  offline: "Çevrimdışı",
};

const previewUnits: BoardUnit[] = [
  { coordinate: "B4", side: "friendly", selected: false },
  { coordinate: "H4", side: "hostile", selected: false },
];

function coordinateFrom(column: number, row: number): string {
  return `${String.fromCharCode(65 + column)}${row + 1}`;
}

function positionFrom(coordinate: string): { column: number; row: number } {
  return {
    column: coordinate.charCodeAt(0) - 65,
    row: Number.parseInt(coordinate.slice(1), 10) - 1,
  };
}

function areAdjacent(unit: UnitSnapshot, selection: TileSelection): boolean {
  const target = positionFrom(selection.coordinate);
  const directions = unit.row % 2 === 0
    ? [[-1, -1], [0, -1], [-1, 0], [1, 0], [-1, 1], [0, 1]]
    : [[0, -1], [1, -1], [-1, 0], [1, 0], [0, 1], [1, 1]];

  return directions.some(([column, row]) =>
    unit.column + column === target.column && unit.row + row === target.row);
}

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function App() {
  const {
    state,
    serverReady,
    matchUpdate,
    joinMatchChannel,
    leaveMatchChannel,
  } = useGameConnection();
  const [session, setSession] = useState<PlayerSession | null>(null);
  const [matches, setMatches] = useState<MatchSummary[]>([]);
  const [playerName, setPlayerName] = useState("");
  const [matchName, setMatchName] = useState("Kuzey Geçidi");
  const [selection, setSelection] = useState<TileSelection>({
    coordinate: "D4",
    terrain: "Çayır",
  });
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [lobbyBusy, setLobbyBusy] = useState(false);
  const [moveBusy, setMoveBusy] = useState(false);
  const [lobbyError, setLobbyError] = useState<string | null>(null);
  const [moveError, setMoveError] = useState<string | null>(null);

  const match = session?.match ?? null;

  const refreshMatches = useCallback(async () => {
    setLobbyBusy(true);
    setLobbyError(null);

    try {
      setMatches(await listMatches());
    } catch (error) {
      setLobbyError(messageFrom(error));
    } finally {
      setLobbyBusy(false);
    }
  }, []);

  useEffect(() => {
    void refreshMatches();
  }, [refreshMatches]);

  useEffect(() => {
    if (!matchUpdate) return;

    setSession((current) => current?.match.id === matchUpdate.id
      ? { ...current, match: matchUpdate }
      : current);
  }, [matchUpdate]);

  const ownUnit = useMemo(() => match?.units.find(
    (unit) => unit.ownerPlayerId === session?.playerId,
  ) ?? null, [match, session?.playerId]);

  useEffect(() => {
    if (!ownUnit || selectedUnitId) return;

    setSelectedUnitId(ownUnit.id);
    setSelection({
      coordinate: coordinateFrom(ownUnit.column, ownUnit.row),
      terrain: "Çayır",
    });
  }, [ownUnit, selectedUnitId]);

  const enterMatch = async (nextSession: PlayerSession) => {
    setSession(nextSession);
    setLobbyError(null);

    const unit = nextSession.match.units.find(
      (candidate) => candidate.ownerPlayerId === nextSession.playerId,
    );

    if (unit) {
      setSelectedUnitId(unit.id);
      setSelection({
        coordinate: coordinateFrom(unit.column, unit.row),
        terrain: "Çayır",
      });
    }

    try {
      await joinMatchChannel(nextSession.match.id);
    } catch (error) {
      setMoveError(messageFrom(error));
    }
  };

  const handleCreate = async () => {
    setLobbyBusy(true);
    setLobbyError(null);

    try {
      await enterMatch(await createMatch(matchName.trim(), playerName.trim()));
    } catch (error) {
      setLobbyError(messageFrom(error));
    } finally {
      setLobbyBusy(false);
    }
  };

  const handleJoin = async (matchId: string) => {
    setLobbyBusy(true);
    setLobbyError(null);

    try {
      await enterMatch(await joinMatch(matchId, playerName.trim()));
    } catch (error) {
      setLobbyError(messageFrom(error));
    } finally {
      setLobbyBusy(false);
    }
  };

  const handleSelection = (nextSelection: TileSelection) => {
    setSelection(nextSelection);
    setMoveError(null);

    const unit = match?.units.find(
      (candidate) => coordinateFrom(candidate.column, candidate.row) === nextSelection.coordinate,
    );

    if (unit && unit.ownerPlayerId === session?.playerId) {
      setSelectedUnitId(unit.id);
    }
  };

  const selectedUnit = match?.units.find((unit) => unit.id === selectedUnitId) ?? ownUnit;
  const targetOccupied = match?.units.some(
    (unit) => coordinateFrom(unit.column, unit.row) === selection.coordinate,
  ) ?? false;
  const isOwnTurn = Boolean(match && session && match.activePlayerId === session.playerId);
  const canMove = Boolean(
    match?.status === "InProgress"
    && isOwnTurn
    && selectedUnit
    && areAdjacent(selectedUnit, selection)
    && !targetOccupied
    && !moveBusy,
  );

  const handleMove = async () => {
    if (!match || !session || !selectedUnit || !canMove) return;

    const target = positionFrom(selection.coordinate);
    setMoveBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = await moveUnit({
        matchId: match.id,
        playerId: session.playerId,
        unitId: selectedUnit.id,
        targetColumn: target.column,
        targetRow: target.row,
        expectedVersion: match.version,
      });
      setSession({ ...session, match: updatedMatch });
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setMoveBusy(false);
    }
  };

  const handleLeave = async () => {
    if (match) await leaveMatchChannel(match.id);
    setSession(null);
    setSelectedUnitId(null);
    setMoveError(null);
    await refreshMatches();
  };

  const connectedAt = useMemo(() => {
    if (!serverReady) return "—";

    return new Intl.DateTimeFormat("tr-TR", {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    }).format(new Date(serverReady.connectedAtUtc));
  }, [serverReady]);

  const boardUnits = useMemo<BoardUnit[]>(() => {
    if (!match || !session) return previewUnits;

    return match.units.map((unit) => ({
      coordinate: coordinateFrom(unit.column, unit.row),
      side: unit.ownerPlayerId === session.playerId ? "friendly" : "hostile",
      selected: unit.id === selectedUnitId,
    }));
  }, [match, selectedUnitId, session]);

  const activePlayer = match?.players.find((player) => player.id === match.activePlayerId);
  const winner = match?.players.find((player) => player.id === match.winnerPlayerId);
  const actionLabel = match?.status === "WaitingForPlayers"
    ? "Rakip bekleniyor"
    : match?.status === "Completed"
      ? `${winner?.name ?? "Oyuncu"} kazandı`
      : isOwnTurn
        ? "Hamleyi tamamla"
        : "Rakibin hamlesi";

  return (
    <div className={styles.shell}>
      <header className={styles.topBar}>
        <div className={styles.identity}>
          <span className={styles.insignia} aria-hidden="true">F</span>
          <div>
            <p className={styles.eyebrow}>Taktik ağ / oynanabilir dilim</p>
            <h1>Frontier Command</h1>
          </div>
        </div>

        <div className={styles.connection} data-state={state}>
          <span className={styles.connectionPulse} aria-hidden="true" />
          <div>
            <span className={styles.connectionLabel}>Komuta bağlantısı</span>
            <strong>{connectionLabels[state]}</strong>
          </div>
        </div>
      </header>

      <main className={styles.workspace}>
        <section className={styles.mapPanel} aria-labelledby="map-title">
          <header className={styles.panelHeader}>
            <div>
              <p className={styles.eyebrow}>{match ? "Aktif operasyon" : "Saha önizlemesi"}</p>
              <h2 id="map-title">{match?.name ?? "Kuzey Geçidi"}</h2>
            </div>
            <div className={styles.mapMeta}>
              <span>9 × 7 hex</span>
              <span>{match ? `v${match.version}` : "Lobi"}</span>
            </div>
          </header>

          <TacticalBoard
            selectedCoordinate={selection.coordinate}
            units={boardUnits}
            onSelect={handleSelection}
          />

          <footer className={styles.mapFooter}>
            <div>
              <span className={styles.dataLabel}>Seçili bölge</span>
              <strong>{selection.coordinate}</strong>
            </div>
            <div>
              <span className={styles.dataLabel}>Arazi</span>
              <strong>{selection.terrain}</strong>
            </div>
            <p>{match
              ? "Birliğini seç, bitişik boş bir bölgeyi işaretle ve hamleni sunucuya gönder."
              : "Çağrı adını belirle; yeni oyun oluştur veya açık bir operasyona katıl."}</p>
          </footer>
        </section>

        {!match ? (
          <LobbyPanel
            playerName={playerName}
            matchName={matchName}
            matches={matches}
            busy={lobbyBusy}
            error={lobbyError}
            onPlayerNameChange={setPlayerName}
            onMatchNameChange={setMatchName}
            onCreate={handleCreate}
            onJoin={handleJoin}
            onRefresh={refreshMatches}
          />
        ) : (
          <aside className={styles.commandRail} aria-label="Tur ve oyun durumu">
            <section className={styles.turnCard}>
              <div className={styles.turnTrack} aria-hidden="true"><span /></div>
              <p className={styles.eyebrow}>Aktif tur</p>
              <p className={styles.turnNumber}>{match.turnNumber.toString().padStart(2, "0")}</p>
              <h2>{match.status === "Completed"
                ? "Operasyon tamamlandı"
                : activePlayer
                  ? `${activePlayer.name} hareket ediyor`
                  : "Rakip bekleniyor"}</h2>
              <p className={styles.muted}>{isOwnTurn
                ? "Birliğinle bitişik boş bir bölge seç."
                : match.status === "WaitingForPlayers"
                  ? "İkinci oyuncu katıldığında ilk tur başlayacak."
                  : "Güncelleme SignalR üzerinden otomatik gelecek."}</p>
              {moveError && <p className={styles.actionError} role="alert">{moveError}</p>}
              <button type="button" disabled={!canMove} onClick={() => void handleMove()}>
                {actionLabel}
              </button>
            </section>

            <section className={styles.systemCard}>
              <header>
                <p className={styles.eyebrow}>Oyuncular</p>
                <span className={styles.statusTag}>{match.players.length}/2 bağlı</span>
              </header>
              <dl>
                {match.players.map((player) => (
                  <div key={player.id}>
                    <dt>{player.seat === 1 ? "Batı birliği" : "Doğu birliği"}</dt>
                    <dd>{player.name}{player.id === session?.playerId ? " · sen" : ""}</dd>
                  </div>
                ))}
                <div>
                  <dt>Bağlantı saati</dt>
                  <dd>{connectedAt}</dd>
                </div>
              </dl>
              <button className={styles.leaveButton} type="button" onClick={() => void handleLeave()}>
                Lobiye dön
              </button>
            </section>

            <section className={styles.legendCard}>
              <p className={styles.eyebrow}>Görev</p>
              <p className={styles.objective}>Birliğini rakibin harita kenarına ulaştır.</p>
              <ul>
                <li><span className={styles.friendlyMarker} />Senin birliğin</li>
                <li><span className={styles.hostileMarker} />Rakip birlik</li>
                <li><span className={styles.selectionMarker} />Hedef bölge</li>
              </ul>
            </section>
          </aside>
        )}
      </main>

      <footer className={styles.commandBar}>
        <span className={styles.dataLabel}>Komut kanalı</span>
        <div className={styles.commandHint}><kbd>Sol tık</kbd><span>Bölge seç</span></div>
        <div className={styles.commandHint}><kbd>Oklar</kbd><span>Klavyeyle ilerle</span></div>
        <span className={styles.buildMark}>NET/10 · SIGNALR · POSTGRESQL</span>
      </footer>
    </div>
  );
}

export default App;
