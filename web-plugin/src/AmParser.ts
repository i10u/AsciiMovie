import { AmAudioCodec, AmFlags, FrameTableEntry, ParsedAmFile, AmHeader } from "./types.js";
import {
  AM_VERSION_1,
  AM_VERSION_2,
  uncompressedFrameSize,
} from "./AmFrameLayout.js";

const MAGIC = "AMOV";

async function inflateRaw(data: Uint8Array): Promise<Uint8Array> {
  if (typeof DecompressionStream !== "undefined") {
    const inputStream = new Blob([new Uint8Array(data)]).stream();
    const outputStream = inputStream.pipeThrough(new DecompressionStream("deflate-raw"));
    const buffer = await new Response(outputStream).arrayBuffer();
    return new Uint8Array(buffer);
  }

  try {
    const pako = await import("pako");
    return pako.inflateRaw(data);
  } catch {
    throw new Error("Raw DEFLATE decompression is not available in this browser.");
  }
}

export class AmParser {
  static parse(buffer: ArrayBuffer): ParsedAmFile {
    const view = new DataView(buffer);
    let offset = 0;

    const magic = readAscii(view, offset, 4);
    offset += 4;
    if (magic !== MAGIC) {
      throw new Error(`Invalid magic: expected ${MAGIC}, got ${magic}`);
    }

    const version = view.getUint16(offset, true);
    offset += 2;
    if (version !== AM_VERSION_1 && version !== AM_VERSION_2) {
      throw new Error(`Unsupported version: ${version}`);
    }

    const flags = view.getUint16(offset, true);
    offset += 2;
    const cols = view.getUint16(offset, true);
    offset += 2;
    const rows = view.getUint16(offset, true);
    offset += 2;
    const fps = view.getFloat32(offset, true);
    offset += 4;
    const frameCount = view.getUint32(offset, true);
    offset += 4;
    const charsetLen = view.getUint16(offset, true);
    offset += 2;
    const charset = readUtf8(view, buffer, offset, charsetLen);
    offset += charsetLen;
    const audioCodec = view.getUint8(offset) as AmAudioCodec;
    offset += 1;
    const audioLen = view.getUint32(offset, true);
    offset += 4;
    const audio = new Uint8Array(buffer, offset, audioLen);
    offset += audioLen;

    const frameTable: FrameTableEntry[] = [];
    for (let i = 0; i < frameCount; i++) {
      frameTable.push({
        offset: view.getUint32(offset, true),
        size: view.getUint32(offset + 4, true),
      });
      offset += 8;
    }

    const header: AmHeader = {
      version,
      flags,
      cols,
      rows,
      fps,
      frameCount,
      charset,
      audioCodec,
      audio: audio.slice(),
    };

    return {
      header,
      frameTable,
      framesBaseOffset: offset,
      buffer,
    };
  }

  static uncompressedFrameSize(header: AmHeader): number {
    return uncompressedFrameSize(header, AmFlags.Color);
  }

  static async readFrame(parsed: ParsedAmFile, index: number): Promise<Uint8Array> {
    if (index < 0 || index >= parsed.frameTable.length) {
      throw new RangeError(`Frame index out of range: ${index}`);
    }

    const entry = parsed.frameTable[index];
    const start = parsed.framesBaseOffset + entry.offset;
    const compressed = new Uint8Array(parsed.buffer, start, entry.size);
    const deflated = (parsed.header.flags & AmFlags.FramesDeflated) !== 0;

    const data = deflated ? await inflateRaw(compressed) : compressed.slice();
    const expected = AmParser.uncompressedFrameSize(parsed.header);
    if (data.length !== expected) {
      throw new Error(`Frame ${index} size mismatch: got ${data.length}, expected ${expected}`);
    }
    return data;
  }
}

function readAscii(view: DataView, offset: number, length: number): string {
  let result = "";
  for (let i = 0; i < length; i++) {
    result += String.fromCharCode(view.getUint8(offset + i));
  }
  return result;
}

function readUtf8(view: DataView, buffer: ArrayBuffer, offset: number, length: number): string {
  const bytes = new Uint8Array(buffer, offset, length);
  return new TextDecoder().decode(bytes);
}
