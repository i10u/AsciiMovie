import { AmFlags, AmHeader } from "./types.js";
import { getCharIndex, getColor } from "./AmFrameLayout.js";

const REF_FONT_SIZE = 64;

export class AmRenderer {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private fontFamily: string;
  private cellWidth = 0;
  private cellHeight = 0;

  constructor(container: HTMLElement, _fontSize = 12, fontFamily = "monospace") {
    this.canvas = document.createElement("canvas");
    this.canvas.style.display = "block";
    this.canvas.style.background = "#000";
    container.appendChild(this.canvas);
    const ctx = this.canvas.getContext("2d");
    if (!ctx) {
      throw new Error("Canvas 2D context unavailable.");
    }
    this.ctx = ctx;
    this.fontFamily = fontFamily;
  }

  resizeToContainer(container: HTMLElement): void {
    const width = container.clientWidth || 960;
    const height = container.clientHeight || 540;
    this.canvas.width = width;
    this.canvas.height = height;
  }

  render(header: AmHeader, frameData: Uint8Array): void {
    const { cols, rows, charset } = header;
    const color = (header.flags & AmFlags.Color) !== 0;

    this.cellWidth = this.canvas.width / cols;
    this.cellHeight = this.canvas.height / rows;
    this.ctx.fillStyle = "#000";
    this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);

    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        const cellIndex = row * cols + col;
        const charIndex = getCharIndex(frameData, cellIndex, header, AmFlags.Color);
        let cssColor: string;

        if (color) {
          const [r, g, b] = getColor(frameData, cellIndex, header, AmFlags.Color);
          cssColor = rgbToCss(r, g, b);
        } else {
          const gray = Math.round((charIndex * 255) / Math.max(1, charset.length - 1));
          cssColor = rgbToCss(gray, gray, gray);
        }

        if (charIndex >= charset.length) continue;
        const ch = charset[charIndex];
        if (ch === " ") continue;

        this.ctx.fillStyle = cssColor;
        drawCellFilled(
          this.ctx,
          ch,
          col * this.cellWidth,
          row * this.cellHeight,
          this.cellWidth,
          this.cellHeight,
          this.fontFamily,
        );
      }
    }
  }

  dispose(): void {
    this.canvas.remove();
  }
}

function drawCellFilled(
  ctx: CanvasRenderingContext2D,
  ch: string,
  x: number,
  y: number,
  cellW: number,
  cellH: number,
  fontFamily: string,
): void {
  ctx.font = `${REF_FONT_SIZE}px ${fontFamily}`;
  ctx.textBaseline = "top";
  ctx.textAlign = "left";
  const metrics = ctx.measureText(ch);
  const w = Math.max(metrics.width, 1);
  const h = REF_FONT_SIZE;

  ctx.save();
  ctx.translate(x, y);
  ctx.scale(cellW / w, cellH / h);
  ctx.fillText(ch, 0, 0);
  ctx.restore();
}

function rgbToCss(r: number, g: number, b: number): string {
  return `rgb(${r},${g},${b})`;
}
