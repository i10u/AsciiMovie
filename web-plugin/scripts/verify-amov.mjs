import { readFileSync } from "node:fs";
import { AmParser } from "../dist/index.js";

const path = process.argv[2] ?? "sample.amov";
const bytes = readFileSync(path);
const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);

const parsed = AmParser.parse(buffer);
console.log("header", {
  cols: parsed.header.cols,
  rows: parsed.header.rows,
  fps: parsed.header.fps,
  frameCount: parsed.header.frameCount,
});

for (let i = 0; i < parsed.header.frameCount; i++) {
  const frame = await AmParser.readFrame(parsed, i);
  console.log(`frame ${i}: ${frame.length} bytes`);
}

console.log("Web parser OK");
