import { AsciiMoviePlayer } from "../src/index.ts";

const container = document.getElementById("player-container")!;
const fileInput = document.getElementById("file-input") as HTMLInputElement;
const playBtn = document.getElementById("play-btn") as HTMLButtonElement;
const pauseBtn = document.getElementById("pause-btn") as HTMLButtonElement;
const stopBtn = document.getElementById("stop-btn") as HTMLButtonElement;
const seek = document.getElementById("seek") as HTMLInputElement;
const timeLabel = document.getElementById("time")!;

const player = new AsciiMoviePlayer(container, { fontFamily: "Consolas, monospace" });
let seekDragging = false;

function setControlsEnabled(enabled: boolean): void {
  playBtn.disabled = !enabled;
  pauseBtn.disabled = !enabled;
  stopBtn.disabled = !enabled;
  seek.disabled = !enabled;
}

function formatTime(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

function updateTime(): void {
  timeLabel.textContent = `${formatTime(player.currentTime)} / ${formatTime(player.duration)}`;
  if (!seekDragging) {
    seek.value = String(Math.round((player.currentTime / Math.max(player.duration, 0.001)) * 1000));
  }
}

fileInput.addEventListener("change", async () => {
  const file = fileInput.files?.[0];
  if (!file) return;
  const buffer = await file.arrayBuffer();
  await player.load(buffer);
  setControlsEnabled(true);
  updateTime();
});

playBtn.addEventListener("click", () => player.play());
pauseBtn.addEventListener("click", () => player.pause());
stopBtn.addEventListener("click", () => {
  player.stop();
  updateTime();
});

seek.addEventListener("mousedown", () => { seekDragging = true; });
seek.addEventListener("mouseup", () => {
  seekDragging = false;
  const ratio = Number(seek.value) / 1000;
  player.seek(ratio * player.duration);
  updateTime();
});
seek.addEventListener("input", () => {
  if (seekDragging) {
    const ratio = Number(seek.value) / 1000;
    player.seek(ratio * player.duration);
    updateTime();
  }
});

setInterval(updateTime, 200);

window.addEventListener("resize", () => {
  // player handles internal canvas; reload current frame on resize would need API extension
});
