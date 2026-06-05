# AsciiMovie

mp4 などの動画ファイルを、**カラー ASCII アート動画 + 同期音声** として再生できるように変換・再生するプロジェクトです。独自バイナリフォーマット `.amov`（magic: `AMOV`）を中核に使います。

## 構成

| コンポーネント | 説明 |
|---|---|
| [AsciiMovie.Core](src/AsciiMovie.Core/) | 共有 .NET ライブラリ — `.amov` の読み書き、DEFLATE 圧縮、ASCII マッピング |
| [AsciiMovie.Converter](src/AsciiMovie.Converter/) | CLI — ffmpeg 経由で動画 → `.amov` に変換 |
| [AsciiMovie.Player](src/AsciiMovie.Player/) | WPF デスクトッププレイヤー（Windows） |
| [web-plugin](web-plugin/) | TypeScript 製ブラウザ組み込みプレイヤー + デモ |
| [format/AM_FORMAT.md](format/AM_FORMAT.md) | バイナリフォーマット仕様 |

## 前提条件

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [ffmpeg](https://ffmpeg.org/) と **ffprobe** が `PATH` 上にあること（またはコンバーターに `--ffmpeg` でパスを指定）
- [Node.js](https://nodejs.org/) 18 以上（Web プラグインのみ）

> **注意:** ffmpeg は **同梱していません**。別途インストールし、コマンドラインから `ffmpeg` / `ffprobe` が実行できることを確認してください。

## ビルド

```bash
# .NET ソリューション（Core + Converter + WPF Player）
dotnet build AsciiMovie.sln

# Web プラグイン
cd web-plugin
npm install
npm run build
```

Core の往復テスト:

```bash
dotnet run --project src/AsciiMovie.Converter -- --verify-core
```

## コンバーター（動画 → `.amov`）

```bash
dotnet run --project src/AsciiMovie.Converter -- input.mp4 -o output.amov
```

### オプション（現行）

```
AsciiMovie.Converter <入力動画> -o <出力.amov> [オプション]
  -o, --output <path>     出力 .amov パス（必須）
      --cols <n>          横セル数（既定 160）
      --rows <n>          縦セル数（既定 90）
      --fps <n>           出力 fps（既定: 入力に合わせる、取得できなければ 24）
      --charset <str>     文字ランプ（暗→明、最大 65536 文字、既定: 高密度 256 段）
      --noinvert          セルごとの反転文字自動選択を無効化（既定: 有効）
  --edge              エッジ部分だけ ASCII 化（他は空白）
  --edge-strength <n> エッジ検出の強さ（既定 1.0）
      --mono              セルごとのカラーを無効化
      --no-audio          音声トラックを含めない
      --audio-codec <c>   mp3 | aac（既定 mp3）
      --ffmpeg <path>     ffmpeg 実行ファイルのパス（既定: ffmpeg）
```

### 使用例

```bash
# 小さめグリッド、モノクロ、音声なし
dotnet run --project src/AsciiMovie.Converter -- clip.mp4 -o clip-mono.amov --cols 120 --rows 68 --mono --no-audio

# AAC 音声
dotnet run --project src/AsciiMovie.Converter -- clip.mp4 -o clip.amov --audio-codec aac
```

進捗は stderr に表示されます（`frame X/Y`）。

> **パフォーマンス:** 既定の 160×90 = 1 フレームあたり 14,400 セルです。グリッドを大きくするとファイルサイズと描画 CPU 負荷が急増します。

> **反転文字（既定 ON）:** 各セルで通常の文字 index と反転 index（ランプ逆側）を比較し、黒背景への描画後に元の輝度に近い方を自動選択します。無効化する場合は `--noinvert` を指定してください。

> **フォーマット:** 新規 `.amov` は **version 2**（`charIndex: u16`、文字ランプ最大 65536 段）です。v1 ファイルも再生できます。詳細は [format/AM_FORMAT.md](format/AM_FORMAT.md)。

## WPF プレイヤー

```bash
dotnet run --project src/AsciiMovie.Player
```

### 対応入力

- `.amov`
- 動画: `mp4 / mkv / avi / mov / webm / wmv`
- 画像: `png / jpg / jpeg / bmp / gif / webp`（1フレーム動画として表示）

### 操作・設定

- **再生 / 一時停止 / 停止**、シークスライダー
- **音量スライダー / ミュート**
- 表示設定（右パネル）
  - 解像度 `cols / rows`
  - エッジモード、エッジ強度
  - カラー表示（生成にも反映）
  - 等幅フォントサイズ・フォント種別
- 一時停止中は ASCII テキストを矩形選択して `Ctrl+C` でコピー可能

### 表示ソースの仕様

- `mp4` など動画/画像を開いた場合: 入力ソースから都度 ASCII 再生成して表示
- `.amov` を開いた場合: `.amov` に保存されたフレームを表示
- 動画/画像を開いたときは、初期表示時に表示領域とフォント実寸を考慮して `cols / rows` を自動調整

## Web プラグイン

### デモ

```bash
cd web-plugin
npm run dev
```

表示された URL（既定ポート 5173）を開き、`.amov` ファイルを選択して再生します。

### ページへの組み込み

```html
<div id="movie" style="width:960px;height:540px;background:#000"></div>
<script type="module">
  import { AsciiMoviePlayer } from "./path/to/web-plugin/dist/index.js";

  const player = new AsciiMoviePlayer(document.getElementById("movie"), {
    fontFamily: "Consolas, monospace",
    autoplay: false,
    loop: false,
  });

  const response = await fetch("sample.amov");
  await player.load(await response.arrayBuffer());
  player.play();
</script>
```

### API

```ts
interface AsciiMovieOptions {
  fontSize?: number;    // px、既定 12
  fontFamily?: string;  // 既定 "monospace"
  autoplay?: boolean;   // 既定 false
  loop?: boolean;       // 既定 false
}

class AsciiMoviePlayer {
  constructor(container: HTMLElement, options?: AsciiMovieOptions);
  load(source: ArrayBuffer | string): Promise<void>;
  play(): void;
  pause(): void;
  stop(): void;
  seek(seconds: number): void;
  get duration(): number;
  get currentTime(): number;
  dispose(): void;
}
```

フレームは **raw DEFLATE** 圧縙です。 .NET の `DeflateStream` とブラウザの `DecompressionStream("deflate-raw")`（`pako` フォールバック付き）で互換性があります。

## 典型的なワークフロー

```bash
# 1. 変換
dotnet run --project src/AsciiMovie.Converter -- sample.mp4 -o sample.amov

# 2. Windows で再生
dotnet run --project src/AsciiMovie.Player
# → sample.amov を開く

# 3. ブラウザで再生
cd web-plugin && npm run dev
# → デモで sample.amov を読み込む
```

## ライセンス

リポジトリのライセンスに従います（該当する場合）。
