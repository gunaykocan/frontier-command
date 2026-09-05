import type {
  TerrainTileSnapshot,
  TerrainType,
} from "../../shared/types/game";

export const terrainLabels: Record<TerrainType, string> = {
  Plain: "Ova",
  Forest: "Orman",
  Hill: "Tepe",
  Marsh: "Bataklık",
};

export const terrainPalette: Record<TerrainType, string> = {
  Plain: "#466b59",
  Forest: "#2f5948",
  Hill: "#71684f",
  Marsh: "#315f65",
};

const previewLayout: TerrainType[][] = [
  ["Plain", "Plain", "Hill", "Forest", "Marsh", "Forest", "Hill", "Plain", "Plain"],
  ["Plain", "Forest", "Plain", "Hill", "Plain", "Hill", "Plain", "Forest", "Plain"],
  ["Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain"],
  ["Plain", "Plain", "Plain", "Hill", "Marsh", "Hill", "Plain", "Plain", "Plain"],
  ["Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain", "Plain"],
  ["Plain", "Forest", "Plain", "Hill", "Plain", "Hill", "Plain", "Forest", "Plain"],
  ["Plain", "Plain", "Hill", "Forest", "Marsh", "Forest", "Hill", "Plain", "Plain"],
];

const previewMovementCosts: Record<TerrainType, number> = {
  Plain: 1,
  Forest: 2,
  Hill: 2,
  Marsh: 3,
};

const previewDefenseBonuses: Record<TerrainType, number> = {
  Plain: 0,
  Forest: 1,
  Hill: 1,
  Marsh: 0,
};

export const previewTerrainTiles: TerrainTileSnapshot[] = previewLayout.flatMap(
  (row, rowIndex) => row.map((type, columnIndex) => ({
    column: columnIndex,
    row: rowIndex,
    type,
    movementCost: previewMovementCosts[type],
    defenseBonus: previewDefenseBonuses[type],
    blocksVision: type === "Forest" || type === "Hill",
  })),
);

export function findTerrainTile(
  terrainTiles: TerrainTileSnapshot[] | undefined,
  column: number,
  row: number,
): TerrainTileSnapshot {
  return terrainTiles?.find((tile) => tile.column === column && tile.row === row) ?? {
    column,
    row,
    type: "Plain",
    movementCost: 1,
    defenseBonus: 0,
    blocksVision: false,
  };
}
