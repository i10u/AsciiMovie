# AsciiMovie 実装指示書（Cursor auto モード向け）

このファイルは、AI コーディングエージェント（Cursor auto モード）が **このリポジトリをゼロから実装する** ための自己完結した指示書である。
他の資料を参照しなくても、このファイルだけで実装を進められるように記述している。

---

## 0. プロジェクト概要

mp4 等の動画ファイルを「色付き ASCII アート動画 + 同期音声」として再生するアプリ群を作る。
中核は独自バイナリフォーマット `.amov`（ascii movie）。

構成する成果物:
1. **Converter** (C#/.NET CLI) … 動画 → `.amov` に変換
2. **WPF Player** (C#/.NET, Windows) … `.amov` を再生
3. **Web プラグイン** (TypeScript) … ブラウザで `.amov` を再生する組み込みライブラリ + デモ
4. **共有 .NET ライブラリ** `AsciiMovie.Core` … `.amov` の読み書き・圧縮・マッピング

### 採用方針（確定事項・変更不可）
- スタック: C#/.NET（Converter + WPF Player）、Web プラグインは TypeScript
- 機能: **カラー ASCII** / **音声同期** / **`.amov` は DEFLATE 圧縮**
- 動画/音声デコードは **`ffmpeg.exe` をサブプロセス起動** して行う（重いネイティブバインディングは使わない）
- ターゲット: **.NET 8**（WPF は `net8.0-windows`）
- 文字エンコード/バイナリは **リトルエンディアン**

---

## 1. リポジトリ構成（この通りに作る）

```
AsciiMovie/
  AsciiMovie.sln
  README.md
  .gitignore
  format/
    AM_FORMAT.md                 # .amov 仕様書（実装の真実）
  src/
    AsciiMovie.Core/             # 共有ライブラリ (net8.0)
      AsciiMovie.Core.csproj
      AmHeader.cs
      AmFlags.cs
      AmReader.cs
      AmWriter.cs
      AsciiMapper.cs
      Compression.cs
    AsciiMovie.Converter/        # CLI (net8.0)
      AsciiMovie.Converter.csproj
      Program.cs
      ConverterOptions.cs
      FfmpegRunner.cs
    AsciiMovie.Player/           # WPF (net8.0-windows)
      AsciiMovie.Player.csproj
      App.xaml / App.xaml.cs
      MainWindow.xaml / MainWindow.xaml.cs
      AsciiRenderer.cs
      PlaybackController.cs
  web-plugin/
    package.json
    tsconfig.json
    src/
      index.ts                   # public API (AsciiMoviePlayer)
      AmParser.ts
      AmRenderer.ts
      types.ts
    demo/
      index.html
      demo.ts
```

---

## 2. `.amov` バイナリフォーマット仕様（全実装で一致させること）

すべて **リトルエンディアン**。`format/AM_FORMAT.md` にこの仕様を清書して保存すること。

### 2.1 ヘッダ
| フィールド | 型 | 説明 |
|---|---|---|
| magic | char[4] | `"AMOV"` 固定 |
| version | u16 | `1` |
| flags | u16 | ビットフラグ（下記） |
| cols | u16 | 横セル数 |
| rows | u16 | 縦セル数 |
| fps | f32 | 再生フレームレート |
| frameCount | u32 | 総フレーム数 |
| charsetLen | u16 | charset のバイト長 |
| charset | u8[charsetLen] | UTF-8。**暗→明** の順の文字ランプ（index 順） |
| audioCodec | u8 | 0=none, 1=mp3, 2=aac |
| audioLen | u32 | 音声バイト長（0 可） |
| audio | u8[audioLen] | 音声データ（コンテナ生バイト。例: MP3 フレーム列） |

### 2.2 フレームテーブル（シーク用）
ヘッダ直後に `frameCount` 個の要素を並べる。
| フィールド | 型 | 説明 |
|---|---|---|
| offset | u32 | frames 領域先頭からの相対オフセット |
| size | u32 | そのフレームブロックの圧縮後バイト長 |

### 2.3 フレーム領域
各フレームは独立した **DEFLATE 圧縮ブロック**（`framesDeflated` フラグが立っている場合）。
展開後の中身（1 フレーム分）は次のセル列を **行優先（row-major, 左上→右下）** で並べたもの:

- color 無効: `charIndex: u8` × (cols×rows)
- color 有効: `(charIndex: u8, r: u8, g: u8, b: u8)` × (cols×rows)（1 セル 4 バイト）

`charIndex` は charset 内のインデックス（0 = 最も暗い文字）。

### 2.4 flags ビット定義
| bit | 名前 | 意味 |
|---|---|---|
| 0 | color | カラー（RGB）を含む |
| 1 | audio | 音声を含む |
| 2 | framesDeflated | 各フレームブロックが DEFLATE 圧縮済み |
| 3 | deltaEncoded | 差分エンコード（**v1 では未使用=0。将来拡張用。実装は false 固定でよい**） |

> 注: DEFLATE は .NET の `System.IO.Compression.DeflateStream`、JS は `DecompressionStream("deflate-raw")` か `pako.inflateRaw` を使う。**raw deflate（zlib ヘッダ無し）** で統一すること。`DeflateStream` は raw deflate を生成するので JS 側は `deflate-raw` / `inflateRaw` を使う。

---

## 3. 共通アルゴリズム（マッピング）

`AsciiMapper`（.NET）と Web 側で **同一ロジック** にする。

1. 既定の文字ランプ（暗→明）: `" .:-=+*#%@"`（10 段階）。`--charset` で変更可。
2. 各セルの輝度は元フレームを cols×rows に縮小（平均/エリアサンプリング）した RGB から計算:
   `luma = 0.299*R + 0.587*G + 0.114*B`（0–255）
3. `charIndex = clamp(round(luma / 255 * (charsetLen-1)), 0, charsetLen-1)`
4. color 有効時、そのセルの代表 RGB（縮小後ピクセルの色）をそのまま格納。

---

## 4. Converter（C#/.NET CLI）詳細

### 4.1 CLI 仕様
```
AsciiMovie.Converter <input video> -o <output.amov> [options]
  -o, --output <path>     出力 .amov パス（必須）
      --cols <n>          横セル数 (既定 160)
      --rows <n>          縦セル数 (既定 90)
      --fps <n>           出力 fps (既定: 入力に合わせる/未指定なら 24)
      --charset <str>     文字ランプ（暗→明, 既定 " .:-=+*#%@")
      --mono              カラーを無効化（モノクロ）
      --no-audio          音声を含めない
      --audio-codec <c>   mp3|aac (既定 mp3)
      --ffmpeg <path>     ffmpeg.exe のパス（既定: PATH 上の "ffmpeg")
```

### 4.2 処理フロー
1. `ffprobe`/`ffmpeg` で入力の解像度・fps を取得（無ければ既定）。
2. 映像デコード: `ffmpeg -i input -vf scale=cols:rows,fps=fps -f rawvideo -pix_fmt rgb24 -` を起動し、**stdout から rawvideo を読む**。1 フレーム = cols*rows*3 バイト。
3. 各フレームを `AsciiMapper` でセル列（charIndex(+RGB)）に変換。
4. 音声抽出（`--no-audio` でなければ）: `ffmpeg -i input -vn -acodec libmp3lame -f mp3 -`（aac の場合は適切な引数）で stdout からバイト取得し audio ブロックに格納。
5. `AmWriter` で `.amov` を出力（各フレームを raw deflate 圧縮、フレームテーブル構築）。
6. 進捗を stderr に表示（`frame X/Y`）。

### 4.3 FfmpegRunner
- `System.Diagnostics.Process` で ffmpeg を起動、`RedirectStandardOutput=true`、`StandardOutput.BaseStream` をバイナリ読み。
- stderr は別スレッドで読み捨て（デッドロック防止）。
- ffmpeg 不在時は分かりやすいエラー（README の前提を案内）。

---

## 5. WPF Player 詳細

### 5.1 UI（MainWindow）
- メニュー/ボタン: 「開く(.amov)」「再生/一時停止」「停止」
- シークバー（Slider, フレーム index にバインド）
- 中央に描画領域（`Image` に `WriteableBitmap` を表示、または `Canvas`）
- 経過時間/総時間表示

### 5.2 AsciiRenderer
- `AmReader` で読んだフレーム（charIndex(+RGB)）を画面に描画。
- 推奨実装: 等幅フォントのグリフを `WriteableBitmap` にバッチ描画、または `DrawingVisual` + `FormattedText`/`GlyphRun`。
- カラー時は各セルの文字色に RGB を適用。背景は黒。
- セルのピクセルサイズはフォントサイズから算出し、ウィンドウサイズに合わせてスケール。

### 5.3 PlaybackController（音声同期）
- 音声がある場合: `MediaElement`（または `MediaPlayer`）に audio バイトを一時ファイル化して再生。`MediaElement.Position` を時刻基準にして表示フレーム = `round(position.TotalSeconds * fps)` を選ぶ。
- 音声が無い場合: `Stopwatch` を時刻基準に同フレーム選択。
- `DispatcherTimer`（または CompositionTarget.Rendering）で毎フレーム更新し、必要フレームだけ再描画。
- シーク時は `MediaElement.Position` を更新し、対応フレームを描画。

---

## 6. Web プラグイン詳細（TypeScript）

### 6.1 公開 API（`src/index.ts`）
```ts
export interface AsciiMovieOptions {
  fontSize?: number;        // px, 既定 12
  fontFamily?: string;      // 既定 "monospace"
  autoplay?: boolean;       // 既定 false
  loop?: boolean;           // 既定 false
}

export class AsciiMoviePlayer {
  constructor(container: HTMLElement, options?: AsciiMovieOptions);
  load(source: ArrayBuffer | string): Promise<void>; // string は URL
  play(): void;
  pause(): void;
  stop(): void;
  seek(seconds: number): void;
  get duration(): number;
  get currentTime(): number;
  dispose(): void;
}
```

### 6.2 実装要件
- `AmParser`: `ArrayBuffer` を `DataView` で読み、ヘッダ・フレームテーブル・音声・各フレーム（遅延展開可）をパース。raw deflate 展開は `DecompressionStream("deflate-raw")`（フォールバックに `pako.inflateRaw`）。
- `AmRenderer`: `<canvas>` を container に生成。`fillText` を **同一色のランごとにバッチ** して描画。背景黒。
- 音声: audio バイトを `Blob` → `URL.createObjectURL` → `HTMLAudioElement`。`audio.currentTime` を時刻基準に表示フレームを `requestAnimationFrame` で同期。音声無しは内部クロックで進行。
- `demo/index.html` + `demo/demo.ts`: ファイル選択 input で `.amov` を読み込み再生するデモ。

### 6.3 ビルド
- `package.json`: TypeScript と（任意で）`pako`、バンドラは `esbuild` か `vite` を採用（軽量な esbuild 推奨）。
- npm scripts: `build`（ライブラリ）、`dev`（デモのローカルサーバ）。

---

## 7. 実装順序（この順で進める）

各ステップ完了ごとにビルドが通ることを確認する。

1. **scaffold**: `AsciiMovie.sln` と各プロジェクト雛形、`.gitignore`、`README.md`、`format/AM_FORMAT.md`（§2 を清書）を作成。
2. **AsciiMovie.Core**: `AmFlags`, `AmHeader`, `Compression`(raw deflate), `AsciiMapper`, `AmWriter`, `AmReader` を実装。**往復テスト**（小さな擬似フレームを write→read して一致確認）を行う簡易テスト or サンプルを用意。
3. **AsciiMovie.Converter**: `FfmpegRunner`, `ConverterOptions`, `Program`。実際の mp4 が無くても、ffmpeg 起動引数の組み立てが正しいことを確認できる構造にする。
4. **AsciiMovie.Player**: WPF。`.amov` を開いて再生・シーク・音声同期。
5. **web-plugin**: パーサ・レンダラ・音声同期・デモ。Core が書いた `.amov` をそのまま再生できることを最終確認。
6. **README**: ビルド/実行手順、ffmpeg 前提、各コンポーネントの使い方、Web プラグイン組込み例。

---

## 8. 受け入れ基準（完了条件）

- [ ] `dotnet build AsciiMovie.sln` が成功する
- [ ] `AsciiMovie.Converter sample.mp4 -o sample.amov` が `.amov` を生成（ffmpeg 必要）
- [ ] WPF Player で `sample.amov` がカラー + 音声同期で再生・シークできる
- [ ] Web デモで同じ `sample.amov` が再生できる（Core 出力と Web パーサの互換性が取れている）
- [ ] `--mono` / `--no-audio` の各オプションが機能する
- [ ] README に ffmpeg 前提と全手順が記載されている

---

## 9. 重要な実装上の注意

- **エンディアンとフォーマットの一致**: .NET と JS で `.amov` の解釈を完全一致させる。特に raw deflate（zlib ヘッダ無し）で揃える。
- **デッドロック回避**: ffmpeg の stdout/stderr は別々に読む。
- **パフォーマンス**: 既定 160x90 でも 1 フレーム 14,400 セル。色付き `fillText`/グリフ描画は **同色バッチ** で。大きすぎる cols/rows はサイズ・CPU 負荷が急増するので README に注意を明記。
- **コメント方針**: コードの「何をしているか」を逐語的に説明するコメントは書かない。非自明な意図・制約のみコメントする。
- **ffmpeg 同梱はしない**: PATH か `--ffmpeg` 指定。README で案内。
