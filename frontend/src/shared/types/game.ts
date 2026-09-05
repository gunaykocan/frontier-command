export type MatchStatus =
  | "WaitingForPlayers"
  | "Deploying"
  | "InProgress"
  | "Completed"
  | "Abandoned";

export type PlayerSnapshot = {
  id: string;
  name: string;
  seat: number;
  isReady: boolean;
  isBot: boolean;
};

export type UnitType = "Scout" | "Infantry" | "Armor";

export type SpecialTileType = "Reinforcement" | "Mine";

export type TerrainType = "Plain" | "Forest" | "Hill" | "Marsh";

export type TerrainTileSnapshot = {
  column: number;
  row: number;
  type: TerrainType;
  movementCost: number;
  defenseBonus: number;
  blocksVision: boolean;
};

export type VisibleCoordinateSnapshot = {
  column: number;
  row: number;
};

export type UnitSnapshot = {
  id: string;
  ownerPlayerId: string;
  type: UnitType;
  health: number;
  maximumHealth: number;
  remainingMovement: number;
  movementAllowance: number;
  attackPower: number;
  visionRange: number;
  hasAttackedThisTurn: boolean;
  isRevealedByAttack: boolean;
  column: number;
  row: number;
};

// Observed history, not a live unit: deliberately no health or movement fields.
export type LastKnownEnemySnapshot = {
  unitId: string;
  unitType: UnitType;
  column: number;
  row: number;
  lastSeenTurnNumber: number;
};

export type SpecialTileSnapshot = {
  type: SpecialTileType;
  column: number;
  row: number;
};

export type MatchEventType =
  | "DeploymentStarted"
  | "UnitPlaced"
  | "PlayerReady"
  | "BattleStarted"
  | "UnitMoved"
  | "ReinforcementTriggered"
  | "MineTriggered"
  | "UnitAttacked"
  | "TurnEnded"
  | "TurnTimedOut"
  | "MatchCompleted"
  | "RematchRequested"
  | "RematchAccepted";

export type MatchEventSnapshot = {
  sequence: number;
  type: MatchEventType;
  turnNumber: number;
  actorPlayerId: string | null;
  relatedPlayerId: string | null;
  unitId: string | null;
  unitType: UnitType | null;
  targetUnitId: string | null;
  targetUnitType: UnitType | null;
  fromColumn: number | null;
  fromRow: number | null;
  toColumn: number | null;
  toRow: number | null;
  amount: number | null;
  defenseBonus: number;
  wasDestroyed: boolean;
};

export type MatchSnapshot = {
  id: string;
  name: string;
  status: MatchStatus;
  turnNumber: number;
  version: number;
  activePlayerId: string | null;
  turnExpiresAtUtc: string | null;
  turnDurationSeconds: number;
  serverTimeUtc: string;
  winnerPlayerId: string | null;
  completedAtUtc: string | null;
  rematchRequestedByPlayerId: string | null;
  rematchMatchId: string | null;
  players: PlayerSnapshot[];
  terrainTiles: TerrainTileSnapshot[];
  visibleCoordinates: VisibleCoordinateSnapshot[];
  units: UnitSnapshot[];
  lastKnownEnemies: LastKnownEnemySnapshot[];
  revealedSpecialTiles: SpecialTileSnapshot[];
  events: MatchEventSnapshot[];
  createdAtUtc: string;
};

export type MatchSummary = {
  id: string;
  name: string;
  status: MatchStatus;
  turnNumber: number;
  version: number;
  playerCount: number;
  playerCapacity: number;
  createdAtUtc: string;
};

export type PlayerSession = {
  playerId: string;
  match: MatchSnapshot;
};

export type ProblemDetails = {
  title?: string;
  detail?: string;
  status?: number;
};
