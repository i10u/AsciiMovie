import { AmParser } from "./AmParser.js";
import { AmRenderer } from "./AmRenderer.js";
import { AmFlags, AsciiMovieOptions, ParsedAmFile } from "./types.js";

export type { AsciiMovieOptions, AmHeader, ParsedAmFile } from "./types.js";
export { AmFlags, AmAudioCodec } from "./types.js";
export { AmParser } from "./AmParser.js";
export { AmRenderer } from "./AmRenderer.js";

export class AsciiMoviePlayer {
  private readonly container: HTMLElement;
  private readonly renderer: AmRenderer;
  private readonly options: Required<AsciiMovieOptions>;
  private parsed: ParsedAmFile | null = null;
  private audio: HTMLAudioElement | null = null;
  private audioUrl: string | null = null;
  private rafId = 0;
  private playing = false;
  private loop: boolean;
  private currentFrame = 0;
  private clockStart = 0;
  private clockOffset = 0;

  constructor(container: HTMLElement, options: AsciiMovieOptions = {}) {
    this.container = container;
    this.options = {
      fontSize: options.fontSize ?? 12,
      fontFamily: options.fontFamily ?? "monospace",
      autoplay: options.autoplay ?? false,
      loop: options.loop ?? false,
    };
    this.loop = this.options.loop;
    this.renderer = new AmRenderer(container, this.options.fontSize, this.options.fontFamily);
    this.renderer.resizeToContainer(container);
  }

  async load(source: ArrayBuffer | string): Promise<void> {
    this.stop();
    this.disposeAudio();

    const buffer =
      typeof source === "string"
        ? await (await fetch(source)).arrayBuffer()
        : source;

    this.parsed = AmParser.parse(buffer);
    this.currentFrame = 0;

    if ((this.parsed.header.flags & AmFlags.Audio) !== 0 && this.parsed.header.audio.length > 0) {
      const mime = this.parsed.header.audioCodec === 2 ? "audio/aac" : "audio/mpeg";
      const blob = new Blob([new Uint8Array(this.parsed.header.audio)], { type: mime });
      this.audioUrl = URL.createObjectURL(blob);
      this.audio = new Audio(this.audioUrl);
      this.audio.preload = "auto";
    }

    await this.drawFrame(0);

    if (this.options.autoplay) {
      this.play();
    }
  }

  play(): void {
    if (!this.parsed) return;
    this.playing = true;
    if (this.audio) {
      void this.audio.play();
    } else {
      this.clockStart = performance.now();
    }
    this.scheduleTick();
  }

  pause(): void {
    this.playing = false;
    if (this.rafId) {
      cancelAnimationFrame(this.rafId);
      this.rafId = 0;
    }
    if (this.audio) {
      this.audio.pause();
    } else {
      this.clockOffset = this.currentTime;
    }
  }

  stop(): void {
    this.pause();
    this.currentFrame = 0;
    this.clockOffset = 0;
    if (this.audio) {
      this.audio.currentTime = 0;
    }
    void this.drawFrame(0);
  }

  seek(seconds: number): void {
    if (!this.parsed) return;
    const clamped = Math.max(0, Math.min(seconds, this.duration));
    if (this.audio) {
      this.audio.currentTime = clamped;
    } else {
      this.clockOffset = clamped;
      this.clockStart = performance.now();
    }
    void this.syncFrameFromTime(clamped);
  }

  get duration(): number {
    if (!this.parsed) return 0;
    return this.parsed.header.frameCount / Math.max(this.parsed.header.fps, 0.001);
  }

  get currentTime(): number {
    if (!this.parsed) return 0;
    if (this.audio) return this.audio.currentTime;
    if (!this.playing) return this.clockOffset;
    return this.clockOffset + (performance.now() - this.clockStart) / 1000;
  }

  dispose(): void {
    this.stop();
    this.disposeAudio();
    this.renderer.dispose();
    this.parsed = null;
  }

  private scheduleTick(): void {
    if (!this.playing) return;
    this.rafId = requestAnimationFrame(() => {
      void this.tick();
    });
  }

  private async tick(): Promise<void> {
    if (!this.parsed || !this.playing) return;

    const time = this.currentTime;
    await this.syncFrameFromTime(time);

    if (time >= this.duration) {
      if (this.loop) {
        this.seek(0);
        if (this.audio) void this.audio.play();
        else this.clockStart = performance.now();
      } else {
        this.pause();
        return;
      }
    }

    this.scheduleTick();
  }

  private async syncFrameFromTime(seconds: number): Promise<void> {
    if (!this.parsed) return;
    const frame = Math.round(seconds * this.parsed.header.fps);
    const clamped = Math.max(0, Math.min(frame, this.parsed.header.frameCount - 1));
    if (clamped === this.currentFrame) return;
    this.currentFrame = clamped;
    await this.drawFrame(clamped);
  }

  private async drawFrame(index: number): Promise<void> {
    if (!this.parsed) return;
    const frameData = await AmParser.readFrame(this.parsed, index);
    this.renderer.render(this.parsed.header, frameData);
  }

  private disposeAudio(): void {
    if (this.audio) {
      this.audio.pause();
      this.audio.src = "";
      this.audio = null;
    }
    if (this.audioUrl) {
      URL.revokeObjectURL(this.audioUrl);
      this.audioUrl = null;
    }
  }
}
