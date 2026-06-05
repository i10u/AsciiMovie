export enum AmFlags {
  Color = 1 << 0,
  Audio = 1 << 1,
  FramesDeflated = 1 << 2,
  DeltaEncoded = 1 << 3,
}

export enum AmAudioCodec {
  None = 0,
  Mp3 = 1,
  Aac = 2,
}

export interface AmHeader {
  version: number;
  flags: number;
  cols: number;
  rows: number;
  fps: number;
  frameCount: number;
  charset: string;
  audioCodec: AmAudioCodec;
  audio: Uint8Array;
}

export interface FrameTableEntry {
  offset: number;
  size: number;
}

export interface ParsedAmFile {
  header: AmHeader;
  frameTable: FrameTableEntry[];
  framesBaseOffset: number;
  buffer: ArrayBuffer;
}

export interface AsciiMovieOptions {
  fontSize?: number;
  fontFamily?: string;
  autoplay?: boolean;
  loop?: boolean;
}
