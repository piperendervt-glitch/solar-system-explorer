# CLAUDE.md — solar-system-explorer

このリポジトリで作業する Claude / 開発者向けの運用ルール。
**編集や実行の前に必ず一読すること。**

> 運用の流儀は `offline-ai-asset-demo` から移植した。`tools/run_unity.ps1` は
> あちらのコピー＋最小改変（文言のみ。ロジックは触っていない）。

---

## 0. 現在の作業（Demo 3 完了 / 次は Demo 4）

**完了・タグ済み:**

| | Step | タグ |
| --- | --- | --- |
| 最小デモ | 0〜7 | `step-0`〜`step-7` / `v0.1-minimal-demo` |
| Demo 2（見た目） | 8〜10 | `step8-0`〜`step10-4` / `demo2` |
| Demo 3（コックピット） | 11-0〜11-5 | `step11-0`〜`step11-5` / `demo3` |

**Demo 3 は完了。** [docs/03-demo3-plan.md](docs/03-demo3-plan.md) §0-A に完了状態と決定値がある。
確定した値は下の §0-A にもまとめてある。**次は Demo 4（ステーションの3Dモデル）。**

Demo 3 の区切りは次のとおりだった。

| Step | 内容 | 状態 |
| --- | --- | --- |
| 11-0 | 調達とライセンスの整理（追跡除外・出典記録・フォールバック骨格） | `step11-0` |
| 11-1 | 取り込みと URP 変換・マテリアルの棚卸し | `step11-1` |
| 11-2 | 配置・スケール・視点（`CockpitDefinition`） | `step11-2` |
| 11-3 | 計器の画面への移設（RT 分割・帯の撤去） | `step11-3` |
| 11-4 | 照明（太陽光の差し込み・補助光） | `step11-4` |
| 11-5 | 有料コックピットの判断ゲート | `step11-5`。**無料のまま続ける**と決定（買わない） |
| 11-6 | 有料コックピットへの差し替え | **実施しない**（11-5 の決定による） |

Demo 3 の「やらない」ことは計画書 §11 にある。**勝手に広げない。**

### Demo 3 で特に効く前提

- **リポジトリは PUBLIC。Asset Store の EULA はアセットの再配布を禁じる。**
  取り込み先 `unity/Assets/ThirdParty/` は追跡除外にし、**追跡ファイルに 1 件も
  入っていないこと**を EditMode テストで縛る。取り込みは追跡除外のコミットの後に行う
- **アセットが無いクローンでも Editor・テスト・ビルドが通ること。** 箱コックピットへ
  フォールバックする。判定はフォルダの有無ではなく**プレハブ GUID の存在**
- **ただしシーンは組み直しが要る（Step 11-2a 以降）。** `Assets/Scenes/Main.unity` は
  追跡ファイルで、コックピットは**プレハブのリンク（GUID）**として載っている。アセットを
  持たないマシンではその参照が解決できず、コックピットが空になる。
  **`.	oolsun_unity.ps1 -Method SolarSetup.Run` でシーンを組み直すと箱に落ちる。**
  組み直す前に PlayMode を回した場合、コックピット関連のテストは Fail ではなく
  **Inconclusive**（「確かめられなかった」）になる
- コックピットは **1000 倍の描画空間**にある（1 m = 1 unit）。メートル単位で作られた
  アセットは**スケール 1 のまま**置ける
- **bloom 3.00 / しきい値 0.90 / 拡散 0.70 は Demo 2 で太陽を見て決めた値。Demo 3 では変えない。**
  画面の文字が潰れるなら画面の発光側を下げて対処する

### Demo 2 で作った道具はそのまま使う

検証ハーネス（`-scenario` / `F2` / `F3` / `ScenarioCapture` / `StandaloneCapture`）、
`F1` のデバッグ HUD、`F4` のデバッグパネル（38 項目）は Demo 3 でも使う。
**使い方は §0-B / §0-C にある（Demo 2 専用の記述ではない）。**
目で決める値は F4 で振り、閉じたときのログをコードの定数へ反映する運用も同じ。

### 運用メモ

- claude.ai の Gmail / Google Calendar コネクタは認証しない方針。未認証の
  システム通知が出ても、作業報告に含めなくてよい。
- **実行時に効く値と、アセットに保存された値を二重管理しない。** どちらが正かを
  コードのコメントに書く。（2026-08-27: ポストプロセスの強度が二重管理で、
  **Step 6 以来 bloom が実行時に一度も効いていなかった。** 現在は
  `Core/PlanetAppearance.cs` を唯一の出所とし、`PostProcessPreset.Awake` が
  実行時に必ず適用する）
- **画面に対する「比」を測るときは、解像度とアスペクト比を定数で固定し、ログに条件を
  併記する。** 垂直画角が同じでも、横に広い画面ほど物体の占める割合は小さく出る。
  （2026-08-28: 窓の投影面積比が同じ目の位置で 640x480 なら 10.4 %、1920x1080 なら 7.8 %。
  **どちらも正しいが、条件を書かない数字は比較に使えない。** Demo 2 の
  `UniverseConstants.RadiansPerPixel` を 1080p 固定にしたのと同じ形。**Demo 4 でも同じ**）
- **報告に数値を載せるときは、実行結果に由来するものだけを実測として書く。**
  書式を示すための例を書く場合は、値をダミーと分かる形（例: `<n>x<n>`）にするか、
  「例」と明記する。**実測でない数値を実測として提示しない。**
  （2026-08-27: 実装前に書式見本として書いた `36x36` を、実装後の報告と
  コミット `d5120fe` に「実測」として持ち込んだ。観測経路が無い値だった）

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
| [docs/asset-sources.md](docs/asset-sources.md) | する | 出典・ライセンス一覧。**Demo 2 分は確定済み。** Demo 3 の Asset Store 分もここに追記する |
| [docs/audio-candidates.md](docs/audio-candidates.md) | する | 音の選定経緯と**ループ加工のパラメータ**（Demo 2 で確定済み） |
| `source_assets/AUDIO_INVENTORY.txt` | しない | 音声 360 本の長さ / Hz / ch / ピーク。ffprobe から再生成できる |

**出典の記録は `source_assets/` に置かない。** あそこは丸ごと gitignore されるので、
リポジトリに残らない。ライセンス関係は必ず `docs/` 側に置くこと。

Kenney の 4 パックは `source_assets/audio/kenney/<パック名>/` に展開済みで、
すべて **CC0**（クレジット任意）。**採用した 7 本はすべて Kenney。**
freesound の 2 ファイルは**ライセンス未確認のまま不採用**とした（採用素材を CC0 に統一）。

### Demo 3 で増える置き場所

| | |
| --- | --- |
| Asset Store のダウンロード物 | `%APPDATA%\Unity\Asset Store-5.x\<publisher>\<category>\<name>.unitypackage`（**リポジトリ外**） |
| 取り込み先 | `unity/Assets/ThirdParty/<Publisher>/`（**追跡除外。EULA のため**。Step 11-0 で `.gitignore` に追加する） |

**取り込みは追跡除外のコミットの後に行う。** 順序を逆にすると、取り込んだ瞬間に
アセットが public リポジトリに乗る。

---

## 0-A. Demo 3 で確定した値

**実機で目で見て決めた値。コードの定数が正で、F4 は振るためだけの口。**
Step が進むたびに追記する。

| 決めたこと | 値 | 出所 |
| --- | --- | --- |
| 目の位置（プレハブ原点基準） | 左右 0.00 / 高さ 0.429 / 前後 -1.436 m | 11-2b。`CockpitDefinition.EyeLocal` |
| 視野角 | 60 度（変更なし） | 11-2b |
| 前方・上方 | 前方 +Z / 上方 +Y | 11-2c。**2 軸で持つ**（1 軸だと前後と上下が同時に反転しうる） |
| 計器の割り当て | **案 A**（大画面 = 飛行 / 目標、HUD = 整列、ゲージ = ダイヤル / AP） | 11-3a〜c。案 B / C は**削除済み** |
| 計器の見せ方 | **逆歪ませ**（`ScreenMode.Prewarp`） | 11-3c。3 択は残す（下記） |
| 画面の発光強度 | **0.75**（`CockpitDefinition.DefaultScreenEmission`） | 11-3b。bloom しきい値 0.90 の下 |
| 下端の帯 | **撤去**。ただし**箱コックピットのときだけ残す** | 11-3c。箱には画面が無い |
| 補助光 | **ON / 強さ 0.35**（`CockpitDefinition.DefaultFillLightIntensity`） | 11-4。点光源・範囲 3 m・影なし・目の 0.35 m 上 / 0.30 m 後ろ |
| 真っ黒率の線 | **7 %**（`CockpitLighting.MaxBlackRatio`） | 11-4。**合否ではなく「補助光が効いていること」を捕まえる線**（OFF 8.1〜8.9 % と ON 5.7〜6.3 % の間） |
| 内装の発光 | **撤去** | 11-4。強さを 0.0 と 1.0 で振っても**内装の画素が 1 つも変わらなかった**（4 場面とも最大差 0） |
| コックピットのアセット | **無料の Hi-Rez サンプルのまま続ける（有料は買わない）** | 11-5。実機で見て十分と判断。11-6 は実施しない |

### 「計器の向き」の 3 択を残す理由（PCVR）

**逆歪ませは「片目・平面モニタ」専用の細工。** 傾いた盤に貼った絵が歪んで
見えるぶんを、RT の中身を先に逆へ歪ませて打ち消している（`ScreenWarpSolver`）。

**VR では両眼視差があるので、傾いた盤は傾いた盤として正しく見える。**
そこで逆歪ませを掛けたままにすると、「歪んだ絵が貼られた盤」に見えてしまう。
**Demo 4 の直後に PCVR を予定している**ので、そのときは F4 の「計器の向き」を
**「面に貼る」へ戻す。** 3 択と切り替えの仕組みはそのために残してある
（見比べ用の足場ではなく、**VR で要る機能**）。

「正対」（面の位置に視線へ正対するクアッドを置く）も残す。逆歪ませが
成立しない面（平面でない画面）が 11-6 で出てきたときの逃げ道になる。

### 補助光について測って分かったこと (11-4)

**内装の明るさ（内装マスクでの実測 / 1920x1080 / 画角 60 度）:**

| 場面 | 補助光 OFF | 補助光 ON (0.35) |
| --- | --- | --- |
| cockpit-view | 平均 17.50 / 真っ黒 8.3 % | 平均 20.94 / 真っ黒 **5.9 %** |
| earth-close-terminator | 平均 21.11 / 真っ黒 8.9 % | 平均 24.43 / 真っ黒 **6.3 %** |
| earth-close-night / sun-hidden | 平均 17.38 / 真っ黒 8.1 % | 平均 20.86 / 真っ黒 **5.7 %** |

内装マスクは**コックピット段の culling mask を空にした絵との差分**で作る。
固定の矩形で測らない（目の位置や機体が変われば内装の位置も変わる）。
`.\tools\run_unity.ps1 -Method SolarSetup.MeasureCockpitLighting` で再現できる。

**他の段へ漏れていないことの測り方（2 回間違えた）:**

- **内装を写したまま強度を上げてはいけない。** 明るくなった内装が bloom と
  トーンマップを通じて画面全体を変え、「漏れた」と区別できない
  （最初にそれで「内装の外で 644,291 画素が変化」という誤った数字を出した）
- **遠くの天体で測っても効き目が無い。** 点光源は 1/d^2 で減衰するので、
  `cullingMask` を外しても 0 画素のまま。**何を変えても 0 なら、その検査は
  何も言っていない**（対照を必ず取る）
- 使える形: **コックピット段を描かずに**強度 50 / 範囲 1e6 で比べる -> 0 画素。
  既定の強度では内装の外が 4,385〜5,567 画素動くが、これは内装が明るくなった
  ことによる画面効果（上の 0 画素と両立する）

**分かっていないこと:** 漏れを止めているのが `Light.cullingMask` なのか、
カメラ段の culling mask なのか、距離減衰なのかは**切り分けられていない**。
念のためレンダリングレイヤー（内装 0x3 / 補助光 0x2）も設定してあるが、
**それが効いている証拠は取れていない。** Demo 4 で station 内部を作るときは、
**近くに検査用の物を置いて**対照付きで測り直すこと。

---

## 0-D. XR（Step 12 のスパイク）で入れたもの

**ブランチ `spike/xr-stack` の作業。main には入っていない。**

| パッケージ | 版 | 出所 |
| --- | --- | --- |
| `com.unity.xr.management` | 4.5.4 | Susuwatari Mirror（Quest 3 で動作実績 / 同じ Unity 6000.3.11f1）に合わせた |
| `com.unity.xr.openxr` | 1.16.1 | 同上。レジストリ最新の 1.19.0-pre.1 は pre なので採らない |
| `com.unity.xr.mock-hmd` | 1.5.0-exp.3 | batchmode 用。**ID は `mockhmd` ではなく `mock-hmd`**（前者は存在しない） |

依存で入るもの: `com.unity.xr.core-utils` 2.5.3 / `com.unity.xr.legacyinputhelpers` 2.1.13。

### `com.unity.xr.legacyinputhelpers` の復活について

**Demo 0 で「旧 XR 入力。Input System 1 本でいく」として削除したパッケージが復活した。**
`xr.management` と `xr.openxr` の**両方が依存**しているため（2.1.11 / 2.1.2 を要求）、
親を消さずに外すことはできない。

**方針は変えない。Input System 1 本のまま。このパッケージの API はコードから使わない。**
旧 Input Manager の軸が再生成されるなどの副作用が出たら、**直しに行かず症状として記録する。**

### 起動経路（`XrBoot`）

**無指定なら XR に一切触らない。** `-xr`（実機 / OpenXR）と `-xrMock`（batchmode /
MockHMD）を付けたときだけローダを選んで初期化する。

`Assets/XR/XRGeneralSettingsPerBuildTarget.asset` は `SolarSetup.ConfigureXr` が
**CLI から**作る（GUI を要する手順を持ち込まない）。**`InitManagerOnStart` は false。**
true だと、ローダを登録した時点で起動と同時に XR が立ち上がり、引数を見る前に
平面の絵が変わる。EditMode テストで false を縛っている。

### 実測（12-0c）

**1. パッケージ追加で平面の絵は変わっていない。**

`dfae1e4`（追加前）と `28601b7`（追加後）で `SolarSetup.CaptureXrStack` の
**36 枚を撮り比べて、差分は 0 画素**（最大差 0）。**シーンは追加後のものに固定**して
撮った（シーンの再生成による差を混ぜないため）。同じコミットで 2 回撮った雑音の
下限も 0 画素なので、この 0 は「測れていない」ではなく「変わっていない」。

exe をビルドすると URP アセットの `m_PrefilterXRKeywords` が **1 -> 0** に変わる
（XR キーワードの間引きをやめる）。**その後にもう一度撮っても 36 枚とも 0 画素。**

**2. MockHMD は batchmode の Editor でも exe でも初期化できる。**

ただし**目のテクスチャが作られるのは exe だけ。**（12-0d の実測）

| 実行形態 | 初期化 | device | stereo | eyeTex |
| --- | --- | --- | --- | --- |
| batchmode Editor (`ProbeXrMock` / PlayMode) | **成功** | Mock HMD Display | MultiPass | Tex2D **256x256** / volumeDepth 1 |
| スタンドアロン exe (`-xrMock`) | **成功** | Mock HMD Display | **SinglePassInstanced** | **Tex2DArray 1512x1680 / volumeDepth 2** |

**256x256 / Tex2D は「XR を使っていないとき」と同じ値。** batchmode の Editor には
描く先が無いので目のテクスチャが作られず、**起動前の既定値がそのまま読める。**
これは「MultiPass だった」ではなく「測れていない」。→ §0-B にホップを 1 行足した。

**3. SPI を実際に通すには設定アセットだけでは足りない (12-0d で解決)。**

`MockHMDLoader.Initialize()` は `MockHMDBuildSettings.Instance` を読んでから
`MockHMD.SetRenderMode` を呼ぶ。この `Instance` は `EditorBuildSettings` の
**`xr.sdk.mock-hmd.settings` キー**から引くので、アセットが `Assets/XR/Settings/` に
在るだけでは引けない。12-0c 時点でこのキーが未登録で `Instance` が null になり、
**アセットに `renderMode: 1` と書いてあっても既定の MultiPass で走っていた。**

`SolarSetup.ConfigureXr` が `EditorBuildSettings.AddConfigObject` で登録するように
した (`XrSetup.RegisterMockSettings`)。**対照付きの実測:**

| exe のビルド時 | frame 1 以降の実測 |
| --- | --- |
| キー登録あり | **SinglePassInstanced / Tex2DArray 1512x1680 / volumeDepth 2** |
| キー登録なし（対照。手で外して再ビルド） | MultiPass / Tex2D 1512x1680 / volumeDepth 1 |

対照でも目のテクスチャ自体は 1512x1680 で作られる。**「XR が動いていない」のではなく
「MultiPass で動いている」**ことが区別できている。

**4. 立体視の値を読む時機を間違えない。**

`XRSettings.eyeTextureDesc` は**表示サブシステムが描き始めるまで埋まらない。**
`XrBoot.Initialize` の直後に読むと、XR を使っていないときと同じ
Tex2D 256x256 / volumeDepth 1 が読める（12-0c はこれを「MultiPass」と読み違えた）。
`XrFactsLogger` が frame 1 / 2 / 10 / 60 / 120 で読み直してログに落とす。

**「設定した」と「経路が通っている」は別。** テストも 2 種類に分けてある:

| テスト | 何を主張しているか |
| --- | --- |
| `XrBootTests.設定アセットにはSPIと書いてある_経路の証拠ではない` | 設定は入っている |
| `XrBootTests.MockHMDの設定がEditorBuildSettingsに登録されている_経路の証拠ではない` | 設定は引ける |
| `XrStereoFactsPlayModeTests.MockHMDがSinglePassInstancedで走る` | **実行時に SPI で走っている**（batchmode では Inconclusive） |

**5. exe をビルドすると `m_PrefilterXRKeywords` が 1 -> 0 に変わる。**

`Assets/Settings/PC_RPAsset.asset`。**XR シェーダキーワードの間引きをやめる**設定で、
XR パッケージを入れると Unity がビルド時に書き換える。**絵は変わらない**
（36 枚とも 0 画素）が、**シェーダバリアントが増えるのでビルド時間と exe のサイズに
効く。** 追跡ファイルなので勝手に戻さないこと（戻しても次のビルドでまた変わる）。

### 12-3（実機ホップ）で確認すること

**最優先: 実機で `[XrFacts]` が SPI になっていること。**

```powershell
build\SolarSystemExplorer.exe -xr -logFile logs\xr_real.log
# ログの [XrFacts] 行を読む（frame 1 / 2 / 10 / 60 / 120 の 5 行が出る）
```

| 期待 | 値 |
| --- | --- |
| `stereoRenderingMode` | `SinglePassInstanced` |
| `eyeTextureDesc.dimension` | `Tex2DArray` |
| `volumeDepth` | `2` |

**ここが MultiPass だったら Q1（SPI で目のインデックスが Overlay 段に伝わるか）の
問い自体が成立していない。** OpenXR 側は `xr.openxr.settings4` が登録済みで
`m_renderMode: 1` なので SPI のはずだが、**MockHMD で同じ「はず」が外れた**
（上の 3）ので、実機でも値を読むまで通さない。

### 立体視の理論値 (Step 12-1 / `Core/StereoGeometry.cs`)

**XR に依存しない純粋な計算。** 後のセッションで実測と突き合わせる基準になる。
**実測とずれても、この式を実測に合わせて変えないこと。** ずれたら疑うのは
描画側（目のインデックスの伝播・段ごとの姿勢の配り方・スケールの掛け忘れ）。

| | |
| --- | --- |
| 頭の位置・IPD・ベースライン | **[m]**（HMD が返す単位） |
| 段の中の距離 | **[units]** |
| 換算 `s` | コックピット段 **1.0** / 外 3 段 **0.001**（1 unit = 1 km） |
| 回転 | **スケールしない。** 位置だけ s 倍する |
| 焦点距離 | `f = (H/2)/tan(FOV/2)` = **935.3074360872 px**（1080p / 60 度）。`AngularSizeSolver` と同じ式 |

**測って分かったこと: 外 3 段には見える視差が無い。**

| 対象 | 距離 | 段 | 視差 (IPD 63 mm) |
| --- | ---: | --- | ---: |
| 計器 | 0.5 units (= 0.5 m) | コックピット | **117.85 px** |
| 地球 | 1.2e4 units | 外 3 段 | 4.91e-6 px |
| 火星 | 8e3 units | 外 3 段 | 7.37e-6 px |
| 太陽 | 1 AU | 外 3 段 | 3.94e-10 px |

**1 px の 20 万分の 1。** 外 3 段は左右の絵が画素として同一になる。
頭を 0.3 m 動かしても地球の方向は 2.3e-5 px しか変わらない。
**立体に見える視差を持つのはコックピット段だけ。**

**Δθ = baseline / D は小角近似。** 指定された式なので変えないが、
計器 0.5 m を 0.3 m 動くと厳密な atan と **10 % 違う**（0.600 対 0.540 rad）。
切り分け用に `DirectionChangeExactRadians` を並べてある。外 3 段では比が
1e-8 以下なので差は double の丸め以下。

### f と IPD は実行時に測る (Step 12-1b)

**平面の見積もりを XR の入力にしない。** `StereoGeometry` は f も IPD も
**既定値を持たない**（渡し忘れると通らない）。実行時の値は Unity 側の
`XrStereoOptics` が測り、`[XrOptics]` としてログに出る（frame 1 / 2 / 10 / 60 / 120）。

MockHMD の実測（frame 1〜120 で完全に一定）:

| | 平面の見積もり | MockHMD の実測 |
| --- | --- | --- |
| 目テクスチャ | 1920x1080 | **1512x1680** |
| 縦画角 | 60 度（`Camera.fieldOfView`） | **111.29 / 111.66 度**（左/右。HMD の投影行列が決める） |
| m11 | 1.732051 | **0.683565 / 0.678844**（左右で 0.7 % 違う） |
| f [px] | 935.31 | **574.19 / 570.23**（平面の 0.614 倍） |
| 眼間 | 63 mm（仮定） | **22.0 mm** |

**この差は無視できない。** 計器 0.5 m の視差は、平面の見積もりだと 117.85 px、
実測の入力だと **25.26 px。比が 4.66。** 見積もりのまま「実測 / 理論」を取ると、
描画が正しくても 1.0 から外れる。

**眼間は 4 段とも同じ 0.022 units で出る。** コックピット段（1 m = 1 unit）では
22 mm だが、**外 3 段（1 unit = 1 km）では 22 m 相当。** 頭姿勢の段配布を
まだ実装していないので、XR が同じメートル単位のずれを 4 段すべてに掛けている。
**事実として記録するだけで、直していない。**

### 頭姿勢の段配布 (Step 12-2 / `XrEyeRig`)

XR はトラッキング位置も眼間も**メートルで**カメラに掛ける。段のスケールを知らないので、
配布しないと外 3 段では 1000 倍の量が効く（実機 62.4 mm が 62.4 m 相当）。

**各段のカメラを `XrEyeAnchor_*` の下へ移し、親のスケールを段のスケールにする。**
コックピット 1.0 / 外 3 段 0.001。計算は 12-1 の `StereoGeometry.CameraLocal`。
**`-xr` / `-xrMock` のときだけ作る**（平面の 36 枚は実測で 0 px）。
`-xrNoEyeRig` で配布を切れる（対照用）。

| 段 | lossyScale 配布前 | 配布後 | 眼間 (MockHMD) 配布前 | 配布後 |
| --- | ---: | ---: | ---: | ---: |
| Deep / Near / Nearfield | 1.0 | **1e-3** | 2.20e-2 units | **2.20e-5 units** |
| Cockpit | 1.0 | 1.0 | 2.20e-2 units | （下記） |

**`[XrOptics]` の眼間は段ごとの値ではない (12-2 で判明)。**
`Camera.GetStereoViewMatrix` は 4 段とも同じ目の位置 (0, 0, ±0.011) を返す。
カメラの位置が違っても同じ値なので、**これは段ごとの実測になっていない。**
配布後にコックピット段の行まで 2.20e-5 に変わるが、これは共有された値を
読んでいるためで、**コックピット段の描画は変わっていない**（下記の対照）。
**段ごとに効いているかは `lossyScale` を見ること。**

**コックピット段を壊していないことの対照（画素）:**

| 比較（内装が支配的な下 1/3） | 差のある画素 |
| --- | ---: |
| 同じ設定で 2 回（実行ごとの雑音の下限） | 46,584 / 47,313 |
| 配布 off と on | 47,018 / 47,468 |

**雑音の下限と同じ大きさ。** 微振動が実行をまたいで再現しないので、1 回の実行の
中では 0 画素でも、実行が違えば必ずこの程度は動く。**配布の有無で区別が付かない
＝コックピット段は変わっていない。**

**Nearfield の左右重心では確かめられなかった。** プローブの重心差は 4 段とも
-42〜-72 px で、これは MockHMD の左右の投影の非対称（主点の差）が支配的。
理論上の視差の変化は D=20 units で 0.63 px -> 0.0006 px しかなく、この指標の
分解能を下回る。**至近距離のステーションが見える場面が要る**（現行のシナリオに無い）。

### 12-2b で確かめられたこと / 確かめられなかったこと

**場面 `station-close` を足した** (`ScenarioLibrary`)。ステーションの中心まで
**0.5 units**（半径 0.25 units なので表面から 0.25 units、標準停止位置 0.3 units の外）。
配布が絵に届いたかを測れるのはこの段（Nearfield）だけ。
`ScenarioTests.ステーションが視線を塞がない` からは名指しで除外してある
（**規則を緩めたのではなく、目的の違う 1 件を外した**）。

**1. 配布は絵に届いている。** ステーションの左右のずれを相関で測った:

| | 最良のずれ [px] |
| --- | ---: |
| 配布 OFF | **-67** |
| 配布 ON | **-53** |
| 配布 ON（2 回目） | -53（再現する） |

差 **14 px。** 理論値（f 574.19 / 眼間 2.20e-2 / D 0.5）は 25.26 px。
**合っていない（実測/理論 = 0.55）。**

**2. 画素の突き合わせに使える土俵がまだ無い。**

| 比較（640x480 全画面 / 世界を止めて撮影） | 差のある画素 | 最大差 |
| --- | ---: | ---: |
| 同じ設定で 2 回（雑音の下限） | 76,343 / 80,497 | 43 / 39 |
| 配布 off と on | 167,694 / 184,703 | 157 / 173 |

`Time.captureFramerate = 60` で刻みを固定し、撮影の直前に `Time.timeScale = 0`
にしても**雑音の下限は 0 にならない。** 差は小さく（最大 43、平均輝度は
72.388 対 72.395）内装側に集中しているので、時間ではなく**描画側に非決定な
要素が残っている**（TAA / ディザ等。特定していない）。
**この土俵では「差が無い」ことを示せない。**

**3. ミラーウィンドウの画素は目テクスチャの画素ではない。**

撮影は 640x480 のミラーだが、目テクスチャは 1512x1680。**対応関係を
確かめていない**ので、`f`（目テクスチャ基準の 574 px）で出した理論値を
ミラーの画素と直接比べられない。上の 0.55 倍はこれで説明が付く可能性がある
（幅で換算すると 10.7 px、高さで換算すると 7.2 px。実測 14 px はその間）。
**角度空間の比較も同じ理由で使えていない**（-348 mrad = -20 度という
あり得ない値が出る）。**先にミラーと目テクスチャの対応を決めること。**

**4. 主点のずれ（実測 / MockHMD）。**

| | 左 | 右 | 差 |
| --- | ---: | ---: | ---: |
| `m00` | 0.759723 | 0.753787 | — |
| `m02`（横） | -0.056852 | 0.058936 | 0.115788 |
| `m12`（縦） | -0.000487 | -0.005681 | -0.005194 |
| 主点 横 [px]（幅 640 換算） | -18.19 | 18.86 | **37.05** |
| 主点 縦 [px]（高さ 480 換算） | -0.12 | -1.36 | -1.25 |

**横は 37 px ずれている。** 12-2 で「4 段とも -42〜-72 px」と出たのは
これが乗っていたため。**画素座標のまま左右を比べない。**
縦のずれは 1.25 px で小さい。

### `Assets/XR/` について

XR パッケージを入れると Unity が自動生成する（ローダ選択と設定）。**第三者アセットでは
ないので追跡する**（`.gitignore` に入れるとローダの選択が別マシンで失われる）。
番人テスト `ThirdPartyTrackingTests` の許可リストに `XR` を足したが、**中身は
`.asset` と `.meta` だけ**に絞ってある（テクスチャや `.unitypackage` が来たら落ちる）。

### RenderMode の値（**パッケージごとに確かめること**）

| パッケージ | 列挙 | 生成された既定値 |
| --- | --- | --- |
| `com.unity.xr.openxr` | `OpenXRSettings.RenderMode` : 0 = MultiPass / 1 = SinglePassInstanced | `m_renderMode: 1` = **SPI** |
| `com.unity.xr.mock-hmd` | `MockHMDBuildSettings.RenderMode` : 0 = MultiPass / 1 = SinglePassInstanced | `renderMode: 0` = **MultiPass** |

**並び順が同じであることはソースを 2 つとも読んで確かめた。** 片方の値をもう片方に
当てはめない。**MockHMD の既定は MultiPass なので、明示的に SPI にする。**

**アセットに 1 と書くだけでは効かない。** `EditorBuildSettings` への登録が要る
（→ 上の「実測」3）。そして**その結果を確かめられるのは exe だけ**（→ §0-B）。

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
| 描画 | **MockHMD が SPI で描いているか（`eyeTextureDesc`）** | 無い。**exe を `-xrMock` で起動して `[XrFacts]` を読む** | 確立済み（Step 12-0d）。**batchmode の Editor は目のテクスチャを作らない**（PlayMode でも `-executeMethod` でも Tex2D 256x256 = XR を使っていないときと同じ値）。`XrStereoFactsPlayModeTests` は batchmode で **Inconclusive** になる。**スタンドアロン exe + MockHMD なら SPI で走る**ので、**SPI 固有の片目落ちはそこで画素判定できる**（12-0d の訂正 / 12-C）。実機ホップは OpenXR ランタイム側の確認のためだけに要る |
| 描画 | **SRP Lens Flare が `Camera.Render()` → RenderTexture 経路で描かれるか** | 要実測 | **未確認。** 現行のスクショは `Deep.Render()` で RT に描いている。乗らない場合は exe 経由（Step 7 で確立）へ切り替える |
| 描画 | 「眩しい」「暗すぎる」の判断 | 輝度分布の測定 | 閾値の妥当性は人手 |
| 描画 | **HDR 値（bloom しきい値を超えたか）** | ARGBHalf の RT へ描いて float で読む | 確立済み（Step 9-1）。**カメラの `renderPostProcessing = false` が必須。`Volume.enabled = false` だけでは ACES が残り、2.4 も 9.6 も 0.59 / 0.63 に潰れて「強度を変えても絵が変わらない」ように見える** |
| 描画 | **bloom が効いているか（滲みの量）** | exe のスクショを画素で比較する。**`Camera.Render()` → RenderTexture でも測れる** | 小さい光源では差が出ない（太陽 4px で強度 0.00 と 0.80 が明部 52 画素と 52 画素、差 0）。**画面いっぱいの地球なら RT でもはっきり出る**（縁の青の比が bloom 0.00 で 0.5369、3.00 で 0.4102）。**「RT では bloom が出ない」と一度書いたが誤り。** 小さい光源で検出できなかっただけ |
| 全般 | **Editor スクリプトで Volume / ScriptableObject の値を変えたとき** | `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` | **しないとアセットに残らず、実行時は既定値で動く。** Step 6 の bloom 消灯はこれ。Editor セッション中は値が生きているので、その場の測定には現れてしまう |
| 全般 | **Asset Store のアセットの入手（Package Manager の My Assets からのダウンロード）** | 無い。人が Editor の GUI で 1 回だけ行う | Asset Store の取得は Package Manager ウィンドウの対話操作で `-batchmode` から叩く口が無い。ダウンロードされた `.unitypackage` は `%APPDATA%\Unity\Asset Store-5.x\` 配下に置かれ、UPM のレジストリパッケージと違い `Packages/manifest.json` には残らない。**そこから `Assets/` への取り込みは `AssetDatabase.ImportPackageImmediately` で CLI に閉じる**（TMP と同じ経路）。取り込み先は EULA のため追跡除外なので、別マシンでの復元は「GUI で再ダウンロード → 取り込みスクリプト」になる。再現手順は `docs/asset-sources.md` に記録 |
| 描画 | **手前の層に遮られて奥の層を画素で測れない**（コックピット・ステーション等） | 測りたくない層の**カメラの culling mask を空にする**。`Camera.enabled = false` は不可 | 確立済み（Step 11-2a）。**URP はスタックの最後のカメラでポストプロセスを適用する**ので、段ごと無効にするとトーンマップと bloom の掛かり方まで変わり、測っている絵が実機と別物になる。mask だけ空にすればカメラは回り続けるので、絵の作られ方は同じ。**Demo 4 のステーションでも同じ形で要る** |
| 描画 | **取り込んだアセットの姿勢の取り違え（前後・上下の反転）** | 配置後の姿勢を数値で縛る。**上がワールドの +Y 側**（dot > 0）／**計器が目の前**（カメラ座標で z > 0） | 確立済み（Step 11-2c）。**実機で見るまで気づけなかった。** `Quaternion.FromToRotation(前方, Z+)` は前方が Z+ と反平行のとき回転軸が一意に決まらず、Unity が X 軸を選ぶと前後と上下が同時に反転する。**前方と上方の 2 軸**で `Inverse(LookRotation(前方, 上方))` を作れば縮退しない。**「窓が目の前にある」では反転を捕まえられない**（キャノピーは操縦者を包むので反転しても前に広がる）。**前にしか無いもの＝計器**で見ること |
| 音 | **音が鳴らない** | `volume` / `pitch` / `Play()` 回数を記録する | batchmode では原理的に不可 |
| 音 | **AudioMixer を使う手順** | 使わない。`Core/AudioMix.cs` + `Unity/AudioRouting.cs` で同等のことを行う | **`AudioMixer` アセットを作る公開 API が無い。** 生成系（`UnityEditor.Audio.AudioMixerController`）は**すべて internal** で、リフレクションだと壊れたときに実行時まで分からない。GUI で作るのは §0-B の方針に反する |
| 音 | AudioMixer のスナップショット遷移・ローパスの効き | 露出パラメータの値を検証 | 数値は読めるが音は聴けない |
| 描画 | **OnGUI（デバッグ HUD・シナリオの確認項目テキスト・F4 デバッグパネル）** | exe 経由で撮る（Step 7 の `StandaloneCapture`） | `Camera.Render` → RenderTexture の経路には**写らない**。実測済み・PlayMode テストで回帰を見ている。**F4 パネル（§0-C）も同じ**ので、パネルで決めた値の確認は必ず exe で行う |
| 音 | ループの継ぎ目のクリック | **波形解析で数値化できる** | 隣接サンプル差の平均に対する連結点の段差比。**人手不要にできるので EditMode テストへ落とす** |
| 全般 | exe 起動でのスクショ | Step 7 の `StandaloneCapture` | 確立済み。ただし音の自動判定は exe でも不可 |

### XR の検証は自動テストでは縛れない (Step 12-C)

**この領域の成果物は自動テストではなく、exe 実行の数値表と画像。**
batchmode の Editor は目のテクスチャを作らないので、XR まわりのテストは
**Inconclusive**（「確かめられなかった」）にしかならない。緑になっても
何も主張していない。

`run_tests.ps1` の判定は **failed 件数を正**としているので、**Inconclusive は
成功扱いで通過する。** 気付かないまま「全緑」と読まないよう、Inconclusive が
1 件でもあれば警告と**テスト名の列挙**を出すようにした (12-C)。

| 何を確かめたいか | どこで確かめるか |
| --- | --- |
| SPI で走っているか | **exe + MockHMD** の `[XrFacts]`（frame 1 以降） |
| f と眼間の実測 | **exe + MockHMD** の `[XrOptics]` |
| SPI 固有の片目落ち | **exe + MockHMD** の左右 2 枚の画素比較 |
| OpenXR ランタイム側 | **実機ホップ**（そこだけ） |

### 左右 2 枚の最終絵を取り出す経路 (Step 12-C / 成立した)

**`ScreenCapture.CaptureScreenshotAsTexture(StereoScreenCaptureMode.LeftEye / RightEye)`
で取れる。** 経路 2（ミラーウィンドウの読み戻し）は要らなかった。

```powershell
build\SolarSystemExplorer.exe -screen-fullscreen 0 -screen-width 640 -screen-height 480 `
  -xrMock -scenario cockpit-view -xrCaptureDir verify\xr-stereo -xrCaptureFrames 150 `
  -logFile logs\xrstereo.log
```

`XrStereoCapture`（`-xrCaptureDir` が無ければ何もしない）が行うこと:

| | |
| --- | --- |
| **SPI ゲート** | frame 150 で `[XrFacts]` を読み、SinglePassInstanced / Tex2DArray / volumeDepth 2 でなければ**撮らずに終了コード 3 で落ちる**。撮影が MultiPass に落ちたまま全部緑、を防ぐ |
| 条件の併記 | `xr-stack.txt` の先頭に stereo モード / dimension / volumeDepth / 目テクスチャのサイズ / 画面サイズ、続けて**段ごとの f（左右）と眼間**（`[XrOptics]`）|
| 撮影 | **左右を同じフレームで撮る。** フレームをまたぐと微振動の差が混ざり、視差か時間か区別できない |
| 対照 | **同じフレームで左目をもう 1 枚。** 実測で **0 画素**なので、左右の差は本物 |
| 層の確認 | `XrDiagnosticsModel.MeasureStereo` で 4 段の識別色を左右それぞれ数える |

**実測（640x480 / cockpit-view / MockHMD）:**

| | |
| --- | --- |
| 雑音の下限（左目 2 回・同フレーム） | **0 / 307,200 画素** |
| 左右の差 | **228,994 / 307,200 画素**（最大差 202） |
| 明るさの平均 | 左 37.20 / 右 **7.87** |

**右目に地球が写っていない。** 左目には地球が画面いっぱいに写り、右目は星空だけ。
コックピット内装と計器は両目に写っている。

**訂正 (12-C2): 12-C の「層ごとの識別色」は測定を取り違えていた。**
プローブは **`-xrProbes` を付けないと表示されない**（既定は非表示）。付けずに
撮ったので、あの数字はプローブではなく**同じ色相を持つ場面の色**を数えていた。
`-xrProbes` 付きで撮り直した値:

| 段 | 左 [px] | 右 [px] | 比 |
| --- | ---: | ---: | ---: |
| Deep | 364 | 333 | 0.9148 |
| Near | 358 | 4 | 0.0112 |
| Nearfield | 5,865 | 5,877 | 0.9980 |
| Cockpit | 930 | 359 | 0.3860 |

**Cockpit プローブの「576 -> 1」は測定器の欠陥ではなかった。** プローブを出せば
両目に写っている（930 / 359）。プローブの置き場所は変えていないので、
平面の 36 枚の基準値も変わっていない。

**目ごとの描画量のほうが、プローブに依らない robust な指標**（下の 12-C2）。

### 片目落ちの原因は自前シェーダ (Step 12-C2 / 対照で確定)

**`Assets/Shaders/*.shader` の 5 本すべてが SPI のマクロを 1 つも持っていない。**
`UNITY_VERTEX_INPUT_INSTANCE_ID` / `UNITY_SETUP_INSTANCE_ID` /
`UNITY_VERTEX_OUTPUT_STEREO` / `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO` が 0 件
（PlanetSurface / PlanetClouds / SunSurface / SunCorona / ScreenWarp）。
SPI ではこれらが無いとテクスチャ配列のスライス 0（左目）にしか描かれない。

**対照（`-xrSwapShader` で `SolarSystem/*` を URP/Lit に差し替えて撮る）:**

| | 左 平均 | 右 平均 | 右/左 | 左 黒でない画素 | 右 黒でない画素 |
| --- | ---: | ---: | ---: | ---: | ---: |
| そのまま | 37.17 | **8.02** | **0.216** | 165,068 | 95,769 |
| 差し替え | 51.34 | **50.90** | **0.991** | 203,372 | 202,657 |

差し替えると右目にも地球が写る（絵でも確認）。**原因はシェーダ 1 クラス。**
カメラ 4 段はいずれも `stereoTargetEye = Both` / `stereoEnabled = True` で、
`projectionMatrix` / `worldToCameraMatrix` / `cullingMatrix` への代入も
`OnPreCull` / `OnPreRender` の実装も**コード全体に 1 件も無い**（grep 0 件）。

**直していない。** 12-C2 は原因クラスの特定までで、対策は行わない。

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

### 立体視の実態を採る（`[XrFacts]` / Step 12-0d）

**目で見る道具ではないが、実機でしか採れないのでここに置く。**
`XRSettings` の実測値を exe のログに落とす。**`-xr` / `-xrMock` を付けたときだけ**
`XrFactsLogger` が生成され、frame 1 / 2 / 10 / 60 / 120 で読み直す。

```powershell
# 実機 (OpenXR)
build\SolarSystemExplorer.exe -xr -logFile logs\xr_real.log

# HMD 無し (MockHMD)。撮って終わる形にもできる
build\SolarSystemExplorer.exe -screen-fullscreen 0 -screen-width 640 -screen-height 480 `
  -xrMock -scenario cockpit-view -captureShot verify\shots\xrmock.png `
  -captureFrames 150 -logFile logs\xrmock.log
```

```
[XrFacts] frame=1 / stereo=SinglePassInstanced / eyeTex=Tex2DArray 1512x1680 volumeDepth=2 / ...
```

**初期化直後の `[XrBoot]` 行の値は使わない。** そこはまだ目のテクスチャが無く、
XR を使っていないときと同じ Tex2D 256x256 / volumeDepth 1 が読める（→ §0-D）。

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
| 数値 | `_AtmosphereStrength` 0〜10（0.25 刻み）/ `_CloudOpacity` 0〜2（0.05）/ フレア基準強度 0〜2（0.05）/ 微振動の振幅 0〜5e-3（2.5e-4）/ 画面の発光強度 0〜2（0.05）/ 補助光の強さ 0〜2（0.05） |
| 計器 (11-3) | **計器の向き**（面に貼る / 正対 / **逆歪ませ**＝既定）/ 画面のテスト柄 / RT を直接表示 / RT 表示の面（5 面から 1 つ） |

天体ごとに「距離 / 角直径の**計算値** / 画面上の**実測 bbox** / 引き渡し率 / 有効な表現」
を 1 行で出す。**計算値と実測が並んでいるのが肝で、食い違いに目で気付くための表示。**
隅がカメラの後ろにあって投影が破綻するときは `---` を出す（嘘の数字を並べない）。

#### 列の読み方（ここを間違えると食い違いを見誤る）

| 列 | 中身 |
| --- | --- |
| 投影直径(計算) | **実際のカメラの FOV と実際の画面高**から出した球のシルエット直径。`UniverseRoot.RadiansPerPixel`（1080p/60 度の参照値。LOD の切替基準をウィンドウ幅で動かさないための固定値）**ではない** |
| bbox(実測) | **実際に描かれているメッシュの頂点**を投影した外接矩形。描画器の AABB ではない |
| 表現 | `点`/`殻`/`実` = 見えている / `-` = 引き渡し・LOD でアルファ 0 / **`x` = パネルでトグル OFF にした** |

**計算値と実測が数 % 違うのは正常。** 頂点は最大 256 点に間引いているので、
シルエットの最外点を取りこぼすと実測がわずかに小さく出る。**桁で違うときだけ疑う。**
（実測例: 1080p で地球 計算 816.40 / 実測 816x814、720p で 544.27 / 544x543）

`x` と `-` を分けているのは、**同じ「見えない」でも原因が違う**ため。
`x` は自分で消したもの、`-` は引き渡しや LOD が消したもの。

#### 計器の切り分け道具 (11-3)

| 項目 | 何が分かるか |
| --- | --- |
| 計器の向き | 面に貼る（歪む）/ 正対（クアッドを浮かせる）/ 逆歪ませ（既定）の比較。**VR に入るときは「面に貼る」へ戻す**（→ §0-A） |
| 画面のテスト柄 | 正方形の格子・真円・四隅の印。**円が真円か・格子が正方形か**で貼り方の歪みが分かる |
| RT を直接表示 | 貼る前の RT を**画面中央に等倍**で出す。1 px の白枠が 4 辺とも見えていれば切れていない。倍率を画面に文字で出す |
| RT 表示の面 | 上の対象を 5 面から選ぶ |

RT の中身そのものを数値で確かめたいときは、画面ではなく
`.	oolsun_unity.ps1 -Method SolarSetup.DumpScreenTextures` を使う。
RT を CPU へ読み戻して PNG と円の外接矩形を出し、**船を回したときに何画素
変わるか**も測る（→ §5 の float の刻みの話）。

#### 見切れの確認

起動引数 `-debugPanel` を付けると最初から開く。`StandaloneCapture` は
`ScreenCapture` 経由なので OnGUI が写る。解像度を変えて撮れば見切れを確認できる。

```powershell
build\SolarSystemExplorer.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 `
  -scenario earth-close-day -debugPanel -captureShot verify\shots\panel.png -captureFrames 180
```

収まること自体は `Core/DebugPanelLayout.cs` の純関数を EditMode テストで縛っている
（1920x1080 / 1280x720 の両方）。**OnGUI は batchmode で描けない**ので、
数値で縛る以外に自動化の手が無い。

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

- **原点から離れた場所に、描画元やオフスクリーンの装置を置かない。**
  （2026-08-28 / Step 11-3c で踏んだ。**Demo 4 でも同じ罠がある**）

  計器の Canvas を「他のカメラに写らないように」と `(1e5, 1e5, 0)` に置いていた。
  **写らないのはレイヤーで決まっている**ので、離す必要はそもそも無かった。

  | | |
  | --- | --- |
  | 原点からの距離 | 141,528 unit |
  | そこでの float の刻み | 2^17 × 2^-23 = **0.0156 unit** |
  | 扱う最小単位（Canvas の 1 画素） | 1/544 = 0.0018 unit |
  | 比 | **刻みのほうが 8.5 倍大きい** |
  | 症状 | 船を 0.5 度回すと RT の **48,620 画素**が変わり、テスト柄の円が 32 px 動く |
  | 直した後（距離 52 unit） | 船を 90 度回しても **0 画素** |

  手順はこの順で見る。

  1. **離す理由が「他のカメラに写らないように」なら、まずレイヤーで解決できないか**
     を見る（カリングマスク）。たいていそれで足りる
  2. どうしても離すなら、**その距離での float の刻みを、扱う最小単位と比べる。**
     刻みのほうが大きければ、**中身を変えていないのに絵が変わる**
  3. **症状は「動かすと揺れる」。静止画では出ない。** スクショの比較では
     見つからないので、**姿勢を変えて 2 回撮り、画素を比べる**

  `SolarSetup.DumpScreenTextures` が、RT を CPU へ読み戻して
  「船を回したときに何画素変わるか」を出す。同じ形で Demo 4 でも測れる。

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
