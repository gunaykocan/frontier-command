import { useEffect, useRef } from "react";
import type {
  LastKnownEnemySnapshot,
  MatchEventSnapshot,
  SpecialTileSnapshot,
  TerrainTileSnapshot,
  TerrainType,
  UnitType,
} from "../../shared/types/game";
import {
  findTerrainTile,
  terrainLabels,
  terrainPalette,
} from "./terrain";
import styles from "./TacticalBoard.module.css";

export type TileSelection = {
  coordinate: string;
  terrain: TerrainType;
  movementCost: number;
  defenseBonus: number;
};

type TacticalBoardProps = {
  selectedCoordinate: string;
  terrainTiles: TerrainTileSnapshot[];
  units: BoardUnit[];
  lastKnownEnemies: LastKnownEnemySnapshot[];
  specialTiles: SpecialTileSnapshot[];
  moveTargets: string[];
  attackTargets: string[];
  deploymentCoordinates: string[];
  visibleCoordinates: string[];
  targetingActive: boolean;
  recentEvent: MatchEventSnapshot | null;
  eventNotice: string | null;
  onSelect: (selection: TileSelection) => void;
  onActivate: (selection: TileSelection) => void;
};

export type BoardUnit = {
  id: string;
  coordinate: string;
  side: "friendly" | "hostile";
  selected: boolean;
  type: UnitType;
  health: number;
  maximumHealth: number;
  remainingMovement: number;
  isRevealedByAttack?: boolean;
};

type BoardCell = TileSelection & {
  x: number;
  y: number;
  radius: number;
};

const unitGlyphs: Record<UnitType, string> = {
  Scout: "İ",
  Infantry: "P",
  Armor: "Z",
};

function drawHexagon(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  radius: number,
): void {
  context.beginPath();

  for (let point = 0; point < 6; point++) {
    const angle = Math.PI / 180 * (60 * point - 30);
    const pointX = x + radius * Math.cos(angle);
    const pointY = y + radius * Math.sin(angle);

    if (point === 0) context.moveTo(pointX, pointY);
    else context.lineTo(pointX, pointY);
  }

  context.closePath();
}

function drawTerrainMark(
  context: CanvasRenderingContext2D,
  terrain: TerrainType,
  x: number,
  y: number,
  radius: number,
): void {
  context.save();
  context.translate(x, y);
  context.strokeStyle = "rgba(238, 233, 216, 0.24)";
  context.fillStyle = "rgba(7, 21, 26, 0.24)";
  context.lineWidth = Math.max(1, radius * 0.035);

  if (terrain === "Forest") {
    for (const [treeX, treeY] of [[-0.24, 0.08], [0, -0.18], [0.24, 0.08]]) {
      context.beginPath();
      context.moveTo(radius * treeX, radius * (treeY - 0.18));
      context.lineTo(radius * (treeX - 0.13), radius * (treeY + 0.1));
      context.lineTo(radius * (treeX + 0.13), radius * (treeY + 0.1));
      context.closePath();
      context.fill();
      context.stroke();
    }
  } else if (terrain === "Hill") {
    for (const scale of [0.72, 0.46]) {
      context.beginPath();
      context.ellipse(0, 0, radius * scale, radius * scale * 0.38, -0.18, 0, Math.PI * 2);
      context.stroke();
    }
  } else if (terrain === "Marsh") {
    for (const lineY of [-0.18, 0.04, 0.26]) {
      context.beginPath();
      context.moveTo(-radius * 0.48, radius * lineY);
      context.bezierCurveTo(
        -radius * 0.18,
        radius * (lineY - 0.11),
        radius * 0.18,
        radius * (lineY + 0.11),
        radius * 0.48,
        radius * lineY,
      );
      context.stroke();
    }
  } else {
    context.beginPath();
    context.moveTo(-radius * 0.35, radius * 0.08);
    context.lineTo(radius * 0.35, -radius * 0.08);
    context.stroke();
  }

  context.restore();
}

function adjacentBoardPositions(column: number, row: number): { column: number; row: number }[] {
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

function drawBoard(
  canvas: HTMLCanvasElement,
  width: number,
  height: number,
  selectedCoordinate: string,
  terrainTiles: TerrainTileSnapshot[],
  units: BoardUnit[],
  lastKnownEnemies: LastKnownEnemySnapshot[],
  specialTiles: SpecialTileSnapshot[],
  moveTargets: string[],
  attackTargets: string[],
  deploymentCoordinates: string[],
  visibleCoordinates: string[],
  targetingActive: boolean,
  recentEvent: MatchEventSnapshot | null,
  animationProgress: number,
): BoardCell[] {
  const context = canvas.getContext("2d");
  if (!context) return [];

  const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
  const pixelWidth = Math.max(1, Math.round(width * pixelRatio));
  const pixelHeight = Math.max(1, Math.round(height * pixelRatio));

  if (canvas.width !== pixelWidth) canvas.width = pixelWidth;
  if (canvas.height !== pixelHeight) canvas.height = pixelHeight;

  context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  context.clearRect(0, 0, width, height);

  const columns = 9;
  const rows = 7;
  const horizontalUnit = Math.sqrt(3);
  const radius = Math.max(
    19,
    Math.min(
      47,
      (width - 68) / (horizontalUnit * (columns + 0.5)),
      (height - 56) / (1.5 * (rows - 1) + 2),
    ),
  );
  const boardWidth = horizontalUnit * radius * (columns + 0.5);
  const boardHeight = radius * (1.5 * (rows - 1) + 2);
  const offsetX = (width - boardWidth) / 2 + horizontalUnit * radius / 2;
  const offsetY = (height - boardHeight) / 2 + radius;
  const cells: BoardCell[] = [];
  const moveTargetSet = new Set(moveTargets);
  const attackTargetSet = new Set(attackTargets);
  const deploymentCoordinateSet = new Set(deploymentCoordinates);
  const visibleCoordinateSet = new Set(visibleCoordinates);

  context.fillStyle = "#101d25";
  context.fillRect(0, 0, width, height);

  for (let row = 0; row < rows; row++) {
    for (let column = 0; column < columns; column++) {
      const x = offsetX + horizontalUnit * radius * (column + (row % 2) * 0.5);
      const y = offsetY + radius * 1.5 * row;
      const coordinate = `${String.fromCharCode(65 + column)}${row + 1}`;
      const terrainTile = findTerrainTile(terrainTiles, column, row);
      const terrain = terrainTile.type;
      const isSelected = coordinate === selectedCoordinate;
      const isMoveTarget = moveTargetSet.has(coordinate);
      const isAttackTarget = attackTargetSet.has(coordinate);
      const isDeploymentCoordinate = deploymentCoordinateSet.has(coordinate);
      const isInvalidTarget = targetingActive && !isSelected && !isMoveTarget && !isAttackTarget;

      cells.push({
        coordinate,
        terrain,
        movementCost: terrainTile.movementCost,
        defenseBonus: terrainTile.defenseBonus,
        x,
        y,
        radius,
      });
      drawHexagon(context, x, y, radius - 1.2);
      context.fillStyle = terrainPalette[terrain];
      context.globalAlpha = isSelected ? 1 : 0.82;
      context.fill();
      context.globalAlpha = 1;
      context.strokeStyle = isSelected
        ? "#efbb6d"
        : isAttackTarget
          ? "#e36c62"
          : isMoveTarget
            ? "#99d5bb"
            : isDeploymentCoordinate
              ? "rgba(227, 168, 87, 0.5)"
              : "rgba(222, 230, 221, 0.18)";
      context.lineWidth = isSelected || isMoveTarget || isAttackTarget ? 2.5 : 1;
      context.setLineDash(isDeploymentCoordinate && !isSelected && !isMoveTarget ? [4, 4] : []);
      context.stroke();
      context.setLineDash([]);
      drawTerrainMark(context, terrain, x, y - radius * 0.08, radius * 0.52);

      context.fillStyle = "rgba(7, 21, 26, 0.52)";
      context.beginPath();
      context.arc(x + radius * 0.49, y - radius * 0.42, Math.max(6, radius * 0.13), 0, Math.PI * 2);
      context.fill();
      context.fillStyle = "rgba(239, 235, 218, 0.78)";
      context.font = `700 ${Math.max(8, radius * 0.16)}px Cascadia Mono, Consolas, monospace`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText(String(terrainTile.movementCost), x + radius * 0.49, y - radius * 0.42);

      context.fillStyle = isSelected ? "#fff4dd" : "rgba(232, 227, 214, 0.48)";
      context.font = "10px Cascadia Mono, Consolas, monospace";
      context.textAlign = "center";
      context.textBaseline = "alphabetic";
      context.fillText(coordinate, x, y + radius * 0.58);

      if (isMoveTarget || isAttackTarget) {
        drawHexagon(context, x, y, radius - 4);
        context.fillStyle = isAttackTarget
          ? "rgba(201, 87, 87, 0.16)"
          : "rgba(118, 182, 139, 0.16)";
        context.fill();
      } else if (isInvalidTarget) {
        drawHexagon(context, x, y, radius - 1.8);
        context.fillStyle = "rgba(4, 13, 18, 0.52)";
        context.fill();
      }

      if (!visibleCoordinateSet.has(coordinate)) {
        drawHexagon(context, x, y, radius - 1.2);
        context.fillStyle = "rgba(4, 13, 18, 0.8)";
        context.fill();
        context.strokeStyle = "rgba(91, 116, 119, 0.16)";
        context.lineWidth = 1;
        context.stroke();

        if (isSelected) {
          context.strokeStyle = "#e3a857";
          context.lineWidth = 2;
          context.setLineDash([3, 4]);
          context.stroke();
          context.setLineDash([]);
        }
      }
    }
  }

  for (const cell of cells) {
    if (!visibleCoordinateSet.has(cell.coordinate)) continue;
    const column = cell.coordinate.charCodeAt(0) - 65;
    const row = Number.parseInt(cell.coordinate.slice(1), 10) - 1;
    const touchesFog = adjacentBoardPositions(column, row)
      .some((position) => !visibleCoordinateSet.has(
        `${String.fromCharCode(65 + position.column)}${position.row + 1}`,
      ));

    if (!touchesFog) continue;
    drawHexagon(context, cell.x, cell.y, radius - 3.4);
    context.strokeStyle = "rgba(153, 213, 187, 0.36)";
    context.lineWidth = Math.max(1.2, radius * 0.035);
    context.setLineDash([2, 5]);
    context.stroke();
    context.setLineDash([]);
  }

  for (const specialTile of specialTiles) {
    const coordinate = `${String.fromCharCode(65 + specialTile.column)}${specialTile.row + 1}`;
    const cell = cells.find((candidate) => candidate.coordinate === coordinate);
    if (!cell) continue;

    context.save();
    context.translate(cell.x - radius * 0.48, cell.y - radius * 0.4);
    context.lineWidth = Math.max(1.5, radius * 0.045);

    if (specialTile.type === "Reinforcement") {
      context.rotate(Math.PI / 4);
      context.fillStyle = "rgba(11, 35, 39, 0.9)";
      context.strokeStyle = "rgba(153, 213, 187, 0.92)";
      context.fillRect(-radius * 0.13, -radius * 0.13, radius * 0.26, radius * 0.26);
      context.strokeRect(-radius * 0.13, -radius * 0.13, radius * 0.26, radius * 0.26);
      context.rotate(-Math.PI / 4);
      context.beginPath();
      context.moveTo(-radius * 0.07, 0);
      context.lineTo(radius * 0.07, 0);
      context.moveTo(0, -radius * 0.07);
      context.lineTo(0, radius * 0.07);
      context.stroke();
    } else {
      context.beginPath();
      context.moveTo(0, -radius * 0.15);
      context.lineTo(radius * 0.16, radius * 0.14);
      context.lineTo(-radius * 0.16, radius * 0.14);
      context.closePath();
      context.fillStyle = "rgba(53, 24, 25, 0.92)";
      context.strokeStyle = "rgba(227, 108, 98, 0.94)";
      context.fill();
      context.stroke();
      context.beginPath();
      context.arc(0, radius * 0.05, Math.max(1.5, radius * 0.035), 0, Math.PI * 2);
      context.fillStyle = "#ffd8c9";
      context.fill();
    }

    context.restore();
  }

  // Historical contacts are separate from real units and never become action targets.
  const drawnContacts = new Set<string>();
  for (const contact of [...lastKnownEnemies].sort((a, b) => b.lastSeenTurnNumber - a.lastSeenTurnNumber)) {
    const coordinate = `${String.fromCharCode(65 + contact.column)}${contact.row + 1}`;
    const cell = cells.find((candidate) => candidate.coordinate === coordinate);
    if (!cell || visibleCoordinateSet.has(coordinate) || drawnContacts.has(coordinate)) continue;
    drawnContacts.add(coordinate);

    context.save();
    context.translate(cell.x, cell.y - radius * 0.12);
    context.beginPath();
    context.arc(0, 0, Math.max(11, radius * 0.31), 0, Math.PI * 2);
    context.fillStyle = "rgba(227, 168, 87, 0.08)";
    context.fill();
    context.strokeStyle = "#e3a857";
    context.lineWidth = 1.5;
    context.setLineDash([3, 3]);
    context.stroke();
    context.setLineDash([]);
    context.fillStyle = "#efbb6d";
    context.font = `700 ${Math.max(13, radius * 0.34)}px Cascadia Mono, Consolas, monospace`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText("?", 0, 0);
    context.font = `${Math.max(8, radius * 0.19)}px Cascadia Mono, Consolas, monospace`;
    context.fillText(`T${String(contact.lastSeenTurnNumber).padStart(2, "0")}`, 0, radius * 0.52);
    context.restore();
  }

  for (const unit of units) {
    const cell = cells.find((candidate) => candidate.coordinate === unit.coordinate);
    if (!cell) continue;

    let unitX = cell.x;
    let unitY = cell.y;
    const isMovingUnit = (
      recentEvent?.type === "UnitMoved" || recentEvent?.type === "UnitPlaced"
    ) && recentEvent.unitId === unit.id
      && recentEvent.fromColumn !== null
      && recentEvent.fromRow !== null;

    if (isMovingUnit) {
      const fromCoordinate = `${String.fromCharCode(65 + recentEvent.fromColumn!)}${recentEvent.fromRow! + 1}`;
      const fromCell = cells.find((candidate) => candidate.coordinate === fromCoordinate);

      if (fromCell) {
        const easedProgress = 1 - Math.pow(1 - animationProgress, 3);
        unitX = fromCell.x + (cell.x - fromCell.x) * easedProgress;
        unitY = fromCell.y + (cell.y - fromCell.y) * easedProgress;

        context.save();
        context.beginPath();
        context.moveTo(fromCell.x, fromCell.y - 3);
        context.lineTo(unitX, unitY - 3);
        context.strokeStyle = unit.side === "friendly"
          ? `rgba(153, 213, 187, ${0.58 * (1 - animationProgress)})`
          : `rgba(227, 108, 98, ${0.58 * (1 - animationProgress)})`;
        context.lineWidth = Math.max(3, radius * 0.08);
        context.setLineDash([5, 5]);
        context.stroke();
        context.restore();
      }
    }

    context.save();
    context.translate(unitX, unitY - 3);
    context.shadowColor = unit.side === "friendly"
      ? "rgba(215, 228, 220, 0.34)"
      : "rgba(201, 87, 87, 0.36)";
    context.shadowBlur = Math.max(6, radius * 0.18);
    context.beginPath();
    context.arc(0, 0, Math.max(11, radius * 0.32), 0, Math.PI * 2);
    context.fillStyle = unit.side === "friendly" ? "#d7e4dc" : "#c95757";
    context.fill();
    context.shadowBlur = 0;
    context.strokeStyle = unit.side === "friendly" ? "#183b43" : "#f4d2c8";
    context.lineWidth = 3.5;
    context.stroke();
    if (unit.selected) {
      context.beginPath();
      context.arc(0, 0, Math.max(16, radius * 0.45), 0, Math.PI * 2);
      context.strokeStyle = "#efbb6d";
      context.lineWidth = 2.5;
      context.stroke();
    }
    context.fillStyle = unit.side === "friendly" ? "#183b43" : "#fff0e8";
    context.font = `700 ${Math.max(12, radius * 0.3)}px Cascadia Mono, Consolas, monospace`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(unitGlyphs[unit.type], 0, 0);

    const barWidth = radius * 0.72;
    const barHeight = Math.max(3, radius * 0.08);
    const healthRatio = Math.max(0, unit.health / unit.maximumHealth);
    context.fillStyle = "rgba(6, 16, 21, 0.72)";
    context.fillRect(-barWidth / 2, radius * 0.4, barWidth, barHeight);
    context.fillStyle = healthRatio > 0.5 ? "#76b68b" : healthRatio > 0.25 ? "#e3a857" : "#df695e";
    context.fillRect(-barWidth / 2, radius * 0.4, barWidth * healthRatio, barHeight);

    if (unit.side === "friendly" && unit.remainingMovement > 0) {
      context.beginPath();
      context.arc(-radius * 0.28, -radius * 0.22, Math.max(6, radius * 0.12), 0, Math.PI * 2);
      context.fillStyle = "#efbb6d";
      context.fill();
      context.fillStyle = "#18313a";
      context.font = `700 ${Math.max(8, radius * 0.17)}px Cascadia Mono, Consolas, monospace`;
      context.fillText(String(unit.remainingMovement), -radius * 0.28, -radius * 0.22);
    }
    if (unit.isRevealedByAttack) {
      context.beginPath();
      context.arc(radius * 0.31, -radius * 0.25, Math.max(6, radius * 0.14), 0, Math.PI * 2);
      context.fillStyle = "#efbb6d";
      context.fill();
      context.fillStyle = "#18313a";
      context.font = `800 ${Math.max(9, radius * 0.21)}px Cascadia Mono, Consolas, monospace`;
      context.fillText("!", radius * 0.31, -radius * 0.25);
    }
    context.restore();
  }

  if (
    recentEvent?.type === "UnitAttacked"
    && recentEvent.toColumn !== null
    && recentEvent.toRow !== null
  ) {
    const targetCoordinate = `${String.fromCharCode(65 + recentEvent.toColumn)}${recentEvent.toRow + 1}`;
    const targetCell = cells.find((candidate) => candidate.coordinate === targetCoordinate);

    if (targetCell && animationProgress < 1) {
      const pulse = Math.sin(animationProgress * Math.PI);
      context.save();
      context.beginPath();
      context.arc(
        targetCell.x,
        targetCell.y - 3,
        radius * (0.34 + pulse * 0.45),
        0,
        Math.PI * 2,
      );
      context.strokeStyle = `rgba(227, 108, 98, ${0.92 * (1 - animationProgress)})`;
      context.lineWidth = Math.max(2, radius * 0.08);
      context.stroke();
      context.fillStyle = `rgba(255, 216, 201, ${1 - animationProgress})`;
      context.font = `700 ${Math.max(15, radius * 0.38)}px Bahnschrift, Arial Narrow, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.shadowColor = "rgba(201, 87, 87, 0.82)";
      context.shadowBlur = 8;
      context.fillText(
        recentEvent.wasDestroyed ? "İMHA" : `-${recentEvent.amount ?? 0}`,
        targetCell.x,
        targetCell.y - radius * (0.44 + animationProgress * 0.42),
      );
      if (!recentEvent.wasDestroyed && recentEvent.defenseBonus > 0) {
        context.fillStyle = `rgba(153, 213, 187, ${1 - animationProgress})`;
        context.font = `700 ${Math.max(9, radius * 0.2)}px Cascadia Mono, Consolas, monospace`;
        context.fillText(
          `SİPER +${recentEvent.defenseBonus}`,
          targetCell.x,
          targetCell.y + radius * (0.48 + animationProgress * 0.2),
        );
      }
      context.restore();
    }
  }

  if (
    recentEvent?.type === "MineTriggered"
    && recentEvent.toColumn !== null
    && recentEvent.toRow !== null
  ) {
    const mineCoordinate = `${String.fromCharCode(65 + recentEvent.toColumn)}${recentEvent.toRow + 1}`;
    const mineCell = cells.find((candidate) => candidate.coordinate === mineCoordinate);

    if (mineCell && animationProgress < 1) {
      const pulse = Math.sin(animationProgress * Math.PI);
      context.save();
      for (const scale of [0.55, 0.82]) {
        context.beginPath();
        context.arc(mineCell.x, mineCell.y, radius * (scale + pulse * 0.42), 0, Math.PI * 2);
        context.strokeStyle = `rgba(227, 108, 98, ${(1 - animationProgress) * (1.2 - scale)})`;
        context.lineWidth = Math.max(2, radius * 0.065);
        context.stroke();
      }
      context.fillStyle = `rgba(255, 216, 201, ${1 - animationProgress})`;
      context.font = `700 ${Math.max(14, radius * 0.34)}px Bahnschrift, Arial Narrow, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.shadowColor = "rgba(201, 87, 87, 0.9)";
      context.shadowBlur = 10;
      context.fillText(
        recentEvent.wasDestroyed ? "MAYIN · İMHA" : `MAYIN · -${recentEvent.amount ?? 0}`,
        mineCell.x,
        mineCell.y - radius * (0.52 + animationProgress * 0.42),
      );
      context.restore();
    }
  }

  if (
    recentEvent?.type === "ReinforcementTriggered"
    && recentEvent.fromColumn !== null
    && recentEvent.fromRow !== null
    && recentEvent.toColumn !== null
    && recentEvent.toRow !== null
  ) {
    const sourceCoordinate = `${String.fromCharCode(65 + recentEvent.fromColumn)}${recentEvent.fromRow + 1}`;
    const targetCoordinate = `${String.fromCharCode(65 + recentEvent.toColumn)}${recentEvent.toRow + 1}`;
    const sourceCell = cells.find((candidate) => candidate.coordinate === sourceCoordinate);
    const targetCell = cells.find((candidate) => candidate.coordinate === targetCoordinate);

    if (sourceCell && targetCell && animationProgress < 1) {
      const pulse = Math.sin(animationProgress * Math.PI);
      context.save();
      context.beginPath();
      context.arc(sourceCell.x, sourceCell.y, radius * (0.38 + pulse * 0.58), 0, Math.PI * 2);
      context.strokeStyle = `rgba(153, 213, 187, ${0.9 * (1 - animationProgress)})`;
      context.lineWidth = Math.max(2, radius * 0.065);
      context.stroke();
      context.beginPath();
      context.moveTo(sourceCell.x, sourceCell.y);
      context.lineTo(targetCell.x, targetCell.y);
      context.setLineDash([4, 5]);
      context.strokeStyle = `rgba(227, 168, 87, ${0.72 * (1 - animationProgress)})`;
      context.stroke();
      context.setLineDash([]);
      context.fillStyle = `rgba(215, 228, 220, ${1 - animationProgress})`;
      context.font = `700 ${Math.max(14, radius * 0.34)}px Bahnschrift, Arial Narrow, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.shadowColor = "rgba(153, 213, 187, 0.8)";
      context.shadowBlur = 10;
      context.fillText(
        "TAKVİYE +1",
        targetCell.x,
        targetCell.y - radius * (0.52 + animationProgress * 0.32),
      );
      context.restore();
    }
  }

  return cells;
}

export function TacticalBoard({
  selectedCoordinate,
  terrainTiles,
  units,
  lastKnownEnemies,
  specialTiles,
  moveTargets,
  attackTargets,
  deploymentCoordinates,
  visibleCoordinates,
  targetingActive,
  recentEvent,
  eventNotice,
  onSelect,
  onActivate,
}: TacticalBoardProps) {
  const frameRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cellsRef = useRef<BoardCell[]>([]);

  useEffect(() => {
    const frame = frameRef.current;
    const canvas = canvasRef.current;
    if (!frame || !canvas) return;

    const shouldAnimate = Boolean(
      recentEvent
      && [
        "UnitMoved",
        "UnitPlaced",
        "UnitAttacked",
        "MineTriggered",
        "ReinforcementTriggered",
      ].includes(recentEvent.type)
      && !window.matchMedia("(prefers-reduced-motion: reduce)").matches,
    );
    let progress = shouldAnimate ? 0 : 1;
    let animationFrame: number | null = null;

    const render = () => {
      const bounds = canvas.getBoundingClientRect();
      cellsRef.current = drawBoard(
        canvas,
        bounds.width,
        bounds.height,
        selectedCoordinate,
        terrainTiles,
        units,
        lastKnownEnemies,
        specialTiles,
        moveTargets,
        attackTargets,
        deploymentCoordinates,
        visibleCoordinates,
        targetingActive,
        recentEvent,
        progress,
      );
    };

    const observer = new ResizeObserver(render);
    observer.observe(frame);
    render();

    if (shouldAnimate) {
      const startedAt = performance.now();
      const duration = ["UnitAttacked", "MineTriggered", "ReinforcementTriggered"]
        .includes(recentEvent?.type ?? "")
        ? 840
        : 620;
      const animate = (timestamp: number) => {
        progress = Math.min(1, (timestamp - startedAt) / duration);
        render();
        if (progress < 1) animationFrame = window.requestAnimationFrame(animate);
      };
      animationFrame = window.requestAnimationFrame(animate);
    }

    return () => {
      observer.disconnect();
      if (animationFrame !== null) window.cancelAnimationFrame(animationFrame);
    };
  }, [
    attackTargets,
    deploymentCoordinates,
    lastKnownEnemies,
    moveTargets,
    recentEvent,
    selectedCoordinate,
    specialTiles,
    targetingActive,
    terrainTiles,
    units,
    visibleCoordinates,
  ]);

  const handlePointerDown = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const bounds = event.currentTarget.getBoundingClientRect();
    const pointerX = event.clientX - bounds.left;
    const pointerY = event.clientY - bounds.top;
    const closestCell = cellsRef.current.reduce<BoardCell | null>((closest, cell) => {
      const distance = Math.hypot(cell.x - pointerX, cell.y - pointerY);
      const closestDistance = closest
        ? Math.hypot(closest.x - pointerX, closest.y - pointerY)
        : Number.POSITIVE_INFINITY;

      return distance < closestDistance ? cell : closest;
    }, null);

    if (closestCell && Math.hypot(closestCell.x - pointerX, closestCell.y - pointerY) <= closestCell.radius) {
      onActivate({
        coordinate: closestCell.coordinate,
        terrain: closestCell.terrain,
        movementCost: closestCell.movementCost,
        defenseBonus: closestCell.defenseBonus,
      });
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLCanvasElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      const selectedCell = cellsRef.current.find(
        (cell) => cell.coordinate === selectedCoordinate,
      );

      if (selectedCell) {
        event.preventDefault();
        onActivate({
          coordinate: selectedCell.coordinate,
          terrain: selectedCell.terrain,
          movementCost: selectedCell.movementCost,
          defenseBonus: selectedCell.defenseBonus,
        });
      }
      return;
    }

    const column = selectedCoordinate.charCodeAt(0) - 65;
    const row = Number.parseInt(selectedCoordinate.slice(1), 10) - 1;
    let nextColumn = column;
    let nextRow = row;

    if (event.key === "ArrowLeft") nextColumn--;
    else if (event.key === "ArrowRight") nextColumn++;
    else if (event.key === "ArrowUp") nextRow--;
    else if (event.key === "ArrowDown") nextRow++;
    else return;

    event.preventDefault();
    nextColumn = Math.min(8, Math.max(0, nextColumn));
    nextRow = Math.min(6, Math.max(0, nextRow));

    const nextTerrain = findTerrainTile(terrainTiles, nextColumn, nextRow);
    onSelect({
      coordinate: `${String.fromCharCode(65 + nextColumn)}${nextRow + 1}`,
      terrain: nextTerrain.type,
      movementCost: nextTerrain.movementCost,
      defenseBonus: nextTerrain.defenseBonus,
    });
  };

  const selectedContact = lastKnownEnemies
    .filter((contact) => `${String.fromCharCode(65 + contact.column)}${contact.row + 1}` === selectedCoordinate)
    .sort((a, b) => b.lastSeenTurnNumber - a.lastSeenTurnNumber)[0];

  return (
    <div className={styles.frame} ref={frameRef}>
      <canvas
        ref={canvasRef}
        className={styles.canvas}
        onPointerDown={handlePointerDown}
        onKeyDown={handleKeyDown}
        tabIndex={0}
        aria-label={`Etkileşimli taktik harita. Seçili bölge ${selectedCoordinate}, ${visibleCoordinates.includes(selectedCoordinate) ? "görüşte" : "görüş dışında"}. ${terrainLabels[findTerrainTile(terrainTiles, selectedCoordinate.charCodeAt(0) - 65, Number.parseInt(selectedCoordinate.slice(1), 10) - 1).type]} arazi. ${selectedContact ? `Son görülen düşman izi, tur ${selectedContact.lastSeenTurnNumber}. Güncel konumu bilinmiyor; saldırı hedefi değil. ` : ""}Orman ve tepe görüşü keser. Ok tuşlarıyla bölge seçin, Enter ile etkinleştirin.`}
      >
        Etkileşimli taktik harita bu tarayıcıda görüntülenemiyor.
      </canvas>
      <div className={styles.scanLine} aria-hidden="true" />
      {eventNotice && (
        <div
          key={recentEvent?.sequence}
          className={styles.eventNotice}
          data-type={recentEvent?.type}
          role="status"
        >
          <span>SAHA VERİSİ</span>
          <strong>{eventNotice}</strong>
        </div>
      )}
    </div>
  );
}
