import { useEffect, useRef } from "react";
import styles from "./TacticalBoard.module.css";

export type TileSelection = {
  coordinate: string;
  terrain: string;
};

type TacticalBoardProps = {
  selectedCoordinate: string;
  units: BoardUnit[];
  onSelect: (selection: TileSelection) => void;
};

export type BoardUnit = {
  coordinate: string;
  side: "friendly" | "hostile";
  selected: boolean;
};

type BoardCell = TileSelection & {
  x: number;
  y: number;
  radius: number;
};

const terrainPalette = {
  Sığlık: "#315f65",
  Çayır: "#466b59",
  Sırt: "#6d6650",
  Geçit: "#776348",
} as const;

function getTerrain(column: number, row: number): keyof typeof terrainPalette {
  const value = (column * 5 + row * 7) % 11;

  if (value < 2) return "Sığlık";
  if (value < 6) return "Çayır";
  if (value < 9) return "Sırt";
  return "Geçit";
}

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

function drawBoard(
  canvas: HTMLCanvasElement,
  width: number,
  height: number,
  selectedCoordinate: string,
  units: BoardUnit[],
): BoardCell[] {
  const context = canvas.getContext("2d");
  if (!context) return [];

  const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.round(width * pixelRatio);
  canvas.height = Math.round(height * pixelRatio);
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

  context.fillStyle = "#101d25";
  context.fillRect(0, 0, width, height);

  for (let row = 0; row < rows; row++) {
    for (let column = 0; column < columns; column++) {
      const x = offsetX + horizontalUnit * radius * (column + (row % 2) * 0.5);
      const y = offsetY + radius * 1.5 * row;
      const coordinate = `${String.fromCharCode(65 + column)}${row + 1}`;
      const terrain = getTerrain(column, row);
      const isSelected = coordinate === selectedCoordinate;

      cells.push({ coordinate, terrain, x, y, radius });
      drawHexagon(context, x, y, radius - 1.2);
      context.fillStyle = terrainPalette[terrain];
      context.globalAlpha = isSelected ? 1 : 0.78;
      context.fill();
      context.globalAlpha = 1;
      context.strokeStyle = isSelected ? "#efbb6d" : "rgba(222, 230, 221, 0.18)";
      context.lineWidth = isSelected ? 2.5 : 1;
      context.stroke();

      context.fillStyle = isSelected ? "#fff4dd" : "rgba(232, 227, 214, 0.48)";
      context.font = "10px Cascadia Mono, Consolas, monospace";
      context.textAlign = "center";
      context.fillText(coordinate, x, y + radius * 0.58);
    }
  }

  for (const unit of units) {
    const cell = cells.find((candidate) => candidate.coordinate === unit.coordinate);
    if (!cell) continue;

    context.save();
    context.translate(cell.x, cell.y - 3);
    context.beginPath();
    context.arc(0, 0, Math.max(7, radius * 0.24), 0, Math.PI * 2);
    context.fillStyle = unit.side === "friendly" ? "#d7e4dc" : "#c95757";
    context.fill();
    context.strokeStyle = unit.side === "friendly" ? "#183b43" : "#f4d2c8";
    context.lineWidth = 3;
    context.stroke();
    if (unit.selected) {
      context.beginPath();
      context.arc(0, 0, Math.max(12, radius * 0.34), 0, Math.PI * 2);
      context.strokeStyle = "#efbb6d";
      context.lineWidth = 2;
      context.stroke();
    }
    context.beginPath();
    context.moveTo(-radius * 0.12, 0);
    context.lineTo(radius * 0.12, 0);
    context.strokeStyle = unit.side === "friendly" ? "#183b43" : "#f4d2c8";
    context.lineWidth = 2;
    context.stroke();
    context.restore();
  }

  return cells;
}

export function TacticalBoard({
  selectedCoordinate,
  units,
  onSelect,
}: TacticalBoardProps) {
  const frameRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cellsRef = useRef<BoardCell[]>([]);

  useEffect(() => {
    const frame = frameRef.current;
    const canvas = canvasRef.current;
    if (!frame || !canvas) return;

    const render = () => {
      const bounds = frame.getBoundingClientRect();
      cellsRef.current = drawBoard(
        canvas,
        bounds.width,
        bounds.height,
        selectedCoordinate,
        units,
      );
    };

    const observer = new ResizeObserver(render);
    observer.observe(frame);
    render();

    return () => observer.disconnect();
  }, [selectedCoordinate, units]);

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
      onSelect({
        coordinate: closestCell.coordinate,
        terrain: closestCell.terrain,
      });
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLCanvasElement>) => {
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

    onSelect({
      coordinate: `${String.fromCharCode(65 + nextColumn)}${nextRow + 1}`,
      terrain: getTerrain(nextColumn, nextRow),
    });
  };

  return (
    <div className={styles.frame} ref={frameRef}>
      <canvas
        ref={canvasRef}
        className={styles.canvas}
        onPointerDown={handlePointerDown}
        onKeyDown={handleKeyDown}
        tabIndex={0}
        aria-label={`Etkileşimli taktik harita. Seçili bölge ${selectedCoordinate}. Ok tuşlarıyla bölge seçin.`}
      >
        Etkileşimli taktik harita bu tarayıcıda görüntülenemiyor.
      </canvas>
      <div className={styles.scanLine} aria-hidden="true" />
    </div>
  );
}
