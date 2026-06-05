# AsciiMovie（`.amov`）バイナリフォーマット仕様

マルチバイト整数および浮動小数点数はすべて **リトルエンディアン** です。

## バージョン

| version | 説明 |
|---|---|
| 1 | 初期版。`charIndex: u8` |
| 2 | **現行版**。`charIndex: u16`（文字ランプ最大 65536 文字） |

新規作成ファイルは **version 2** を使用すること。プレイヤーは v1 / v2 両方を読めること。

## ヘッダ

| フィールド | 型 | 説明 |
|---|---|---|
| magic | char[4] | 固定値 `"AMOV"` |
| version | u16 | `1` または `2` |
| flags | u16 | ビットフラグ（下記参照） |
| cols | u16 | 横セル数 |
| rows | u16 | 縦セル数 |
| fps | f32 | 再生フレームレート |
| frameCount | u32 | 総フレーム数 |
| charsetLen | u16 | charset のバイト長（UTF-8） |
| charset | u8[charsetLen] | UTF-8。**暗→明** の順の文字ランプ（index 順）。最大 65536 文字（v2） |
| audioCodec | u8 | 0=none, 1=mp3, 2=aac |
| audioLen | u32 | 音声バイト長（0 可） |
| audio | u8[audioLen] | 音声データ（コンテナ生バイト。例: MP3 フレーム列） |

## フレームテーブル（シーク用）

ヘッダ直後に `frameCount` 個の要素を並べます。

| フィールド | 型 | 説明 |
|---|---|---|
| offset | u32 | frames 領域先頭からの相対オフセット |
| size | u32 | そのフレームブロックの圧縮後バイト長 |

## フレーム領域

`framesDeflated` フラグが立っている場合、各フレームは独立した **raw DEFLATE** ブロックです。
1 フレーム分の展開後データは、**行優先（row-major、左上→右下）** のセル列です。

### version 1

- **モノクロ**: `charIndex: u8` × (cols × rows)
- **カラー**: `(charIndex: u8, r: u8, g: u8, b: u8)` × (cols × rows) — 1 セル 4 バイト

### version 2（推奨）

- **モノクロ**: `charIndex: u16` × (cols × rows) — 1 セル 2 バイト
- **カラー**: `(charIndex: u16, r: u8, g: u8, b: u8)` × (cols × rows) — 1 セル 5 バイト

`charIndex` は charset 内のインデックス（0 = 最も暗い文字）。v2 では **0〜65535** まで表現可能です。

## フラグ

| bit | 名前 | 意味 |
|---|---|---|
| 0 | color | セルごとの RGB を含む |
| 1 | audio | 音声ブロックを含む |
| 2 | framesDeflated | 各フレームブロックが DEFLATE 圧縮済み |
| 3 | deltaEncoded | v1 では未使用（常に 0） |

## 圧縮に関する注意

- **raw DEFLATE**（zlib ラッパーなし）を使用すること。
- .NET: `System.IO.Compression.DeflateStream`
- JavaScript: `DecompressionStream("deflate-raw")` または `pako.inflateRaw`

## 忠実度について

動画の再現度は主に次で決まります。

1. **グリッド解像度**（cols × rows）
2. **文字ランプの段数**（charset の文字数。v2 では最大 65536）
3. **カラー**（セルごとの RGB を保持するか）

Unicode 全文字を 1 セル 1 文字で直接指定する方式（コードポイント格納）は将来拡張の余地として残す。v2 は「大規模文字ランプ + 輝度インデックス」方式です。
