import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  describeMatchEvent,
  MatchEventLog,
} from "./features/game/MatchEventLog";
import { AfterActionReport } from "./features/game/AfterActionReport";
import { TurnTimer } from "./features/game/TurnTimer";
import { useTurnClock } from "./features/game/useTurnClock";
import {
  TacticalBoard,
  type BoardUnit,
  type TileSelection,
} from "./features/game/TacticalBoard";
import {
  findTerrainTile,
  previewTerrainTiles,
  terrainLabels,
} from "./features/game/terrain";
import { LobbyPanel } from "./features/lobby/LobbyPanel";
import {
  acceptRematch,
  attackUnit,
  clearPlayerSession,
  createMatch,
  endTurn,
  enterRematch,
  joinMatch,
  listMatches,
  moveUnit,
  placeUnit,
  readyForBattle,
  requestRematch,
  restorePlayerSession,
} from "./shared/api/gameApi";
import { useGameConnection } from "./shared/api/useGameConnection";
import type {
  MatchEventSnapshot,
  MatchSummary,
  PlayerSession,
  UnitSnapshot,
  UnitType,
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
  ...["A4", "A5", "I4", "I5"].map((coordinate): BoardUnit => ({
    id: `preview-reserve-${coordinate}`,
    coordinate,
    side: coordinate.startsWith("A") ? "friendly" : "hostile",
    selected: false,
    type: "Infantry",
    health: 4,
    maximumHealth: 4,
    remainingMovement: coordinate.startsWith("A") ? 2 : 0,
  })),
  {
    id: "preview-west-scout",
    coordinate: "B3",
    side: "friendly",
    selected: false,
    type: "Scout",
    health: 2,
    maximumHealth: 2,
    remainingMovement: 3,
  },
  {
    id: "preview-west-infantry",
    coordinate: "B4",
    side: "friendly",
    selected: false,
    type: "Infantry",
    health: 4,
    maximumHealth: 4,
    remainingMovement: 2,
  },
  {
    id: "preview-west-armor",
    coordinate: "B5",
    side: "friendly",
    selected: false,
    type: "Armor",
    health: 6,
    maximumHealth: 6,
    remainingMovement: 1,
  },
  {
    id: "preview-east-scout",
    coordinate: "H3",
    side: "hostile",
    selected: false,
    type: "Scout",
    health: 2,
    maximumHealth: 2,
    remainingMovement: 0,
  },
  {
    id: "preview-east-infantry",
    coordinate: "H4",
    side: "hostile",
    selected: false,
    type: "Infantry",
    health: 4,
    maximumHealth: 4,
    remainingMovement: 0,
  },
  {
    id: "preview-east-armor",
    coordinate: "H5",
    side: "hostile",
    selected: false,
    type: "Armor",
    health: 6,
    maximumHealth: 6,
    remainingMovement: 0,
  },
];

const unitTypeLabels: Record<UnitType, string> = {
  Scout: "İzci",
  Infantry: "Piyade",
  Armor: "Zırhlı",
};

function coordinateFrom(column: number, row: number): string {
  return `${String.fromCharCode(65 + column)}${row + 1}`;
}

function positionFrom(coordinate: string): { column: number; row: number } {
  return {
    column: coordinate.charCodeAt(0) - 65,
    row: Number.parseInt(coordinate.slice(1), 10) - 1,
  };
}

function selectionFrom(
  terrainTiles: PlayerSession["match"]["terrainTiles"],
  column: number,
  row: number,
): TileSelection {
  const terrain = findTerrainTile(terrainTiles, column, row);
  return {
    coordinate: coordinateFrom(column, row),
    terrain: terrain.type,
    movementCost: terrain.movementCost,
    defenseBonus: terrain.defenseBonus,
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

function adjacentPositions(column: number, row: number): { column: number; row: number }[] {
  const directions = row % 2 === 0
    ? [[-1, -1], [0, -1], [-1, 0], [1, 0], [-1, 1], [0, 1]]
    : [[0, -1], [1, -1], [-1, 0], [1, 0], [0, 1], [1, 1]];

  return directions
    .map(([columnOffset, rowOffset]) => ({
      column: column + columnOffset,
      row: row + rowOffset,
    }))
    .filter((position) =>
      position.column >= 0 && position.column < 9 && position.row >= 0 && position.row < 7);
}

function messageFrom(error: unknown): string {
  if (error instanceof Error && error.message.includes("turn time has expired")) {
    return "Tur süren doldu. Sıra rakibine geçiyor.";
  }
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function App() {
  const [session, setSession] = useState<PlayerSession | null>(null);
  const {
    state,
    serverReady,
    matchUpdate,
    joinMatchChannel,
    leaveMatchChannel,
  } = useGameConnection(session?.playerId ?? null);
  const [matches, setMatches] = useState<MatchSummary[]>([]);
  const [playerName, setPlayerName] = useState("");
  const [matchName, setMatchName] = useState("Kuzey Geçidi");
  const [selection, setSelection] = useState<TileSelection>(() =>
    selectionFrom(previewTerrainTiles, 3, 3));
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [lobbyBusy, setLobbyBusy] = useState(false);
  const [sessionRestoring, setSessionRestoring] = useState(true);
  const [actionBusy, setActionBusy] = useState(false);
  const [lobbyError, setLobbyError] = useState<string | null>(null);
  const [moveError, setMoveError] = useState<string | null>(null);
  const [recentEvent, setRecentEvent] = useState<MatchEventSnapshot | null>(null);
  const eventMatchIdRef = useRef<string | null>(null);
  const lastEventSequenceRef = useRef(0);
  const recentEventTimeoutRef = useRef<number | null>(null);
  const rematchTransitionRef = useRef(false);

  const match = session?.match ?? null;
  const matchId = match?.id ?? null;
  const remainingSeconds = useTurnClock(match);
  const turnExpired = match?.status === "InProgress" && remainingSeconds === 0;

  const applySession = useCallback((nextSession: PlayerSession, suppressExistingEvents = false) => {
    eventMatchIdRef.current = nextSession.match.id;
    lastEventSequenceRef.current = suppressExistingEvents
      ? (nextSession.match.events?.at(-1)?.sequence ?? 0)
      : 0;
    setRecentEvent(null);
    rematchTransitionRef.current = false;
    setSession(nextSession);
    setLobbyError(null);

    const player = nextSession.match.players.find(
      (candidate) => candidate.id === nextSession.playerId,
    );
    const unit = nextSession.match.units.find(
      (candidate) => candidate.ownerPlayerId === nextSession.playerId,
    );

    if (player) {
      setPlayerName(player.name);
    }

    if (unit) {
      setSelectedUnitId(unit.id);
      setSelection(selectionFrom(
        nextSession.match.terrainTiles ?? previewTerrainTiles,
        unit.column,
        unit.row,
      ));
    }
  }, []);

  const applyMatchUpdate = useCallback((nextMatch: PlayerSession["match"]) => {
    setSession((current) => {
      if (!current || current.match.id !== nextMatch.id || current.match.version > nextMatch.version
        || (current.match.version === nextMatch.version
          && Date.parse(current.match.serverTimeUtc) >= Date.parse(nextMatch.serverTimeUtc))) {
        return current;
      }

      return { ...current, match: nextMatch };
    });
  }, []);

  useEffect(() => {
    if (!matchId) return;
    let disposed = false;
    let inFlight = false;
    const synchronize = async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const restored = await restorePlayerSession();
        if (!disposed && restored?.match.id === matchId) applyMatchUpdate(restored.match);
      } catch {
        // The connection indicator communicates outages; retry on focus or the next timeout tick.
      } finally {
        inFlight = false;
      }
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") void synchronize();
    };
    if (state === "connected") void synchronize();
    window.addEventListener("focus", onVisible);
    document.addEventListener("visibilitychange", onVisible);
    // Recover a missed broadcast without asking the client to end the turn itself.
    const interval = turnExpired ? window.setInterval(() => void synchronize(), 2_000) : undefined;
    return () => {
      disposed = true;
      window.removeEventListener("focus", onVisible);
      document.removeEventListener("visibilitychange", onVisible);
      window.clearInterval(interval);
    };
  }, [applyMatchUpdate, matchId, state, turnExpired]);

  useEffect(() => {
    let disposed = false;

    const restore = async () => {
      try {
        const restoredSession = await restorePlayerSession();

        if (!disposed && restoredSession) {
          applySession(restoredSession, true);
        }
      } catch (error) {
        if (!disposed) {
          setLobbyError(messageFrom(error));
        }
      } finally {
        if (!disposed) {
          setSessionRestoring(false);
        }
      }
    };

    void restore();

    return () => {
      disposed = true;
    };
  }, [applySession]);

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

    applyMatchUpdate(matchUpdate);
  }, [applyMatchUpdate, matchUpdate]);

  const latestEvent = match?.events?.at(-1) ?? null;

  useEffect(() => {
    if (!match) {
      if (recentEventTimeoutRef.current !== null) {
        window.clearTimeout(recentEventTimeoutRef.current);
        recentEventTimeoutRef.current = null;
      }
      eventMatchIdRef.current = null;
      lastEventSequenceRef.current = 0;
      setRecentEvent(null);
      return;
    }

    if (eventMatchIdRef.current !== match.id) {
      eventMatchIdRef.current = match.id;
      lastEventSequenceRef.current = 0;
    }

    if (!latestEvent || latestEvent.sequence <= lastEventSequenceRef.current) return;

    lastEventSequenceRef.current = latestEvent.sequence;
    setRecentEvent(latestEvent);
    if (recentEventTimeoutRef.current !== null) {
      window.clearTimeout(recentEventTimeoutRef.current);
    }
    recentEventTimeoutRef.current = window.setTimeout(() => {
      setRecentEvent(null);
      recentEventTimeoutRef.current = null;
    }, 1650);
  }, [latestEvent, match]);

  useEffect(() => () => {
    if (recentEventTimeoutRef.current !== null) {
      window.clearTimeout(recentEventTimeoutRef.current);
    }
  }, []);

  useEffect(() => {
    if (
      !match
      || !session
      || match.status !== "Completed"
      || !match.rematchMatchId
      || rematchTransitionRef.current
    ) return;

    rematchTransitionRef.current = true;
    setActionBusy(true);
    setMoveError(null);

    void enterRematch(match.id)
      .then(async (nextSession) => {
        await leaveMatchChannel(match.id).catch(() => undefined);
        applySession(nextSession);
      })
      .catch((error) => {
        rematchTransitionRef.current = false;
        setMoveError(messageFrom(error));
      })
      .finally(() => setActionBusy(false));
  }, [applySession, leaveMatchChannel, match, session]);

  useEffect(() => {
    if (state !== "connected" || !matchId) return;

    let disposed = false;

    void joinMatchChannel(matchId).catch((error) => {
      if (!disposed) {
        setMoveError(messageFrom(error));
      }
    });

    return () => {
      disposed = true;
    };
  }, [joinMatchChannel, matchId, state]);

  const ownUnit = useMemo(() => match?.units.find(
    (unit) => unit.ownerPlayerId === session?.playerId,
  ) ?? null, [match, session?.playerId]);

  useEffect(() => {
    if (!ownUnit) {
      setSelectedUnitId(null);
      return;
    }

    const selectedUnitStillAvailable = match?.units.some(
      (unit) => unit.id === selectedUnitId && unit.ownerPlayerId === session?.playerId,
    );

    if (selectedUnitStillAvailable) return;

    setSelectedUnitId(ownUnit.id);
    setSelection(selectionFrom(
      match?.terrainTiles ?? previewTerrainTiles,
      ownUnit.column,
      ownUnit.row,
    ));
  }, [match?.units, ownUnit, selectedUnitId, session?.playerId]);

  const handleCreate = async (playAgainstBot: boolean) => {
    setLobbyBusy(true);
    setLobbyError(null);

    try {
      applySession(await createMatch(matchName.trim(), playerName.trim(), playAgainstBot));
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
      applySession(await joinMatch(matchId, playerName.trim()));
    } catch (error) {
      setLobbyError(messageFrom(error));
    } finally {
      setLobbyBusy(false);
    }
  };

  const handleBoardSelect = (nextSelection: TileSelection) => {
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
  const currentPlayer = match?.players.find((player) => player.id === session?.playerId) ?? null;
  const targetUnit = match?.units.find(
    (unit) => coordinateFrom(unit.column, unit.row) === selection.coordinate,
  ) ?? null;
  const isOwnTurn = Boolean(match && session && match.activePlayerId === session.playerId);
  const canActThisTurn = isOwnTurn && !turnExpired;
  const boardTerrainTiles = match?.terrainTiles ?? previewTerrainTiles;
  const boardVisibleCoordinates = useMemo(
    () => (match?.visibleCoordinates ?? previewTerrainTiles)
      .map((position) => coordinateFrom(position.column, position.row)),
    [match?.visibleCoordinates],
  );
  const selectedPosition = positionFrom(selection.coordinate);
  const selectedTerrain = findTerrainTile(boardTerrainTiles, selectedPosition.column, selectedPosition.row);
  const isSelectionVisible = boardVisibleCoordinates.includes(selection.coordinate);
  const selectedContact = match?.lastKnownEnemies
    ?.filter((contact) => coordinateFrom(contact.column, contact.row) === selection.coordinate)
    .sort((a, b) => b.lastSeenTurnNumber - a.lastSeenTurnNumber)[0];
  const deploymentCoordinates = useMemo(() => {
    if (match?.status !== "Deploying" || !currentPlayer) return [];

    const columns = currentPlayer.seat === 1 ? [0, 1] : [7, 8];
    return columns.flatMap((column) =>
      Array.from({ length: 7 }, (_, row) => coordinateFrom(column, row)));
  }, [currentPlayer, match?.status]);
  const moveTargets = useMemo(() => {
    if (!match || !session || !selectedUnit || selectedUnit.ownerPlayerId !== session.playerId) {
      return [];
    }

    if (match.status === "Deploying" && currentPlayer && !currentPlayer.isReady) {
      const occupied = new Set(match.units.map((unit) => coordinateFrom(unit.column, unit.row)));
      return deploymentCoordinates.filter((coordinate) => !occupied.has(coordinate));
    }

    if (match.status !== "InProgress" || !canActThisTurn || selectedUnit.remainingMovement <= 0) {
      return [];
    }

    return adjacentPositions(selectedUnit.column, selectedUnit.row)
      .filter((position) => !match.units.some(
        (unit) => unit.column === position.column && unit.row === position.row,
      ))
      .filter((position) =>
        findTerrainTile(boardTerrainTiles, position.column, position.row).movementCost
          <= selectedUnit.remainingMovement)
      .map((position) => coordinateFrom(position.column, position.row));
  }, [
    boardTerrainTiles,
    currentPlayer,
    deploymentCoordinates,
    canActThisTurn,
    match,
    selectedUnit,
    session,
  ]);
  const attackTargets = useMemo(() => {
    if (
      !match
      || !session
      || match.status !== "InProgress"
      || !canActThisTurn
      || !selectedUnit
      || selectedUnit.ownerPlayerId !== session.playerId
      || selectedUnit.hasAttackedThisTurn
    ) {
      return [];
    }

    return adjacentPositions(selectedUnit.column, selectedUnit.row)
      .map((position) => match.units.find(
        (unit) => unit.column === position.column && unit.row === position.row,
      ))
      .filter((unit): unit is UnitSnapshot =>
        Boolean(unit && unit.ownerPlayerId !== session.playerId))
      .map((unit) => coordinateFrom(unit.column, unit.row));
  }, [canActThisTurn, match, selectedUnit, session]);
  const targetingActive = Boolean(
    selectedUnit
    && selectedUnit.ownerPlayerId === session?.playerId
    && (
      (match?.status === "Deploying" && currentPlayer && !currentPlayer.isReady)
      || (match?.status === "InProgress" && canActThisTurn)
    ),
  );
  const isOpenMoveTarget = Boolean(
    match?.status === "InProgress"
    && canActThisTurn
    && selectedUnit
    && selectedUnit.ownerPlayerId === session?.playerId
    && selectedUnit.remainingMovement > 0
    && areAdjacent(selectedUnit, selection)
    && !targetUnit
  );
  const hasEnoughMovement = Boolean(
    selectedUnit && selectedUnit.remainingMovement >= selection.movementCost,
  );
  const canMove = moveTargets.includes(selection.coordinate) && !actionBusy;
  const insufficientMovement = isOpenMoveTarget && !hasEnoughMovement;
  const canAttack = attackTargets.includes(selection.coordinate) && !actionBusy;

  const performPlacement = async (targetSelection: TileSelection) => {
    if (
      !match
      || !session
      || !selectedUnit
      || match.status !== "Deploying"
      || !moveTargets.includes(targetSelection.coordinate)
      || actionBusy
    ) return;

    const target = positionFrom(targetSelection.coordinate);
    setActionBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = await placeUnit({
        matchId: match.id,
        unitId: selectedUnit.id,
        targetColumn: target.column,
        targetRow: target.row,
        expectedVersion: match.version,
      });
      applyMatchUpdate(updatedMatch);
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const performBattleAction = async (targetSelection: TileSelection) => {
    if (!match || !session || !selectedUnit || actionBusy) return;

    const canMoveToTarget = moveTargets.includes(targetSelection.coordinate);
    const canAttackTarget = attackTargets.includes(targetSelection.coordinate);
    if (!canMoveToTarget && !canAttackTarget) return;

    const target = positionFrom(targetSelection.coordinate);
    const clickedTargetUnit = match.units.find(
      (unit) => coordinateFrom(unit.column, unit.row) === targetSelection.coordinate,
    ) ?? null;
    setActionBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = canAttackTarget && clickedTargetUnit
        ? await attackUnit({
          matchId: match.id,
          attackerUnitId: selectedUnit.id,
          targetUnitId: clickedTargetUnit.id,
          expectedVersion: match.version,
        })
        : await moveUnit({
          matchId: match.id,
          unitId: selectedUnit.id,
          targetColumn: target.column,
          targetRow: target.row,
          expectedVersion: match.version,
        });
      applyMatchUpdate(updatedMatch);
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const handleAction = async () => {
    await performBattleAction(selection);
  };

  const handleBoardActivate = (nextSelection: TileSelection) => {
    handleBoardSelect(nextSelection);

    const clickedUnit = match?.units.find(
      (unit) => coordinateFrom(unit.column, unit.row) === nextSelection.coordinate,
    );

    if (clickedUnit?.ownerPlayerId === session?.playerId) {
      return;
    }

    if (match?.status === "Deploying") {
      void performPlacement(nextSelection);
    } else if (match?.status === "InProgress") {
      void performBattleAction(nextSelection);
    }
  };

  const handleReady = async () => {
    if (
      !match
      || !session
      || match.status !== "Deploying"
      || currentPlayer?.isReady
      || actionBusy
    ) return;

    setActionBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = await readyForBattle(match.id, match.version);
      applyMatchUpdate(updatedMatch);
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const handleEndTurn = async () => {
    if (!match || !session || !canActThisTurn || match.status !== "InProgress" || actionBusy) return;

    setActionBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = await endTurn(match.id, match.version);
      applyMatchUpdate(updatedMatch);
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const handleRequestRematch = async () => {
    if (!match || match.status !== "Completed" || actionBusy) return;

    setActionBusy(true);
    setMoveError(null);

    try {
      const updatedMatch = await requestRematch(match.id, match.version);
      applyMatchUpdate(updatedMatch);
    } catch (error) {
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const handleAcceptRematch = async () => {
    if (!match || match.status !== "Completed" || actionBusy) return;

    rematchTransitionRef.current = true;
    setActionBusy(true);
    setMoveError(null);

    try {
      const nextSession = await acceptRematch(match.id, match.version);
      await leaveMatchChannel(match.id).catch(() => undefined);
      applySession(nextSession);
    } catch (error) {
      rematchTransitionRef.current = false;
      setMoveError(messageFrom(error));
    } finally {
      setActionBusy(false);
    }
  };

  const resetLocalGame = () => {
    setSession(null);
    setSelectedUnitId(null);
    setRecentEvent(null);
    setMoveError(null);
    eventMatchIdRef.current = null;
    lastEventSequenceRef.current = 0;
    rematchTransitionRef.current = false;
    setSelection(selectionFrom(previewTerrainTiles, 3, 3));
  };

  const handleLeave = async () => {
    setActionBusy(true);
    setLobbyError(null);

    if (match) {
      await leaveMatchChannel(match.id).catch(() => undefined);
    }

    try {
      await clearPlayerSession();
      resetLocalGame();
      await refreshMatches();
    } catch (error) {
      resetLocalGame();
      setLobbyError(`Oturum kapatılamadı: ${messageFrom(error)}`);
    } finally {
      setActionBusy(false);
    }
  };

  const handleNewGame = async () => {
    if (!match || match.status !== "Completed" || !currentPlayer || actionBusy) return;

    const nextMatchName = match.name;
    const nextPlayerName = currentPlayer.name;
    setActionBusy(true);
    setLobbyError(null);
    setMoveError(null);

    await leaveMatchChannel(match.id).catch(() => undefined);

    try {
      await clearPlayerSession();
      const nextSession = await createMatch(nextMatchName, nextPlayerName);
      applySession(nextSession);
    } catch (error) {
      resetLocalGame();
      await refreshMatches();
      setLobbyError(`Yeni oyun oluşturulamadı: ${messageFrom(error)}`);
    } finally {
      setActionBusy(false);
    }
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
      id: unit.id,
      coordinate: coordinateFrom(unit.column, unit.row),
      side: unit.ownerPlayerId === session.playerId ? "friendly" : "hostile",
      selected: unit.id === selectedUnitId,
      type: unit.type,
      health: unit.health,
      maximumHealth: unit.maximumHealth,
      remainingMovement: unit.remainingMovement,
      isRevealedByAttack: unit.isRevealedByAttack,
    }));
  }, [match, selectedUnitId, session]);

  const recentEventNotice = recentEvent && match
    ? describeMatchEvent(recentEvent, match.players, session?.playerId).title
    : null;

  const activePlayer = match?.players.find((player) => player.id === match.activePlayerId);
  const winner = match?.players.find((player) => player.id === match.winnerPlayerId);
  const actionLabel = match?.status === "WaitingForPlayers"
    ? "Rakip bekleniyor"
    : match?.status === "Deploying"
      ? currentPlayer?.isReady
        ? "Rakip hazırlanıyor"
        : "Birlik yerleştir"
    : match?.status === "Completed"
      ? `${winner?.name ?? "Oyuncu"} kazandı`
      : turnExpired
        ? "Tur devrediliyor…"
      : isOwnTurn
        ? canAttack
          ? "Saldır"
          : canMove
            ? "Hareket et"
            : insufficientMovement
              ? "Hareket yetersiz"
            : "Hedef seç"
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
        {match?.status === "Completed" && session ? (
          <AfterActionReport
            match={match}
            viewerPlayerId={session.playerId}
            busy={actionBusy}
            error={moveError}
            onRequestRematch={() => void handleRequestRematch()}
            onAcceptRematch={() => void handleAcceptRematch()}
            onNewGame={() => void handleNewGame()}
            onLobby={() => void handleLeave()}
          />
        ) : (
          <>
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
            terrainTiles={boardTerrainTiles}
            units={boardUnits}
            lastKnownEnemies={match?.lastKnownEnemies ?? []}
            specialTiles={match?.revealedSpecialTiles ?? []}
            moveTargets={moveTargets}
            attackTargets={attackTargets}
            deploymentCoordinates={deploymentCoordinates}
            visibleCoordinates={boardVisibleCoordinates}
            targetingActive={targetingActive}
            recentEvent={recentEvent}
            eventNotice={recentEventNotice}
            onSelect={handleBoardSelect}
            onActivate={handleBoardActivate}
          />

          <footer className={styles.mapFooter}>
            <div>
              <span className={styles.dataLabel}>Seçili bölge</span>
              <strong>{selection.coordinate}</strong>
              {match && (
                <small className={styles.visionStatus} data-visible={isSelectionVisible}>
                  {isSelectionVisible ? "Görüşte" : "Görüş dışında"}
                </small>
              )}
            </div>
            <div>
              <span className={styles.dataLabel}>Arazi</span>
              <strong>{terrainLabels[selection.terrain]}</strong>
              <small className={styles.terrainVisibility}>
                {selectedTerrain.blocksVision ? "Görüşü keser" : "Görüşü kesmez"}
              </small>
            </div>
            <div>
              <span className={styles.dataLabel}>Hareket maliyeti</span>
              <strong>{selection.movementCost} puan</strong>
            </div>
            <div>
              <span className={styles.dataLabel}>Arazi savunması</span>
              <strong>{selection.defenseBonus > 0 ? `+${selection.defenseBonus} koruma` : "Koruma yok"}</strong>
            </div>
            {selectedUnit && (
              <div className={styles.unitReadout}>
                <span className={styles.dataLabel}>Seçili birlik</span>
                <strong>{unitTypeLabels[selectedUnit.type]}</strong>
                <small>
                  Can {selectedUnit.health}/{selectedUnit.maximumHealth}
                  {" · "}Hareket {selectedUnit.remainingMovement}/{selectedUnit.movementAllowance}
                  {" · "}Saldırı {selectedUnit.attackPower}
                  {" · "}Görüş {selectedUnit.visionRange}
                </small>
                {selectedUnit.isRevealedByAttack && (
                  <small className={styles.reconStatus}>! Ateş açtın · rakibin turu bitene kadar görünürsün.</small>
                )}
              </div>
            )}
            {selectedContact && (
              <div className={styles.unitReadout}>
                <span className={styles.dataLabel}>Son görülen düşman</span>
                <strong>? {unitTypeLabels[selectedContact.unitType]}</strong>
                <small className={styles.reconStatus}>
                  T{String(selectedContact.lastSeenTurnNumber).padStart(2, "0")} · Güncel konumu bilinmiyor
                </small>
              </div>
            )}
            {targetUnit?.isRevealedByAttack && targetUnit.ownerPlayerId !== session?.playerId && (
              <div className={styles.unitReadout}>
                <span className={styles.dataLabel}>Açığa çıkan düşman</span>
                <strong>! {unitTypeLabels[targetUnit.type]}</strong>
                <small className={styles.reconStatus}>Senin turun bitene kadar görünür.</small>
              </div>
            )}
            <p>{!match
              ? "Çağrı adını belirle; yeni oyun oluştur veya açık bir operasyona katıl."
              : match.status === "WaitingForPlayers"
                ? "İkinci oyuncu katıldığında birlik yerleştirme başlayacak."
                : match.status === "Completed"
                  ? "Sonuç kaydedildi. Sağdaki Yeni oyun oluştur düğmesiyle yeni bir operasyona geçebilirsin."
                : match.status === "Deploying"
                  ? currentPlayer?.isReady
                    ? "Yerleşimin kilitlendi. Rakibin hazır olmasını bekliyorsun."
                    : "Birliğini seç; kehribar başlangıç alanındaki boş hücreye tıkla."
                  : selectedContact
                    ? "Bu işaret eski bir keşif kaydıdır; burada hâlâ asker olduğu anlamına gelmez. Saldırılamaz; hücreyi yeniden görerek doğrula."
                  : !isSelectionVisible
                    ? "Bu hücre mesafe veya arazi nedeniyle görüş dışında. Farklı bir açıdan yaklaşarak keşfet."
                    : "Orman ve tepe arkasını gizler. Açık görüş hattındaki düşmanları görebilirsin."}</p>
          </footer>
        </section>

        {!match ? (
          <LobbyPanel
            playerName={playerName}
            matchName={matchName}
            matches={matches}
            busy={lobbyBusy || sessionRestoring}
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
              <p className={styles.eyebrow}>{match.status === "Completed"
                ? "Maç sonucu"
                : match.status === "Deploying"
                  ? "Yerleşim aşaması"
                  : "Aktif tur"}</p>
              <p className={styles.turnNumber}>
                {match.status === "Deploying" ? "00" : match.turnNumber.toString().padStart(2, "0")}
              </p>
              <h2>{match.status === "Completed"
                ? `${winner?.name ?? "Oyuncu"} kazandı`
                : match.status === "Deploying"
                  ? currentPlayer?.isReady
                    ? "Yerleşim kilitlendi"
                    : "Birliklerini konuşlandır"
                : activePlayer
                  ? `${activePlayer.name} hareket ediyor`
                  : "Rakip bekleniyor"}</h2>
              <p className={styles.muted}>{match.status === "Completed"
                ? winner?.id === session?.playerId
                  ? "Zafer kaydedildi. Aynı adla yeni bir oyun açabilir veya lobiye dönebilirsin."
                  : "Maç tamamlandı. Yeni bir oyun açabilir veya lobiye dönüp başka bir operasyona katılabilirsin."
                : match.status === "Deploying"
                  ? currentPlayer?.isReady
                    ? "Rakibin yerleşimini tamamladığında savaş otomatik başlayacak."
                    : "Birliği seç, başlangıç alanındaki boş hücreye tıkla; dizilişin bitince hazır olduğunu bildir."
                  : isOwnTurn
                    ? "Birliği seç; aydınlatılmış hedefe tıkla. Hazır olduğunda turu bitir."
                    : match.status === "WaitingForPlayers"
                      ? "İkinci oyuncu katıldığında yerleşim aşaması başlayacak."
                      : "Güncelleme SignalR üzerinden otomatik gelecek."}</p>
              {match.status === "InProgress" && (
                <TurnTimer
                  remainingSeconds={remainingSeconds}
                  durationSeconds={match.turnDurationSeconds}
                  isOwnTurn={isOwnTurn}
                  connected={state === "connected"}
                />
              )}
              {moveError && <p className={styles.actionError} role="alert">{moveError}</p>}
              <div className={styles.actionButtons}>
                {match.status === "Completed" ? (
                  <>
                    <button
                      className={styles.readyButton}
                      type="button"
                      disabled={actionBusy}
                      onClick={() => void handleNewGame()}
                    >
                      Yeni oyun oluştur
                    </button>
                    <button
                      className={styles.endTurnButton}
                      type="button"
                      disabled={actionBusy}
                      onClick={() => void handleLeave()}
                    >
                      Lobiye dön
                    </button>
                  </>
                ) : match.status === "Deploying" ? (
                  <button
                    className={styles.readyButton}
                    type="button"
                    disabled={Boolean(currentPlayer?.isReady) || actionBusy}
                    onClick={() => void handleReady()}
                  >
                    {currentPlayer?.isReady ? "Hazırsın · rakip bekleniyor" : "Hazırım · yerleşimi kilitle"}
                  </button>
                ) : (
                  <>
                    <button
                      type="button"
                      disabled={!canMove && !canAttack}
                      onClick={() => void handleAction()}
                    >
                      {actionLabel}
                    </button>
                    <button
                      className={styles.endTurnButton}
                      type="button"
                      disabled={!canActThisTurn || match.status !== "InProgress" || actionBusy}
                      onClick={() => void handleEndTurn()}
                    >
                      Turu bitir
                    </button>
                  </>
                )}
              </div>
            </section>

            <MatchEventLog
              events={match.events ?? []}
              players={match.players}
              viewerPlayerId={session?.playerId ?? ""}
            />

            <section className={styles.systemCard}>
              <header>
                <p className={styles.eyebrow}>Oyuncular</p>
                <span className={styles.statusTag}>{match.players.length}/2 oyuncu</span>
              </header>
              <dl>
                {match.players.map((player) => (
                  <div key={player.id}>
                    <dt>{player.seat === 1 ? "Batı birliği" : "Doğu birliği"}</dt>
                    <dd>
                      {player.name}{player.isBot ? " · BOT" : ""}{player.id === session?.playerId ? " · sen" : ""}
                      {match.status === "Deploying" && (
                        <span className={player.isReady ? styles.playerReady : styles.playerPreparing}>
                          {player.isReady ? "hazır" : "yerleşiyor"}
                        </span>
                      )}
                    </dd>
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
              <p className={styles.objective}>
                Bir birliği rakip kenara ulaştır veya tüm düşman birliklerini yok et.
              </p>
              <ul>
                <li><span className={styles.friendlyMarker} />Senin birliğin</li>
                <li><span className={styles.hostileMarker} />Rakip birlik</li>
                <li><span className={styles.selectionMarker} />Hedef bölge</li>
                <li><span className={styles.moveTargetMarker} />Ulaşılabilir hücre</li>
                <li><span className={styles.attackTargetMarker} />Saldırı hedefi</li>
                <li><span className={styles.deploymentMarker} />Yerleşim alanı</li>
                <li><span className={styles.fogMarker} />Görüş dışı bölge</li>
                <li><span className={styles.contactMarker}>?</span>Son görülen · konum kesin değil</li>
                <li><span className={styles.exposedMarker}>!</span>Ateş açtı · geçici olarak görünür</li>
                <li><span className={styles.reinforcementMarker}>+</span>Keşfedilmiş takviye</li>
                <li><span className={styles.mineMarker}>!</span>Keşfedilmiş mayın</li>
                <li>İ / P / Z · İzci / Piyade / Zırhlı</li>
              </ul>
              <p className={styles.terrainHeading}>Arazi / hareket · savunma</p>
              <ul className={styles.terrainLegend}>
                <li><span className={`${styles.terrainMarker} ${styles.plainMarker}`} />Ova <b>1 · +0</b></li>
                <li><span className={`${styles.terrainMarker} ${styles.forestMarker}`} />Orman <b>2 · +1</b></li>
                <li><span className={`${styles.terrainMarker} ${styles.hillMarker}`} />Tepe <b>2 · +1</b></li>
                <li><span className={`${styles.terrainMarker} ${styles.marshMarker}`} />Bataklık <b>3 · +0</b></li>
              </ul>
              <p className={styles.visionNote}>
                Piyade ve zırhlı 1, izci 3 hücre uzağı görür. Orman ve tepe görüşü keser.
                Ateş açan birlik, rakibinin sonraki turu bitene kadar görünür.
                Kesik çizgili ? işareti yalnızca son görülen yeri ve turu gösterir.
              </p>
            </section>
          </aside>
        )}
          </>
        )}
      </main>

      <footer className={styles.commandBar}>
        <span className={styles.dataLabel}>Komut kanalı</span>
        <div className={styles.commandHint}><kbd>Sol tık</kbd><span>Seç / uygula</span></div>
        <div className={styles.commandHint}><kbd>Oklar</kbd><span>Klavyeyle ilerle</span></div>
        <div className={styles.commandHint}><kbd>Enter</kbd><span>Komutu uygula</span></div>
        <span className={styles.buildMark}>NET/10 · SIGNALR · POSTGRESQL</span>
      </footer>
    </div>
  );
}

export default App;
