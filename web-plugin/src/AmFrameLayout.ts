export const AM_VERSION_1 = 1;
export const AM_VERSION_2 = 2;

export function charIndexSize(version: number): number {
  return version >= AM_VERSION_2 ? 2 : 1;
}

export function cellStride(header: { version: number; flags: number }, colorFlag: number): number {
  const color = (header.flags & colorFlag) !== 0;
  return charIndexSize(header.version) + (color ? 3 : 0);
}

export function uncompressedFrameSize(
  header: { version: number; flags: number; cols: number; rows: number },
  colorFlag: number,
): number {
  return header.cols * header.rows * cellStride(header, colorFlag);
}

export function getCharIndex(
  frame: Uint8Array,
  cellIndex: number,
  header: { version: number; flags: number },
  colorFlag: number,
): number {
  const offset = cellIndex * cellStride(header, colorFlag);
  if (header.version >= AM_VERSION_2) {
    return frame[offset] | (frame[offset + 1] << 8);
  }
  return frame[offset];
}

export function getColor(
  frame: Uint8Array,
  cellIndex: number,
  header: { version: number; flags: number },
  colorFlag: number,
): [number, number, number] {
  const offset = cellIndex * cellStride(header, colorFlag) + charIndexSize(header.version);
  return [frame[offset], frame[offset + 1], frame[offset + 2]];
}
