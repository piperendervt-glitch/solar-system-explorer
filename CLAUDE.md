# CLAUDE.md — solar-system-explorer

このリポジトリで作業する Claude / 開発者向けの運用ルール。
**編集や実行の前に必ず一読すること。**

> 運用の流儀は `offline-ai-asset-demo` から移植した。`tools/run_unity.ps1` は
> あちらのコピー＋最小改変（文言のみ。ロジックは触っていない）。

---

## 0. 現在の作業（Demo 2）

**Step 0〜7（最小デモ）は完了・タグ済み（`step-0`〜`step-7` / `v0.1-minimal-demo`）。**

現在は [docs/02-demo2-plan.md](docs/02-demo2-plan.md) の **Demo 2（見た目デモ）** を進める。
**Step 番号は `8-0` から。** 計画上の区切りは次のとおり。

| Step | 内容 |
| --- | --- |
| 8-0 | 共通の小物（F1 トグル・微振動） |
| 8 | 惑星の表現（テクスチャ → シェーダ → 雲 → 自転 → 整合） |
| 9 | 太陽の表現（HDR ディスク → コロナ → フレア → 露出再調整） |
| 10 | 音（素材 → Mixer → エンジン → イベント → スナップショット） |

Demo 2 の「やらない」ことは計画書 §7 にある。**勝手に広げない。**

### 素材の置き場所

| | |
| --- | --- |
| 作業用コピー | `source_assets/textures/` と `source_assets/audio/`（**`.gitignore` 済み**） |
| 原本 | `D:\Users\pipe_render\Downloads\solar-system-explorer\`（**消さない。ここが原本**） |

実測 `%USERPROFILE%` は `C:\Users\pipe_render` だが、**原本は D: ドライブにある。**
`%USERPROFILE%\Downloads\...` では届かないので、上表の絶対パスを使うこと。

`source_assets/` はリポジトリに入らない。clone しただけの環境には無いので、
`unity/Assets/` へ取り込むときだけ参照する。

| ファイル | 追跡 | 内容 |
| --- | --- | --- |
| [docs/asset-sources.md](docs/asset-sources.md) | する | 出典・ライセンス一覧（**雛形。「要確認」が残っている**） |
| [docs/audio-candidates.md](docs/audio-candidates.md) | する | §10-1 の 6 用途ごとの候補（**未決定。人間が試聴して選ぶ**） |
| `source_assets/AUDIO_INVENTORY.txt` | しない | 音声 360 本の長さ / Hz / ch / ピーク。ffprobe から再生成できる |

**出典の記録は `source_assets/` に置かない。** あそこは丸ごと gitignore されるので、
リポジトリに残らない。ライセンス関係は必ず `docs/` 側に置くこと。

Kenney の 4 パックは `source_assets/audio/kenney/<パック名>/` に展開済みで、
すべて **CC0**（クレジット任意）。freesound の 2 ファイルは **ライセンス未確認**。
計画書 §10-1 は「CC0 のみ採用」なので、**確認するまで採用しない。**

---

## 0-B. batchmode で検証できないホップ

**この表は Demo 2 に限らない。** Step が進むたびに追記する。
「自動で確かめられないもの」を明示しておかないと、確かめたつもりで進んでしまう。

| 分類 | ホップ | 代替手段 | 状態 |
| --- | --- | --- | --- |
| 入力 | **物理キーの押下** | `ShipRig.InputOverride` でフラグを直接送る | 既知。Step 3a で確立。batchmode PlayMode は Input System のイベントを配送できない |
| 入力 | 微振動・操作感の体感 | 振幅の上下限を数値検証 | 「酔わないか」は人手 |
| 描画 | 「写真に見えるか」の判断 | — | 完全に人手 |
| 描画 | シェーダのバリアント欠落による見た目の差 | 画素検証（縁の色成分・夜側輝度など） | コンパイルエラーはログに出るが、キーワード漏れは出ない |
| 描画 | 透明オブジェクトの描画順の不定性 | 複数カメラ角度のスクショ差分 | 角度を振れば自動化できる |
| 描画 | **SRP Lens Flare が `Camera.Render()` → RenderTexture 経路で描かれるか** | 要実測 | **未確認。** 現行のスクショは `Deep.Render()` で RT に描いている。乗らない場合は exe 経由（Step 7 で確立）へ切り替える |
| 描画 | 「眩しい」「暗すぎる」の判断 | 輝度分布の測定 | 閾値の妥当性は人手 |
| 音 | **音が鳴らない** | `volume` / `pitch` / `Play()` 回数を記録する | batchmode では原理的に不可 |
| 音 | AudioMixer のスナップショット遷移・ローパスの効き | 露出パラメータの値を検証 | 数値は読めるが音は聴けない |
| 描画 | **OnGUI（デバッグ HUD・シナリオの確認項目テキスト・F4 デバッグパネル）** | exe 経由で撮る（Step 7 の `StandaloneCapture`） | `Camera.Render` → RenderTexture の経路には**写らない**。実測済み・PlayMode テストで回帰を見ている。**F4 パネル（§0-C）も同じ**ので、パネルで決めた値の確認は必ず exe で行う |
| 音 | ループの継ぎ目のクリック | **波形解析で数値化できる** | 隣接サンプル差の平均に対する連結点の段差比。**人手不要にできるので EditMode テストへ落とす** |
| 全般 | exe 起動でのスクショ | Step 7 の `StandaloneCapture` | 確立済み。ただし音の自動判定は exe でも不可 |

**GUI が要るホップは現時点で無い。** Demo 2 の惑星シェーダは Shader Graph をやめて
手書き `.shader` にしたので（[02-demo2-plan.md](docs/02-demo2-plan.md) §8-2）、
CLI で完結する。**新たに GUI を要する手順を持ち込まないこと。**

---

## 0-C. 実機で見るための道具（F1 / F4）

**exe で人が目で判断するための道具。** batchmode では効かない（OnGUI なので
RenderTexture に写らない → §0-B）。

| キー | 役割 |
| --- | --- |
| `F1` | デバッグ HUD の表示切替（**情報表示**）。既定は非表示。`-debugHud` を付けると初期表示 |
| `F2` / `F3` | シナリオを次 / 前へ |
| `F4` | **デバッグパネル**（操作盤）の開閉 |

### F4 デバッグパネル（Step 8-0b）

「目で決めるしかない値」を実機で決めるための操作盤。**実装依頼 → 再ビルド →
起動 → 目視 の往復を無くすのが目的。** 絵で判断すべき値を数値で判断しないこと。

| キー | 動作 |
| --- | --- |
| `↑` `↓` | 項目を移動 |
| `←` `→` | 値を増減（トグルは ON/OFF） |
| `Space` | トグル項目の ON/OFF |
| `R` | **全項目**を既定へ戻す |
| `F4` | 閉じる（設定は保持される） |

**開いている間は船の操作を止める。** `Space`（前進）と `R`（ダイヤル増）を
パネルが使うため。パネルにその旨を 1 行出している。

**シナリオを切り替えるとトグルと選択だけ既定へ戻る。数値は保持する。**
`earth-close-day` で決めた値を `terminator` や `night` でも確かめるため。

触れる対象:

| 種別 | 内容 |
| --- | --- |
| 段の表示 | カメラ 4 段の個別 ON/OFF ＋「1 段だけ表示」（排他。個別トグルより優先） |
| 天体 | 3 天体 × 3 表現（光点 / プロキシ殻 / 実スケール）の個別 ON/OFF |
| その他 | 雲層 / ステーション / スカイボックス / ポストプロセス / レンズフレア |
| 数値 | `_AtmosphereStrength` 0〜10（0.25 刻み）/ `_CloudOpacity` 0〜2（0.05）/ フレア基準強度 0〜2（0.05）/ 微振動の振幅 0〜5e-3（2.5e-4） |

天体ごとに「距離 / 角直径の**計算値** / 画面上の**実測 bbox** / 引き渡し率 / 有効な表現」
を 1 行で出す。**計算値と実測が並んでいるのが肝で、食い違いに目で気付くための表示。**
隅がカメラの後ろにあって投影が破綻するときは `---` を出す（嘘の数字を並べない）。

### 値の決め方の運用

1. exe を起動して `F4` で開き、絵を見ながら値を決める
2. `F4` で閉じると、**既定から変わった項目だけ**がログに出る
3. その出力を Claude に渡す。**Claude がコードの定数を書き換える**

```
[DebugPanel] 既定から変更された項目 2 件:
  _AtmosphereStrength: 5.00 -> 6.75
  カメラ段 Nearfield: ON -> off
```

**アセットには書き戻さない。** 触るのは実行時のマテリアルとコンポーネントだけ。
既定値は `Core/PlanetAppearance.cs` などコードの定数から読む（パネル側に値を
二重定義しない）。

**`F4` を押さなければ既存動作と完全に同一。** 閉じている間は何も適用しない。

---

## 1. 環境（実測値）

| 項目 | 値 |
| --- | --- |
| OS / Shell | Windows 11 / PowerShell 5.1 |
| Unity Editor | `C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe` |
| Unity プロジェクト | リポジトリ直下 `unity/`（Universal 3D テンプレート） |
| `activeInputHandler` | `1`（Input System のみ） |

### パッケージ実バージョン（2026-08-26 / `unity/Packages/packages-lock.json` 実測）

`manifest.json` は「何を要求したか」、`packages-lock.json` は「実際に解決された何が入ったか」。
**バージョンを知りたいときは必ず lock 側を見ること。**
`depth 0` = `manifest.json` に直接書いてあるもの。`1` 以上は依存で入ったもの。

| パッケージ | バージョン | depth | source |
| --- | --- | --- | --- |
| `com.unity.render-pipelines.universal` | 17.3.0 | 0 | builtin |
| `com.unity.render-pipelines.core` | 17.3.0 | 1 | builtin |
| `com.unity.render-pipelines.universal-config` | 17.0.3 | 1 | builtin |
| `com.unity.shadergraph` | 17.3.0 | 1 | builtin |
| `com.unity.test-framework` | 1.6.0 | 0 | builtin |
| `com.unity.ext.nunit` | 2.0.5 | 1 | builtin |
| `com.unity.test-framework.performance` | 3.2.0 | 3 | registry |
| `com.unity.inputsystem` | 1.19.0 | 0 | registry |
| `com.unity.ugui` | 2.0.0 | 0 | builtin |
| `com.unity.ai.navigation` | 2.0.11 | 0 | registry |
| `com.unity.timeline` | 1.8.11 | 0 | registry |
| `com.unity.visualscripting` | 1.9.10 | 0 | registry |
| `com.unity.collab-proxy` | 2.11.4 | 0 | registry |
| `com.unity.ide.rider` | 3.0.39 | 0 | registry |
| `com.unity.ide.visualstudio` | 2.0.26 | 0 | registry |
| `com.unity.burst` | 1.8.28 | 2 | registry |
| `com.unity.collections` | 2.6.2 | 2 | registry |
| `com.unity.mathematics` | 1.3.3 | 2 | registry |
| `com.unity.searcher` | 4.9.4 | 2 | registry |
| `com.unity.nuget.mono-cecil` | 1.11.6 | 3 | registry |

合計: 非 modules 20 / `com.unity.modules.*` 35。

Unity Editor のパスは環境変数 `UNITY_EDITOR_PATH` で上書きできる。
設定されていなければ上表の既定値を使う。

`unity/` は Unity Hub を使わず、Editor 同梱のテンプレート
`Editor/Data/Resources/PackageManager/ProjectTemplates/com.unity.template.3d-cross-platform-17.0.14.tgz`
（中身は `com.unity.template.urp-blank` / displayName `3D URP` = Hub の「Universal 3D」）の
`package/ProjectData~` を展開して作った。Hub の「新規作成」と同じ手順。

**`activeInputHandler` を変える必要が出たら、Unity を起動していない状態で
`unity/ProjectSettings/ProjectSettings.asset` をテキスト編集する。**
Editor は「入力ハンドラを切り替えて再起動するか」のダイアログを出すが、
batchmode では表示できず固まる。値: `0`=旧 Input Manager / `1`=Input System / `2`=Both。

### テンプレート展開後に Editor が加えた変更と、その掃除（2026-08-26 / 完了）

テンプレート同梱の `manifest.json` は 10 パッケージ（URP 17.0.1 / test-framework 1.4.2 /
Unity 2023.3 向け）だったが、**初回 batchmode インポートで Editor が自動的に
解決し直し、バージョンを上げたうえで不要なものを追加した。**
**バージョンの繰り上がりは維持し、不要パッケージだけを削除済み。**

削除したもの（`manifest.json` から `depth 0` の 8 件。他は依存で自動的に消えた）:

| 削除した ID | 理由 / 副作用 |
| --- | --- |
| `com.unity.purchasing` 4.14.2 | IAP。`Assets/MobileDependencyResolver/`（Google EDM4U の dll/pdb 一式）と `Assets/Resources/BillingMode.json` を生成していた |
| `com.unity.ads` 4.16.4 | 広告。非公開の個人デモに不要 |
| `com.unity.analytics` 3.8.2 | 解析。`com.unity.services.analytics` を連れてくる |
| `com.unity.modules.unityanalytics` | 旧 `UnityEngine.Analytics` モジュール。lock 上どこからも参照されていなかった |
| `com.unity.multiplayer.center` 1.0.1 | マルチプレイヤー。単機デモに不要 |
| `com.unity.2d.sprite` / `com.unity.2d.tilemap` | 2D 向け |
| `com.unity.xr.legacyinputhelpers` 2.1.13 | 旧 XR 入力。Input System 1 本でいく |

依存で連鎖的に消えたもの: `com.unity.services.analytics` 6.2.2 /
`com.unity.services.core` 1.16.0。

`Assets/MobileDependencyResolver/` と `Assets/Resources/BillingMode.json` は
`.meta` ごと削除し、空になった `Assets/Resources/` も削除した。

**再発したら同じ手順で消すこと。** 手順は「`manifest.json` から ID を消す →
`run_unity.ps1` で Editor に解決し直させる → `packages-lock.json` に
残っていないこと（他パッケージの依存として復活していないこと）を確認する」。
`Assets/` を直接消すだけではパッケージが再生成するので効かない。

---

## 2. リポジトリ構成

`[ ]` は未作成（担当 Step を併記）。

```
solar-system-explorer/
├─ CLAUDE.md                  このファイル（実行ルール・コマンド）
├─ .gitignore                 Unity / 生成物の除外設定
├─ docs/
│  └─ 00-requirements.md      要件・Step 計画（正。勝手に書き換えない）
├─ tools/
│  ├─ run_unity.ps1           batchmode ラッパー
│  └─ run_tests.ps1           EditMode テストランナー
├─ unity/                     Unity プロジェクト本体（Universal 3D）
│  ├─ Assets/
│  │  ├─ Scripts/Core/        SolarSystem.Core.asmdef（UnityEngine 非依存）
│  │  ├─ Scripts/Unity/       SolarSystem.Unity.asmdef（MonoBehaviour 側）
│  │  ├─ Editor/              SolarSystem.Editor.asmdef（Editor 専用）
│  │  ├─ Tests/EditMode/      SolarSystem.Tests.EditMode.asmdef（現在は空）
│  │  ├─ [ ] Editor/SolarSetup.cs   エントリポイント        （Step 1）
│  │  ├─ Scenes/SampleScene.unity   テンプレート付属
│  │  ├─ Settings/                  テンプレート付属（URP RPAsset 等）
│  │  ├─ TutorialInfo/              テンプレート付属
│  │  └─ InputSystem_Actions.inputactions  テンプレート付属
│  ├─ Packages/manifest.json
│  └─ ProjectSettings/
├─ verify/
│  └─ shots/                  自動スクショ出力（git 管理外・.gitkeep のみ追跡）
└─ logs/                      batchmode ログ / テスト結果 XML
                              （git 管理外・.gitkeep のみ追跡）
```

---

## 3. Unity をバッチ実行する — `tools/run_unity.ps1`

```powershell
# -executeMethod を付けずに起動（起動確認のみ）
.\tools\run_unity.ps1

# エントリポイントを実行（Step 1 以降）
.\tools\run_unity.ps1 -Method SolarSetup.Run

# Unity へ追加引数を素通しする
.\tools\run_unity.ps1 -Method SolarSetup.Run -ExtraArgs '-outDir','verify/shots'
```

### 引数

| 引数 | 型 | 説明 |
| --- | --- | --- |
| `-Method` | string（省略可） | `-executeMethod` に渡す静的メソッド名。**省略すると `-executeMethod` を付けず `-batchmode -quit` のみで起動する。** 対象メソッドが未実装のうちに指定すると必ず異常終了するので、実装前は必ず省略する。 |
| `-ExtraArgs` | string[]（省略可） | Unity へそのまま渡す追加引数。 |
| `-TimeoutMinutes` | double | Unity プロセスの待ち時間の上限（分）。既定 `15`。超えたらプロセスツリーごと強制終了し **終了コード 124** を返す。 |

**`-Method` に渡す文字列に名前空間は付けない。** クラス名とメソッド名だけを渡す。

### スクリプトの挙動

- `-projectPath` は**スクリプト自身の位置から解決した絶対パス**（リポジトリ直下 `unity/`）。カレントディレクトリに依存しない。
- `-logFile` は `logs/unity_yyyyMMdd_HHmmss.log`。実行ごとに新しいファイルを作る。
- 実行後にログを走査し、`error CS` / `Failed to` / `Aborting batchmode` を含む行を全て標準出力へ出し、パターン別と合計の件数サマリを表示する。既知の良性ノイズは除外して「既知ノイズ: N 件（無視）」として別に数える（→ §4「既知の良性ログノイズ」）。
- `-TimeoutMinutes` を超えたら `taskkill /T /F` でプロセスツリーごと落とし、ログの最終 20 行を出して終了する。

### 終了コード

| 値 | 意味 |
| --- | --- |
| `0` | Unity が正常終了 |
| `1` | Unity Editor / `unity/` が見つからない等、起動前のエラー |
| `124` | `-TimeoutMinutes` を超えたので強制終了した |
| その他 | Unity プロセスの ExitCode をそのまま返す |

**`124` は「正常終了」とも「テスト失敗」とも区別できる値として選んである。**
CI やスクリプトから判定するときはこの 3 種を区別すること。

### スタンドアロンビルド（Step 7）

```powershell
# Windows 向けにビルドする。出力は build/（.gitignore 済み）
.\tools\run_unity.ps1 -Method SolarSetup.Build -TimeoutMinutes 30
```

実測 178 MB / 73.7 秒。既定の 15 分でも足りるが、初回は余裕を見て伸ばす。

ビルドした exe は引数無しで普通に起動する。検証用に 1 枚だけ撮って終了させる口がある:

```powershell
build\SolarSystemExplorer.exe -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 `
  -captureShot verify\shots\7_03_standalone.png -captureFrames 180 -logFile logs\standalone.log
```

`-captureShot` が無ければ何もしない。`StandaloneCapture` は
`ScreenCapture.CaptureScreenshotAsTexture` を使う（`CaptureScreenshot` は
書き出しが非同期で、直後に Quit すると空ファイルが残る）。

### セーブファイル（Step 7）

`%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\solar-system-explorer.save.json`。
中身は最後にドッキングしたステーション名 1 個だけ。
**テストからは `SaveFile.OverridePath` で一時ファイルへ逃がすこと。**
本物を汚すと他の PlayMode テストの開始地点が変わる。

---

## 4. EditMode テストを回す — `tools/run_tests.ps1`

```powershell
# 全 EditMode テスト
.\tools\run_tests.ps1

# 絞り込み
.\tools\run_tests.ps1 -Filter 'SolarSystem.Core.Tests'
```

| 引数 | 型 | 説明 |
| --- | --- | --- |
| `-TestPlatform` | string | `-testPlatform` の値。既定 `EditMode`。 |
| `-Filter` | string（省略可） | `-testFilter` に渡すテスト完全名フィルタ。 |
| `-ExtraArgs` | string[]（省略可） | Unity へそのまま渡す追加引数。 |
| `-TimeoutMinutes` | double | Unity プロセスの待ち時間の上限（分）。既定 `15`。超えたらプロセスツリーごと強制終了し **終了コード 124** を返す。 |

### スクリプトの挙動

- 起動引数は `-batchmode -runTests -testPlatform <platform>`。
- 出力は `logs/unity_tests_<timestamp>.log` と `logs/testresults_<timestamp>.xml`（NUnit3 形式）。
- **結果 XML は実行ごとにタイムスタンプ付きの新しいファイル名にする。**
  固定パスにすると、Unity が起動に失敗して XML を書けなかったときに
  **前回の成功 XML を読んで「OK」と誤判定する。**
- XML の `total / passed / failed / skipped / inconclusive` を表示し、
  `//test-case[@result='Failed']` の `fullname` と `failure/message` を標準出力へ出す。
- あわせて `run_unity.ps1` と同じ `error CS` / `Failed to` / `Aborting batchmode` のログ走査も行う。既知の良性ノイズは除外する（→「既知の良性ログノイズ」）。
- `-TimeoutMinutes` を超えたら `taskkill /T /F` でプロセスツリーごと落とし、ログの最終 20 行を出して `124` で終了する。

### 終了コードの決め方（重要 / 実測に基づく）

**判定は結果 XML を正とする。Unity の ExitCode は原則見ない。**

| 値 | 意味 |
| --- | --- |
| `0` | 失敗テスト 0 件（**テスト 0 件の実行を含む**） |
| `1` | テスト失敗 / コンパイルエラー / **XML が無い・壊れている** / 起動前のエラー |
| `124` | `-TimeoutMinutes` を超えたので強制終了した |

判定の順序:

1. **XML が無い・パースできない・`test-run` 要素が無い → FAIL（`1`）。**
   「不明」で通さない。Unity がテスト実行前に落ちている可能性が高い。
   このとき Unity の ExitCode は**信用しない**（XML が無い以上、意味を持たない）。
2. `failed > 0` → FAIL（`1`）
3. `error CS > 0` → FAIL（`1`）
4. `total = 0` → **ここだけ** Unity の ExitCode も見る。**`0` のみ OK**、
   それ以外（`1`、`3` = run failed 等）は FAIL。
5. それ以外 → OK（`0`）

`total=0` は Step 0 では正常。テストアセンブリが空なのでこれが期待値。

#### Unity ExitCode についての訂正（2026-08-26）

以前このファイルには「テスト 0 件のとき Unity は ExitCode=1 を返す」と書いていたが、
**これは Unity の挙動ではなく PowerShell 側の読み取りアーティファクトだった。**

| `$proc.Handle` | 読めた ExitCode | 回数 |
| --- | --- | --- |
| 取得していない | `1` | 2/2 |
| `$null = $proc.Handle` で取得 | `0` | 3/3 |

同じ実行の XML はどちらも `result="Passed" total="0" failed="0"`。

**2026-08-26 追記: 原因が `.Handle` 未取得と特定できたため、手順 4 の
「`1` も許容」は撤去済み。現在は `total=0` のとき ExitCode=0 のみを成功とする。**

### 触ってはいけない実装上の前提

- **`-runTests` と `-quit` を併用しない。** テスト完了前に Editor が終了する。
  `-runTests` は完了後に自分で終了する。
- **`-nographics` は絶対に付けない**（§5 参照）。
- **Unity.exe は GUI サブシステムのアプリなので `&` 演算子で呼ぶと待たずに返り、
  `$LASTEXITCODE` が取れない。** `Start-Process -NoNewWindow -PassThru` の
  `.ExitCode` を使う。
- **`Start-Process` に `-Wait` を付けない**（`run_unity.ps1` / `run_tests.ps1` の両方）。
  `-Wait` は Unity が起動した**子プロセス**（LicensingClient デーモン等）の終了まで
  待つため、Unity 本体が終わっても戻ってこない。2026-08-26 の実測:

  | 待ち方 | Unity 本体の所要 | スクリプトが戻るまで |
  | --- | --- | --- |
  | `-Wait` | 約 66 秒（ログの時刻差） | **668.8 秒** |
  | `.WaitForExit(ms)` | 同等 | 13.7〜15.0 秒（Library 温まり後） |

  直接の子プロセスだけを待つため `-Wait` を外して `$proc.WaitForExit($ms)` を呼ぶ。
- **`Start-Process` の直後に `$null = $proc.Handle` を実行する。これは必須。**
  PowerShell 5.1 ではハンドルを一度キャッシュしておかないと、プロセス終了後に
  `.ExitCode` が正しく読めない（`$null` になる／別の値が返る）。
  実測では Handle 未取得のとき `1`、取得後は `0` で安定した（→ §4 の訂正表）。
- **待ちは `WaitForExit()` の無引数版を使わない。** ミリ秒指定のオーバーロード
  `WaitForExit($ms)` を使い、戻り値 `$false`（＝タイムアウト）を必ず処理する。
  無引数版は永久に待つので、Unity が固まると人間が Ctrl+C するまで戻らない。
- **戻らないときは待たずにタイムアウトに任せる。**
  「もう少し待てば終わるかも」と手で待ち続けない。既定 15 分で必ず
  `taskkill /T /F` が走り、ログの最終 20 行と一緒に `124` で返る。
  15 分で足りない処理を回すときだけ `-TimeoutMinutes` を明示的に上げる。
- **`Process.Kill()` だけではプロセスツリーを落とせない。**
  PowerShell 5.1 が乗る .NET Framework 4.x の `Kill()` に
  `entireProcessTree` オーバーロードは無く、自分自身しか殺せない。
  両スクリプトの `Stop-ProcessTree` は `taskkill /PID <id> /T /F` を主として使い、
  取りこぼしたときだけ `Kill()` にフォールバックする。
- `unity/` が存在しない場合はエラーで即終了する。Unity は存在しないパスを渡されると
  空プロジェクトを勝手に作ってしまうため。

---

### 既知の良性ログノイズ（`Failed to` 最大 2 件）

本リポジトリでは 2026-08-26 の実測で、正常終了時にも `Failed to` として
次の 2 行が拾われる（`run_unity.ps1` / `run_tests.ps1` の両方）。
2 行目は Hub の LicensingClient の状態次第で出たり出なかったりする。

```
[Licensing::Module] Error: Access token is unavailable; failed to update
[Licensing::Module] Error: Failed to handshake to channel: "LicenseClient-pipe_render"
```

2 行目の前後はこうなっている。プロトコル 1.18.0 が弾かれ（`ResponseCode: 505`）、
Editor が自前の `LicenseClient-pipe_render-6000.3.11` を起動し直して成功している。

```
[Licensing::Client] Error: HandshakeResponse reported an error:
	ResponseCode: 505
	ResponseStatus: Unsupported protocol version '1.18.0'.
[Licensing::Module] Error: Failed to handshake to channel: "LicenseClient-pipe_render"
[Licensing::Module] Successfully launched the LicensingClient (PId: 32364)
[Licensing::Module] Successfully connected to LicensingClient on channel: "LicenseClient-pipe_render-6000.3.11"
```

直後に以下が続き、ライセンス自体は解決している。

```
[Licensing::Client] Successfully resolved entitlement details
[Licensing::Module] License group:
  Product: Unity Personal
  Type: Assigned
  Expiration: Unlimited
```

これらの行は**スクリプト側で自動的に除外**され、
`Failed to` の件数には入らず「既知ノイズ: N 件（無視）」として別に表示される。

```
[既知ノイズ/Failed to] line 81: [Licensing::Module] Error: Access token is unavailable; failed to update
[run_unity]   Failed to            : 0 件   (既知ノイズ: 1 件（無視）)
[run_unity]   既知ノイズ                : 1 件（無視）
```

#### 除外リストの場所

両スクリプトの先頭付近にある **`$KnownBenignLogNoise` 配列**（1 箇所）:

| ファイル | 位置 |
| --- | --- |
| [tools/run_unity.ps1](tools/run_unity.ps1) | `Format-Arg` の直後、`# ---- 既知の良性ログノイズ ----` ブロック |
| [tools/run_tests.ps1](tools/run_tests.ps1) | `Get-RunCount` の直後、同名のブロック |

- **除外を足すときは配列に 1 行足すだけ。判定ロジック（`Test-BenignNoise`）は触らない。**
- **2 つのスクリプトは配列を共有していない。足すときは両方に足すこと。**
- 判定は `String.Contains` による序数の部分文字列一致。
  `-like` / `-match` は `[Licensing::Module]` の `[ ]` を
  ワイルドカード／文字クラスとして解釈してしまうので使わない。
- 足す前に必ずログの前後を読み、本当に良性か確認する。**推測で足さない。**

**リストに無い `Failed to` が出たら必ず中身を読むこと。
`error CS` は 1 件でも無視しない。**

（移植元の `offline-ai-asset-demo` でも同じ 2 行が観測されていた。）

---

## 5. 作業ルール（厳守）

### Unity 実行

- **GUI の Unity Editor を開いたまま batchmode を起動しない。**
  プロジェクトロックが競合して必ず失敗する。batchmode を回す前に Editor を閉じる。
  `run_unity.ps1` / `run_tests.ps1` を叩く前に、必ず Editor が閉じていることを確認する。
- **`-nographics` は付けない。** スクリーンショットによる目視検証に必要。
  「batchmode だから `-nographics` を付けておく」は禁止。
- **1 回の起動に数十秒〜数分かかる。複数のタスクは 1 回の Run にまとめる。**
  ただし**コンパイルやドメインリロードを跨ぐ処理は Run を分ける**
  （パッケージ追加とシーン生成を同一 Run に入れない）。
- **戻らないときは待たずにタイムアウトに任せる。** 両スクリプトは既定 15 分で
  プロセスツリーごと落として `124` を返す（→ §3 / §4）。手で待ち続けたり、
  別ターミナルから様子を見に行ったりしない。`124` が返ったらログの最終行を読む。
- **ログの `error CS`（コンパイルエラー）は必ず修正してから次へ進む。**

### アセンブリ構成

3 枚の asmdef ＋ テスト用 1 枚。依存の向きは **Core ← Unity ← Editor** の一方向。

| asmdef | 場所 | 参照 | 制約 |
| --- | --- | --- | --- |
| `SolarSystem.Core` | `Assets/Scripts/Core/` | なし | **`noEngineReferences: true`** |
| `SolarSystem.Unity` | `Assets/Scripts/Unity/` | Core | ランタイム。`UnityEditor` 禁止 |
| `SolarSystem.Editor` | `Assets/Editor/` | Core, Unity | `includePlatforms: ["Editor"]` |
| `SolarSystem.Tests.EditMode` | `Assets/Tests/EditMode/` | Core, Unity | Editor 限定・現在は空 |

- **Core に `UnityEngine` 依存を持ち込まない。**
  `SolarSystem.Core.asmdef` は `"noEngineReferences": true` を立ててある。
  これは「規約」ではなく**コンパイラが強制する**設定で、Core 内で
  `using UnityEngine;` を書いた時点で `error CS` になる。
  **この設定を外して回避しない。** 軌道計算・時刻・単位変換は Unity 非依存に保つ。
  Unity 型（`Vector3` / `MonoBehaviour` / `Time` / `Debug`）が要るなら、
  それは Core ではなく `SolarSystem.Unity` の仕事。
- **`Assets/Editor` 以外に `UnityEditor` 名前空間を参照するコードを書かない。**
  ビルド時にコンパイルが通らなくなる。

### 座標・単位

- **1 unit = 1 km。** Unity 側のスケールはこれで固定する。
- **絶対座標は `double`。`Vector3` に絶対座標を入れない。**
  `Vector3` は `float`（32bit, 仮数 24bit）なので、太陽系スケールでは精度が足りない。
  実測した最小刻み（ULP, 1 unit = 1 km）:

  | 位置 | 距離 | `float` の刻み | `double` の刻み |
  | --- | --- | --- | --- |
  | 地球半径 | 6,371 km | 0.49 m | 9.1e-13 km |
  | 太陽半径 | 696,000 km | 62.5 m | 1.2e-10 km |
  | 地球軌道 (1 AU) | 1.496e8 km | **16 km** | 3.0e-8 km |
  | 海王星軌道 (30.07 AU) | 4.498e9 km | **512 km** | 9.5e-7 km |

  地球軌道の位置を `float` で持つと刻みが 16 km。地球の直径の 0.13% が
  1 ステップになるので、公転どころか自転すら表現できない。海王星では 512 km。

  したがって:
  - **絶対座標（太陽中心慣性系での位置・速度）は `double` の自前型で保持する。**
    Core 側（UnityEngine 非依存）に置く。
  - **`Vector3` に入れてよいのは、カメラ／フローティングオリジン基準の
    相対座標だけ。** 原点から数千 km 以内に収まっていることを確認してから
    `(float)` にキャストする。
  - **`double` → `Vector3` のキャストは 1 箇所に集約する。** あちこちで
    キャストすると、どこで精度が落ちたか追えなくなる。

- **物理的な意味と数学的な操作を分けて書く。**
  「地球を公転させる」（物理）と「時刻 t の平均近点角から位置ベクトルを出す」（数学）
  は別の記述にする。

- **`Rigidbody` は使わない。** PhysX は内部 `float` で、上表のとおり太陽系スケールに
  精度が足りない。加えて固定タイムステップの数値積分は軌道の長期安定性を保証しない。
  天体の位置は**毎フレーム軌道要素から直接求める**（積分しない）。
  `Rigidbody` / `Collider` ベースの重力シミュレーションに置き換えない。

### ドキュメント

- **`docs/00-requirements.md` は凍結。変更は追記のみ。**
  既存の行を書き換えたり削除したりしない。要件が変わったときは
  ファイル末尾に日付付きの追記節を足す。
- **設計上の逸脱は `docs/01-architecture.md` 側に理由付きで書く。**
  要件と設計が食い違ったとき、要件書を設計に合わせて直しにいかない。
  「要件はこう書いてあるが、こういう理由でこう設計した」を
  `01-architecture.md` に残し、要件書は原文のまま置いておく。
- **`docs/01-architecture.md` はこれ以上拡張しない。**
  決定（D-1〜D-25）が変わったときに該当行を直すだけ。章や節を足さない。
- **Step ごとの計画は 1 ページ以内。** 長い計画書を書かない。
- **要決定を新設しない。** 判断は実装しながら**最小の選択**で埋める。
  迷ったら「動く最小のもの」を選び、理由を 1 行コメントで残す。

### Step 進行

- **各 Step の完了条件を満たしてからコミットする。**
  完了条件を満たしていない状態でコミットしない。
- **完了条件を満たしても、勝手に次の Step へ進まない。** 次に進む判断は人間が行う。
- 正常動作を確認したらすぐコミットする。大きな変更の前にも必ずコミットを取る。
- **その Step の範囲外のコードを先に書かない。**（例: Step 0 でゲームロジックを書かない）

---

## 6. 座標・数値計算の検証

- 実装前に具体的な入出力値を 3 件以上計算して示す。
- 実装後に実際の値を出力し、期待値と照合する。
- 「物理的な意味」と「数学的な操作」を分けて記述する。
- 分からないときは推測で理由を作らず「確認が必要」と明示する。
