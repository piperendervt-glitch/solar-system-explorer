# solar-system-explorer

Unity 6 / URP で作っている、太陽系をゆっくり眺めるための**個人用の最小デモ**です。
地球ステーションから火星ステーションまで飛んで、ドッキングして、戻ってくる。それだけ。
ゲーム性はありません。公開・配布・販売を目的にしたものではなく、
requirements に沿って Step を刻みながら作った学習・実験用のリポジトリです。

1 unit = 1 km。天体の絶対座標は `double` で持ち、毎フレーム原点を船に据え直す
(フローティングオリジン) ことで、海王星軌道スケールでも `float` の精度落ちを避けています。

---

## 遊び方

**ビルド済みの実行ファイルは配布していません** (`build/` は `.gitignore` 済み)。
Unity Editor で開いて Play してください。

### 必要なもの

| | |
| --- | --- |
| Unity Editor | **6000.3.11f1** (URP 17.3.0) |
| OS | Windows 11 で開発・検証。他は未確認 |

### 手順

1. このリポジトリを clone する。

2. **テクスチャの原本を `source_assets/` に置く。**
   `source_assets/` は `.gitignore` 済みなので clone しても入ってきません。
   入手元・ファイル名・解像度は [docs/02-assets.md](docs/02-assets.md) にあります。
   必要なのは次の 4 つです。

   ```
   source_assets/starmap_2020_4k.exr
   source_assets/8k_earth_daymap.jpg
   source_assets/8k_mars.jpg
   source_assets/8k_sun.jpg
   ```

   > `unity/Assets/Textures/` に取り込み済みの複製は追跡してあるので、
   > **Editor で開くだけなら 2 は省けます。** 取り込み直したいときや、
   > 原本を差し替えたいときにだけ必要です。

3. `unity/` を Unity Editor で開く。

4. シーンを生成する。**GUI の Editor を閉じてから**回してください
   (batchmode とプロジェクトロックが競合します)。

   ```powershell
   .\tools\run_unity.ps1 -Method SolarSetup.ImportTmp        # TextMeshPro の Essential Resources
   .\tools\run_unity.ps1 -Method SolarSetup.ImportTextures   # source_assets/ -> Assets/Textures/
   .\tools\run_unity.ps1 -Method SolarSetup.Run              # Assets/Scenes/Main.unity を生成
   ```

   ドメインリロードを跨ぐので **3 回に分けて実行します。**

5. Editor で `Assets/Scenes/Main.unity` を開いて Play。

### テストとビルド

```powershell
.\tools\run_tests.ps1                                    # EditMode  108 件
.\tools\run_tests.ps1 -TestPlatform PlayMode             # PlayMode   31 件
.\tools\run_unity.ps1 -Method SolarSetup.Build -TimeoutMinutes 30   # build/ へ Windows ビルド
```

---

## 操作

視点はコックピット固定です。姿勢を変えるとコックピットごと回ります。

| キー | 動作 |
| --- | --- |
| マウス移動 | ピッチ / ヨー |
| `W` `A` `S` `D` | ピッチ / ヨー (キーボード) |
| `Q` `E` | ロール |
| `Space` | 前進 (スラスト) |
| `R` `F` | 速度ダイヤルを上げる / 下げる |
| `Tab` | 目標ステーションを切り替える |
| `T` | オートパイロット起動 |
| `G` | オートパイロット解除 |
| `Enter` | ドッキング要求 |
| `BackSpace` | 出港 |
| `1`〜`8` | デバッグジャンプ (火星までの距離を指定して飛ぶ) |

### 速度ダイヤル (`R` / `F`)

9 段。低速側は km/s 表記、高速側は c 表記です。手動操作の上限は 1 km/s。

```
STOP  →  10 m/s  →  100 m/s  →  1 km/s  →  0.001c  →  0.01c  →  0.1c  →  0.5c  →  0.9c
```

### デバッグジャンプ (`1`〜`8`)

火星までの距離を指定して瞬間移動します。光点からメッシュへの切り替わりを目で追うためのものです。

| キー | 火星までの距離 |
| --- | --- |
| `1` | 1.0e7 units |
| `2` | 1.0e6 units |
| `3` | 1.0e5 units |
| `4` | 5.0e4 units |
| `5` | 2.0e4 units |
| `6` | 1.0e4 units |
| `7` | 5.0e3 units |
| `8` | 4.0e3 units |

### 計器

画面下端に 2 行。速度 (SPD) / 目標までの距離 (DST) / 到着予定 (ETA) /
目標名 (TGT) / ポート正面からのずれ角 (ALN)。
ずれ角が許容 30 度以内に入ると `ALIGNED` が付きます。
`Enter` を押して要求が通らなかったときは、距離・速度・角度のどれが足りないかが 1 行で出ます。

### セーブ

ドッキングしたときに、そのステーション名だけを JSON で保存します。
次に起動するとそこから始まります。ファイルが無い / 壊れている / 知らない名前のときは
地球ステーションから始まります。

```
%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\solar-system-explorer.save.json
```

---

## クレジット

このリポジトリには**テクスチャ本体を含んでいます** (`unity/Assets/Textures/`)。
再配布にあたるため、以下の表記はライセンス上の必須事項です。

### 惑星・太陽のテクスチャ

**Solar System Scope** — <https://www.solarsystemscope.com/textures/>
**Licensed under CC BY 4.0** — <https://creativecommons.org/licenses/by/4.0/>

使用ファイル:

- `8k_earth_daymap.jpg` (8192 x 4096) — 地球のアルベド
- `8k_mars.jpg` (8192 x 4096) — 火星のアルベド
- `8k_sun.jpg` (4096 x 2048) — 太陽の表面

地球の雲・夜景・法線マップは使っていません。

### 星空 (スカイボックス)

**NASA/Goddard Space Flight Center Scientific Visualization Studio**
*Deep Star Maps 2020* — <https://svs.gsfc.nasa.gov/4851>

SVS のページが指定するクレジット文言:

> NASA/Goddard Space Flight Center Scientific Visualization Studio.
> Gaia DR2: ESA/Gaia/DPAC.
> Constellation figures based on those developed for the IAU by Alan MacRobert of
> Sky and Telescope magazine (Roger Sinnott and Rick Fienberg).

- Animator / Visualizer: **Ernie Wright** (USRA)
- Technical Support: Laurence Schuler (ADNET Systems, Inc.), Ian Jones (ADNET Systems, Inc.)

使用ファイル: `starmap_2020_4k.exr` (celestial, 4096 x 2048。Unity 側で Cubemap 化して 2048 px/面)

元データは **Hipparcos-2 / Tycho-2 / Gaia Data Release 2** に加え、
Yale Bright Star Catalog / UCAC3 / XHIP に由来します。

> 本デモが使っているのは星のみの *celestial* 版で、星座線入りの版ではありません。
> 上のクレジット文言は SVS ページの指定をそのまま引いたものです。

NASA の画像は一般にパブリックドメイン扱いですが、
Gaia (ESA) など第三者のデータを含みます。詳細は [docs/02-assets.md](docs/02-assets.md) を参照してください。

---

## 実装状況

**Step 0 〜 Step 7 まで完了**しています。各 Step の完了時点に git タグを打ってあります
(`step-0` 〜 `step-7`、および `v0.1-minimal-demo`)。

| Step | 内容 |
| --- | --- |
| 0 | スキャフォールド (batchmode ラッパー、asmdef 4 枚、CLAUDE.md) |
| 1 | フローティングオリジン |
| 2 | 太陽系プレースホルダー (プロキシ殻、角直径による切替) |
| 3a | 手動操作 (速度ダイヤル、姿勢、デバッグジャンプ) |
| 3b | オートパイロットと実スケール引き渡し |
| 4 | コックピットと計器 (RenderTexture + TextMeshPro) |
| 5 | ステーションとドッキング |
| 6 | 見た目の仕上げ (星空、惑星テクスチャ、ポストプロセス、エンジン音) |
| 7 | セーブ、計器レイアウトの改修、スタンドアロンビルド |

船の乗り換え・相対論効果の描画・惑星への着陸・PCVR 対応・戦闘などは
**スコープ外**です。理由は [docs/00-requirements.md](docs/00-requirements.md) §4 を参照してください。

### ドキュメント

| ファイル | 内容 |
| --- | --- |
| [docs/00-requirements.md](docs/00-requirements.md) | 要件 (凍結。変更は追記のみ) |
| [docs/01-architecture.md](docs/01-architecture.md) | 設計と決定 D-1 〜 D-25 |
| [docs/02-assets.md](docs/02-assets.md) | 外部アセットの出所とライセンス |
| [CLAUDE.md](CLAUDE.md) | 開発時の運用ルール・コマンド |
