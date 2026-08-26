# Demo 2（見た目デモ）実装計画：惑星・太陽・音

最小デモ（コミット 6599d5b、Step 0〜7）の上に、新機能を足さずに「画面に映るもの」を本物へ置き換える。対象は **惑星の表現 / 太陽の表現 / 音** の3つ。コックピット・ステーションの3Dモデル差し替えは Demo 3 以降。

Step 番号は最小デモの続きで **Step 8〜10**、共通の小物を **Step 8-0** とする。各 Step は従来通り「Claude Code で実装 → run_tests.ps1 全緑 → batchmode スクショで目視確認 → コミット＋タグ」のゲート方式。

**batchmode で検証できないホップの一覧は [CLAUDE.md](../CLAUDE.md) §0-B にある。**
Demo 3 以降でも使う性質の表なので、計画書ではなく CLAUDE.md 側に置いた。
各 Step のテスト欄を書くときは必ず突き合わせること。

---

## 0. 前提（現状の構成と守ること）

- Unity 6 / URP 17.3.0 / 1 unit = 1 km / 浮動原点 / Core は UnityEngine 非依存
- カメラ4段: Deep [500, 1.2e4] / Near [100, 1.5e5] / Nearfield [0.01, 100] / Cockpit [0.1, 100]
  - 遠方の天体は Deep 段の**プロキシ殻**、接近後は Near 段の**実スケールメッシュ**に引き渡される。**惑星シェーダは両方に同じマテリアルを使う**（見た目の連続性を保つため）
- ポストプロセスは Medium（bloom 0.80 / しきい値 1.05 / vignette 0.22）で確定済み。太陽光強度 3.0、スカイボックス _Exposure 1.0
- **惑星・太陽のマテリアルは Transparent（`_Surface: 1` / `_ZWrite: 0`）。** LOD 切替と実スケール引き渡しのクロスフェードに使っている。深度を書かないことが §8-3 と §9-3 に波及する
- **`CelestialBodyView` が `_BaseColor` を MaterialPropertyBlock で毎フレーム上書きする。** `Body.Color`（Core の LDR 値）から RGBA 4 成分すべてを書くので、**マテリアルに焼いた RGB は初回フレームで消える**。強度は別プロパティに逃がすこと（§8-2 / §9-1）
- **太陽は現在まったく HDR ではない。** `Sun_Mesh.mat` は URP/Unlit・`_BaseColor` (1,1,1,1)・`_EmissionColor` 無し・`_EMISSION` キーワード無効。`MaterialLibrary` の `emissive: true` を渡す呼び出しは 1 箇所も無く、`EmissionIntensity = 4.0f` は**デッドコード**。出力は 1.0 で頭打ちなので bloom しきい値 1.05 を構造的に超えられない（§9-1）
- **`.inputactions` に F1 のバインドは無い**（`f1` の一致 7 件は GUID の一部）。§8-0 で新規に足す
- 既知の罠: Volume.profile ではなく sharedProfile を使う / VolumeProfile.Add<T>() の結果は AssetDatabase.AddObjectToAsset が必要 / Camera.nearClipPlane は 0.01 にクランプされる / batchmode PlayMode ではキー入力を再現できない（InputOverride 経由）
- リポジトリは PUBLIC で unity/Assets/Textures/ を追跡中。追加テクスチャは **CC BY 4.0 のクレジットを README に必ず追記**する

## 1. ゴールと完了条件

| 項目 | 完了条件（目視） |
|---|---|
| 惑星 | 地球ステーション出港時、地球に青い大気の縁・雲・夜側の街灯り・海の鏡面ハイライトが見える。火星到着時、火星に地形の陰影と薄い橙色の縁が見える |
| 太陽 | 視界に太陽が入るとレンズフレアとコロナが出て「眩しい」。太陽が視界外へ出るとフレアが消える |
| 音 | 出港→巡航→制動→ドッキングの一連で、エンジン音が速度に追従し、ドッキング時に金属音が鳴り、無音の区間がない |
| 共通 | F1 でデバッグ表示を消したスクショが「デモ」ではなく「ゲーム画面」に見える |

---

## 2. Step 8-0：共通の小物（半日）

やること
- デバッグ文字列（左上の OnGUI）を **F1 でトグル**。既定は非表示にし、起動引数 `-debugHud` で初期表示に切替
- 巡航中の**微振動**: Cockpit 段のカメラ親に、スラスト量に比例したパーリンノイズ揺れ。ドッキング中は 0
  - **並進ではなく回転で揺らす（決定）。** 並進はカメラからの距離で効き方が変わり、
    無限遠は 1 画素も動かない。実測（`RadiansPerPixel` = 1.069e-3）:

    | 揺らし方 | 窓枠 (2 units) | ステーション (20 units) | 惑星・星空 (∞) |
    |---|---:|---:|---:|
    | 並進 0.0003 unit (= 0.3 mm) | 0.14 px | 0.014 px | **0 px** |
    | 回転 1.5e-3 rad (= 0.086 度) | 1.4 px | 1.4 px | **1.4 px** |

    並進 0.3 mm では**画面上で誰も動かない**（星空は原理的に 0 px）。
    回転なら視界全体が同じだけ動くので、振幅を角度で決められる。
  - **既定は 1.5e-3 rad（約 1.4 px）。** `CockpitShake.MaxAmplitudeRadians` の 1 定数で調整する
  - **並進版は実装しない。** 見えないコードを残さない
    （`MaterialLibrary.EmissionIntensity = 4.0f` がデッドコードのまま残った轍を踏まない）
  - カメラと枠は**一緒に**揺らす。カメラだけ揺らすと枠が泳いで見える。
    実機ではカメラは枠に固定されていて、枠は静止したまま外の景色が揺れるのが正しい。
    ship の下に `CockpitRig` を挟み、`Cam_Cockpit` と `Cockpit` の共通の親を揺らす
- スクショ撮影ヘルパーに「F1 オフ状態で撮る」オプションを追加（以降の Step の目視確認用）

検証ハーネス（8-0 のスコープに追加）
- 起動引数 `-scenario <name>` でシナリオを指定して起動する。**引数なしなら従来どおりの通常プレイ**
- シナリオは「船の位置・姿勢・目標・時刻・カメラ設定」の初期状態と、画面右上に出す確認項目（1〜3 行）を持つ
  - 定義は **Core 側**（`Scenario` / `ScenarioStart` / `ScenarioLibrary`）。UnityEngine 非依存にして EditMode テストで妥当性を検証する
  - 姿勢は Quaternion ではなく **`LookAt` + `Up`** で持つ。Core に Quaternion は無く、オイラー角は順序の解釈で揺れるため。破綻条件（`Position != LookAt` / `Up` が零でない）をテストで直接見られる
  - 位置は `Create(model)` がモデルから解決する。リテラル座標を Core に固定しない
  - `ScenarioStart.SunDirectionOverride`（`Vec3d?`）で太陽方向を上書きできる。**8-0 では未設定**。Step 8 の earth-close で明暗境界線の角度を変えるのに使う
- 同じ定義を batchmode の撮影からも使う: `SolarSetup.CaptureScenario`
  - 既定の出力先は `verify/shots/`（gitignore の内側・フル解像度 PNG）
  - `-hero` を付けたときだけ `docs/screenshots/demo2/` へ 1280x720 の JPEG。**追跡対象なので Step ごと最大 3 枚**
  - `-diag` で段ごとに切り分けたスクショと描画物の一覧を出す（何が写っているか分からないときの切り分け用）
- 実行中に **F2 で次 / F3 で前**のシナリオへ切り替える
- 8-0 時点のシナリオは `harness-selftest` 1 つ。以降の Step で増やす

> **シナリオの視点はステーションを背にして置くこと。** ポート正面や真横に立つと
> ステーション本体が視界を塞ぐ。EditMode テストで「視線から 45 度以内に
> ステーションが来ない」ことを保証している。

> **EditMode の撮影は計器の 10 Hz 更新を跨ぐまで回すこと。** 4 tick（0.067 秒）だと
> 一度も更新されず、初期化直後の値（速度 0.9c / 距離 1 AU）が写る。`SettleTicks = 20`。

テスト
- EditMode: `.inputactions` に F1 / F2 / F3 のバインディングがあること / シナリオ定義の妥当性（名前の一意性・確認項目 1〜3 行・`Position != LookAt`・`Up` が非零・目標番号が範囲内・画角 10〜120 度・時刻が非負・未知名で null）/ **ステーションが視線を塞がないこと** / 振幅の純関数（スラスト比例・ドッキング中 0・1 画素以上 5 画素以下）
- PlayMode: `-scenario` 無しで従来どおり始まること / シナリオの初期状態が定義どおりに適用されること / F1 で HUD が反転し押しっぱなしで連射しないこと / F2・F3 でシナリオ番号が循環すること / 微振動がスラストに追従しドッキング中に止まること / **HUD の状態が RT スクショに影響しないこと**（OnGUI が RT 経路に写らないことの回帰）

完了条件
- F1 オフのスクショが撮れる
- `-scenario harness-selftest` で初期状態のスクショが撮れる

> **F1 オン/オフの対比は exe で撮る。** OnGUI は `Camera.Render` → RenderTexture の
> 経路に写らないため、batchmode の RT 撮影では両者が同一画像になる
> （[CLAUDE.md](../CLAUDE.md) §0-B）。確認項目テキストも同様。

---

## 3. Step 8：惑星の表現（2〜3日）

### 8-1 テクスチャ追加とサイズ方針

Solar System Scope（CC BY 4.0、既存と同シリーズ）から追加取得:
- 地球: `earth_clouds` / `earth_nightmap` / `earth_normal_map` / `earth_specular_map`
- 火星: 法線マップは**入手できなかった**。NASA MOLA 標高図から生成する案は Demo 2 の枠に対して重いので、**§8-6 は「Demo 2 では省略」で確定**（§7 に将来案として記録）

サイズ方針（決定事項として記録する）
- 既存 8k は 52.6MB を public リポジトリで配布中。追加分は **4k に縮小**して取り込む（デモでは 4k で十分、リポジトリ肥大を抑える）。8k 原本は source_assets/（gitignore 済み）に置く
- 縮小は Editor スクリプト（`SolarSetup.ImportPlanetTextures`）で source_assets/ から自動生成し、手作業を残さない

実素材の素性（実測。全て 8192x4096）

| ファイル | mode | 実測 |
|---|---|---|
| `8k_earth_clouds.jpg` | RGB | **R=G=B のグレースケール。アルファ無し。** 値域 0–227 / 平均 70.8 |
| `8k_earth_specular_map.tif` | RGB | **R=G=B のマスク。海=255 / 陸=0**（サハラ 0・太平洋 255・アマゾン 0・大西洋 255 で確認） |
| `8k_earth_normal_map.tif` | RGB | R[14,242] G[6,237] / **B は全域 255 固定**。全画素の 15.5% が (128,128) から外れる = 起伏は本物 |

> `8k_earth_normal_map.tif` の B が 255 固定なのは通常の接空間法線と違うが、
> Unity の NormalMap インポート（DXT5nm）は B を捨てて再構成するので実害は無い。
> **B を読む前提のコードを書かないこと。**

色空間とインポート設定（**決定**）

| テクスチャ | Texture Type | sRGB | 出力形式 | 理由 |
|---|---|---|---|---|
| albedo / nightmap / clouds | Default | **ON** | JPEG 可 | 見た目の色 |
| normal_map | **NormalMap** | （自動で Linear） | **PNG 必須** | JPEG のクロマサブサンプリングが X/Y 成分を壊す |
| specular_map | Default | **OFF（Linear）** | PNG 推奨 | 輝度ではなく**マスク**。sRGB で読むと海岸線の中間値が歪む |

縮小時の扱い（**決定**）
- **normal / specular はガンマ変換を挟まず、生の線形データとして縮小する**
- **法線マップは縮小でベクトルが非正規化される。** シェーダ側で `normalize` する
- albedo / night / clouds は本来リニア空間で縮小すべきだが、sRGB 符号化のまま
  箱型フィルタを掛ける。**近似であることを承知のうえで採用する**（見た目の差は小さい）

取り込み対象の絞り込み（**取り込みスクリプト共通のルール**）
- **拡張子のホワイトリストで拾う。** テクスチャは `.jpg` / `.png` / `.tif` / `.exr` のみ。
  ディレクトリを走査して「あるものを全部」入れない
- `desktop.ini` / `Thumbs.db` / `.DS_Store` / `*.url` / `*.txt` / `*.md` は**必ず除外**する。
  Windows や配布元が混ぜてくる非素材ファイルで、Unity に入れても意味が無く、
  `.meta` が増えて差分が汚れる
- 先頭が `.` または `_` のファイル・ディレクトリも除外する（Unity 自身が無視する規則に合わせる）
- **除外した件数とファイル名をログに出す。** 黙って捨てると、
  取り込まれなかった素材に気付けない

### 8-2 惑星シェーダ（**手書き `.shader`、単一 Forward パス**）

`PlanetSurface.shader` を1つ書き、地球・火星でプロパティだけ変える。

**Shader Graph は使わない（決定）。** `.shader` はテキストなので `SolarSetup.Run` の前に
置くだけでよく、GUI を挟まず CLI で完結する。リスク表にあった
「GUI が必要な唯一のホップ」はこの決定で消える。

**「URP Lit ベース」ではなく自前の単一 `UniversalForward` パスにする。**
`UniversalFragmentPBR` を呼ぶ本格 PBR は、構造体シグネチャの URP バージョン結合・
ShadowCaster/DepthOnly/DepthNormals/Meta の自前実装・`multi_compile` の網羅を抱え込む。
この案件では次の理由でそれらがほぼ不要:

- 惑星は **Transparent / ZWrite off** なので ShadowCaster・DepthOnly・DepthNormals は元々意味を持たない
- 光源は **Directional Light 1 灯のみ**。追加ライト・シャドウのバリアントが要らない
- **SRP Batcher は既に効いていない**（`CelestialBodyView` が MaterialPropertyBlock を使うため）。CBUFFER の厳密さは性能上ほぼ無意味

必要なのは `Core.hlsl` + `Lighting.hlsl` の `GetMainLight()` だけを使い、
Lambert 拡散 ＋ 簡易鏡面 ＋ フレネル ＋ Emission を自分で書く形。
§8-2 の要求機能は全て手計算の範囲で、PBR の BRDF は要らない。

残る欠点（承知のうえで採用）
- ノードプレビューが無く、反復は batchmode スクショのみ（1 サイクル 30 秒前後）
- コンパイルエラーはログに出るが、UV の取り違えのような警告未満の誤りは絵を見るまで分からない
- インクルードは **URP 17.3 に結合する**。使う関数を `GetMainLight()` 程度に絞って影響を小さくする

**守るべき制約（既存コードとの契約）**
- プロパティ名は**厳密に `_BaseColor`**（`Shader.PropertyToID("_BaseColor")` 決め打ち）
- **`_BaseColor` のアルファを出力アルファに掛ける。** LOD 切替と実スケール引き渡しのクロスフェードがこれで動いている
- **Transparent / ZWrite off を維持する**
- **`_BaseColor` の RGB は毎フレーム MPB に上書きされる。** HDR 強度や色の作り込みを `_BaseColor` に焼いてはいけない。強度は `_EmissionIntensity` のような**別プロパティ**に持たせる（MPB が触らないので生き残る）

これを外すと Step 2・3b のクロスフェードが無言で壊れる。既存の PlayMode テスト
（被覆率の単調性・アルファ合計 1.0）が検出はするが、設計段階で意識すること。

入力プロパティ
- Albedo / Normal / Specular（海マスク）/ NightLights / AtmosphereColor / AtmosphereStrength / AtmospherePower / NightIntensity

機能
1. **法線マップ**: Normal スロットに接続（火星は無ければ未接続）
2. **海の鏡面**: Specular マップ（白=海）を Smoothness に流す。陸 0.1、海 0.85 程度。Metallic は 0
3. **夜側の街灯り**: `NdotL = dot(N, L)` を `smoothstep(0.1, -0.15, NdotL)` でマスクし、NightLights × NightIntensity を Emission に加算。**明暗境界線をまたいで滑らかに切り替わる**ことが肝
4. **大気の縁（フレネル）**: `fresnel = pow(1 - saturate(dot(N, V)), AtmospherePower)`。昼側だけ光らせるため `wrap = saturate(NdotL * 0.5 + 0.5)` を掛け、`AtmosphereColor × fresnel × wrap × AtmosphereStrength` を Emission に加算
   - 地球: 青 (0.35, 0.55, 1.0)、Power 3.5
   - 火星: 橙 (1.0, 0.6, 0.35)、Power 5、Strength は地球の 1/4（薄い大気）

太陽方向 L は Directional Light の向き（Main Light）をそのまま使う。**プロキシ殻でも実スケールメッシュでも、方向ベクトルは同じなので見た目は一致する**（Step 2 の角直径照合で位置関係は検証済み）。

### 8-3 雲層（地球のみ）

- 地球球の子に半径 **×1.006** の球を1つ追加（実寸 +8km だと 1 画素にならないため見た目優先）。同じシェーダは使わず、Transparent の Unlit または Lit を使う
- **`earth_clouds` は輝度（`.r`）をアルファとして読む（決定）。**
  素材はアルファを持たないグレースケール JPEG で、RGB は完全に同一と実測済み。
  RGBA PNG を生成し直すと 4k で 8MB 前後になり §8-1 の 30MB 枠を圧迫するため、
  **テクスチャは作り替えず、シェーダ側で `.r` を読む。**
  - 素材の最大値は 227（= 0.89）、平均 70.8（= 0.28）。そのままでは雲が最大 89% の不透明度にしかならないので、**ゲイン `_CloudOpacity` を掛けられるようにする**
- **描画順は `renderQueue` を地表より +1 して確定させる（決定）。**
  雲球と地表球は**同心**なので、Transparent の既定ソート（バウンディング中心のカメラ距離）が
  同値になり順序が不定になる。カメラ角度やフレームで地表と雲が入れ替わり得る。
  **深度で解決しない**（ZWrite off が前提のため）。角度を振ったスクショ検証は残すが、
  不定性そのものは `renderQueue` で潰す
- 自転とは別に **Y 軸まわりを 1 周 / 現実の 20 時間程度**で回す（地表より少し速く、雲が流れて見える）。UniverseClock の時刻から角度を導出し、フレームレート非依存に
- 雲の影を地表に落とすのは省略（効果に対して工数が大きい）
- プロキシ殻と実スケールメッシュの**両方**に同じ構造で付ける（引き渡し時に雲が消えないこと）

### 8-4 惑星の自転

- 現状は固定。UniverseClock から「地球 23.93 時間 / 火星 24.62 時間」で Y 軸回転を導出。等倍時間なので体感は極めてゆっくりだが、5 分の航行で地球が 1.25° 回る = 出港時と帰港時で明暗境界線が僅かに動く
- **回転を載せる Transform（決定）**
  - `CelestialBodyView` が毎フレーム上書きするのは **`mesh` の `localScale`**（と `realMesh` の位置・回転・スケール）。
    したがって**自転は `mesh` の親 Transform に載せる**。`mesh` 自身に載せると毎フレームのスケール書き込みと競合する
  - **雲球は自転速度が違うので、さらに別の Transform に載せる。** 地表の回転を継承させない
  - ステーションは `Stations` 配下の別階層なので巻き込まれない（航路とポート位置は不変）
- 自転軸の傾き（地球 23.4°）は今回は入れない（ステーションの位相角 90° の配置が崩れるため。将来の要決定に記録）

### 8-5 プロキシ殻との整合

- プロキシ殻は Deep 段でスケールされた球。シェーダはワールド法線と視線方向しか使わないので同じマテリアルで正しく描ける
- 確認点: 引き渡し（帯 5e4→3e4 units）の前後でスクショを撮り、**大気の縁の太さと色が連続**していること（Step 2 の被覆率検証と同じ手順）

### 8-6 火星の地形陰影（**Demo 2 では省略。決定**）

- **火星の法線マップは入手できていない。Demo 2 では省略で確定する。**
  - §1 の火星側の完了条件は「地形の陰影と薄い橙色の縁」だが、**必須は「薄い橙色の縁」まで**とする
  - MOLA 標高図から高さ→法線を生成する経路は、取得・投影の整合・スケール決めが要り、Demo 2 の 1 日枠に対して重すぎる（§7 に将来案として記録）
- **地球の法線マップは本物なので、§8-2 の法線スロット自体は地球で検証できる**（全画素の 15.5% が平坦から外れる）

テスト
- EditMode: マテリアルに必要テクスチャが全て割り当てられていること / 地球と火星のプロパティ値が仕様表と一致
- PlayMode（画素検証）:
  - 地球ステーション出港直後のスクショで、地球円盤の縁 3 画素幅の平均色が中心より青成分が高い（大気の縁）
  - 夜側（NdotL<0 の領域）に輝度 > 0 の画素が一定割合ある（街灯り）
  - 引き渡し前後のスクショで縁の色差が閾値以下（連続性）
- 既存 **108/31 件**が全緑のまま（Step 7 で EditMode +6 / PlayMode +7）

完了条件
- 出港時の地球、到着時の火星のスクショが「写真」に見える

コミット目安: 8-0 / 8-1〜8-2 / 8-3〜8-4 / 8-5〜8-6 の 4 回

---

## 4. Step 9：太陽の表現（1日）

### 9-1 太陽ディスクの HDR 化

**「上げる」ではなく「効いていない仕組みを作り直す」作業。**
現状 `Sun_Mesh.mat` は URP/Unlit・`_BaseColor` (1,1,1,1)・`_EmissionColor` 無し・
`_EMISSION` キーワード無効で、`MaterialLibrary` に `emissive: true` を渡す呼び出しが
1 箇所も無い。`EmissionIntensity = 4.0f` はデッドコード。
**出力は 1.0 で頭打ちのため、bloom しきい値 1.05 を構造的に超えられない。**
Step 6 で太陽が小さく暗い灰色の点に見えたのはこれが原因（当時は露出の問題と誤って説明した）。

- 太陽プロキシ殻（8k_sun.jpg の球）が **HDR 強度 4〜8** を出せるようにする（bloom しきい値 1.05 を十分に超える値）。ACES で白飛びするが太陽なので正しい
- **強度は `_BaseColor` に焼かない（決定）。**
  `CelestialBodyView.ApplyColors()` は `Body.Color`（Core の LDR 値）から RGBA 4 成分すべてを
  MaterialPropertyBlock で毎フレーム `_BaseColor` に書くため、**焼いた HDR 値は初回フレームで消える**。
  強度は `_EmissionIntensity` のような**別プロパティ**に持たせる。MPB は触らないので生き残る
  - 責務の分離: **MPB = 描画状態（クロスフェードのアルファ）/ マテリアル = 見た目（強度・色の作り込み）**
  - デッドコードの `MaterialLibrary.EmissionIntensity` と `emissive` 引数は、この作業で整理する
- 太陽面の縁を少し暗くする周辺減光（limb darkening）: フレネルの逆（中心 1.0 → 縁 0.6）を Emission に掛ける。これだけで「平坦な球」感が消える

### 9-2 コロナ（ビルボード）

- 太陽の子に、常にカメラを向く Quad を1枚。半径は太陽本体の **×2.5**。放射状グラデーション（中心 白 → 縁 透明、少し橙）を Editor スクリプトでプロシージャル生成（256×256、外部素材不要）。Additive ブレンド
- Deep 段のプロキシ殻に付ける（太陽は常に Deep 側。近距離下限 7.844e5 units は Step 2 で確認済みで、ミッション範囲では到達しない）

### 9-3 SRP Lens Flare

**§9-3 は既に半分実装済み。** Step 6 で `SceneBuilder` が Directional Light に
`LensFlareComponentSRP` を付け、`LensFlareBuilder` が `SunFlare.asset` を生成している。
現状は**手続き生成の円 3 枚 / `intensity = 0.6` / `attenuationByLightShape = false`**。
新規作成ではなく拡張と調整の作業になる。

- 円形ゴーストを **3 枚 → 4〜6 個＋光条 1 個**に増やし、強度を 0.3 前後から再調整する
- **オクルージョンは使わない（決定）。**
  SRP Lens Flare のオクルージョンは深度バッファ参照だが、**惑星もステーションも
  Transparent（`_ZWrite: 0`）で深度を書かない**。Deep 段のプロキシ殻も Near 段の実スケール
  メッシュも同様なので、**惑星の裏に回ってもフレアは消えない**。これは実測を待つまでもない。
  - **惑星に深度を書く専用パスは足さない。** Transparent 前提のクロスフェード（LOD 切替・
    実スケール引き渡し）を壊すため。§7「やらないこと」に記録した
  - **代替案「引き渡し率でフレア強度を減衰させる」を本線とする。** `CelestialBodyView` の
    `RealScaleBlend` が既にあるので、接近して惑星が画面を占めるほどフレアを弱める
- 視界外で自動的に消える（コンポーネント標準機能）

### 9-4 露出の再調整

- 太陽の HDR 化で bloom の総量が増えるため、Medium の bloom 0.80 を **0.6〜0.8 で再測定**。Step 6 の測定手順（Bloom強度/しきい値/Vignette/四隅の明るさ）を再実行して数値で決める
- 太陽光強度 3.0 は変更しない（惑星の明るさを崩さない）

テスト
- EditMode: Directional Light に LensFlareComponentSRP が付き、Data アセットが AddObjectToAsset 済みであること / コロナ Quad の親と半径倍率
- PlayMode（画素検証）:
  - 太陽が視界中央のスクショで、太陽中心の輝度が 255 かつ半径 ×2.5 の範囲に外側へ単調減少する輝度分布がある（コロナ）
  - 太陽を視界外へ向けたスクショで、画面内の最大輝度が惑星面の最大値以下（フレアが残っていない）
  - 火星へ接近して引き渡し率が上がった位置で、フレア強度が単調に低下すること（オクルージョンではなく引き渡し率による減衰）

完了条件
- 太陽が視界を横切るときに「眩しい」と感じ、視界外で消える

コミット目安: 9-1〜9-2 / 9-3〜9-4 の 2 回

---

## 5. Step 10：音（1〜2日）

### 10-1 素材の調達（CC0 優先）

freesound.org で **CC0** のみを選び、source_assets/audio/ に置いた上で必要分だけ unity/Assets/Audio/ に取り込む。出典（ID・作者・URL）は `docs/credits.md` に記録（CC0 でも記録しておく）。

Kenney（CC0）のパックも同じ置き場に展開して使う。出典は [asset-sources.md](asset-sources.md) に記録済み。

取り込み対象の絞り込み（**8-1 と同じルール**）
- **拡張子のホワイトリストで拾う。** 音声は `.ogg` / `.wav` のみ
- `desktop.ini` / `Thumbs.db` / `.DS_Store` / `*.url` / `License.txt` / `Preview.ogg` は除外する。
  **実例: `kenney/sci-fi-sounds/Audio/` に `desktop.ini` が混入していた。**
  `Preview.ogg` はパック全体の試聴用で素材ではない
- 先頭が `.` または `_` のファイル・ディレクトリも除外する
- **除外した件数とファイル名をログに出す**

> `License.txt` は取り込まないが、**中身は `docs/asset-sources.md` に転記済み**。
> 取り込みから外すことと、記録を残さないことは別。

採用素材（**確定**）

全て Kenney の CC0。`source_assets/audio/kenney/<パック>/Audio/` から取り込む。
選定の経緯と不採用の理由は [audio-candidates.md](audio-candidates.md)。

| 用途 | ファイル | パック | 長さ[s] | ch | ピーク | 備考 |
|---|---|---|---:|---:|---:|---|
| エンジン | `spaceEngineLow_003.ogg` | sci-fi-sounds | 5.000 | 1 | -1.0 dB | **ループ加工が前提**（下記） |
| コックピット | `forceField_000.ogg` | sci-fi-sounds | 0.954 | 1 | -0.9 dB | **ループ加工が前提**（下記） |
| ドッキング | `impactPlate_heavy_001.ogg` | impact-sounds | 0.352 | 2 | -0.9 dB | 単発 |
| 出港 | `switch_004.ogg` | interface-sounds | 0.500 | 2 | -1.0 dB | 単発 |
| UI 選択 | `select_001.ogg` | interface-sounds | 0.043 | 2 | -1.1 dB | 単発 |
| UI 確定 | `confirmation_003.ogg` | interface-sounds | 0.322 | 1 | -1.0 dB | 単発 |
| 警告 | `error_008.ogg` | interface-sounds | 0.139 | 1 | -1.0 dB | 「要求NG」用 |

**エンジン音とコックピット音は、どちらもループ加工してから取り込む（決定）。**

**Kenney の 5 秒素材はループ用にオーサリングされていない。** 実測:

| 候補 | ループ端の段差 / 隣接サンプル差の平均 |
|---|---:|
| `spaceEngineLow_003`（採用） | **85.16 倍** |
| `spaceEngineLow_000` | 33.84 倍 |
| `spaceEngineLarge_001` | 10.81 倍 |
| `computerNoise_000` | 9.08 倍 |
| `engineCircular_002` | 6.26 倍 |

`spaceEngineLow_003.ogg` は先頭/末尾のサンプル値が 821 / 16406、段差 15585 に対して
隣接サンプル差の平均が 183.0。**毎周ハッキリしたクリックが鳴る。**
フェードイン・アウトは無い（先頭 100ms RMS 14182 / 末尾 14136 / 中央 14656）ので、
素材としてはループに向くが、端の処理だけが未加工。

加工の方針は 2 つで別物:

| 用途 | 問題 | 加工 |
|---|---|---|
| エンジン `spaceEngineLow_003` | **ループ端のクリック**（85 倍の段差） | 末尾を先頭に **50〜100 ms クロスフェード**するだけ。周期 5 秒は要件を満たしている |
| コックピット `forceField_000` | **周期が 0.954 秒と短い**（段差は 0.05 倍で元々無い） | ピッチ 0.97 / 1.00 / 1.03 の 3 層を重ねて **8 秒**に伸ばす |

pitch 0.9〜1.2 で駆動すると周期は 5.56〜4.17 秒に変わる。**クリックがあると高 pitch ほど
頻度が上がって目立つ**ので、加工は必須。

コックピット音の手順とパラメータは [audio-candidates.md](audio-candidates.md) の
「コックピット音のループ加工」に記録済み。**加工版の採否は試聴して決める。**

---

**インポート設定（決定）。素材の形式では決めない。**

Kenney は全て OGG。単発音を WAV に再エンコードしても **Vorbis で失われた情報は戻らない**
（不可逆 → 可逆でファイルが太るだけ）。実行時の形は `AudioImporter.defaultSampleSettings` が決める。

| 用途 | Load Type | Compression Format | 意図 |
|---|---|---|---|
| エンジン / コックピット（ループ） | Compressed In Memory | Vorbis | 常時鳴るので常駐。長さがあるので圧縮 |
| ドッキング / 出港 / UI / 警告（単発） | Decompress On Load | PCM | 0.04〜0.5 秒と短い。発音遅延を作らない |

- `preloadAudioData` は全て ON（初回発音の詰まりを避ける）
- 2ch の単発（`impactPlate_heavy_001` / `switch_004` / `select_001`）は `forceToMono` を ON にしてよい。2D 再生でステレオを保つ意味が薄く、容量が半分になる

---

**加工音の置き場所と再現性（決定）。**

`source_assets/` は `.gitignore` の内側なので、**clone した環境には原本が無い。**
「取り込み時に原本から生成する」案は成立しない。テクスチャと同じ契約に揃える。

1. **加工済みクリップを `unity/Assets/Audio/` にコミットする**（これが唯一の配布物）
2. **生成は C# の Editor スクリプトで行う。** `run_unity.ps1` だけで完結し、ffmpeg への
   環境依存を CLAUDE.md の前提に持ち込まない。Step 6 の `EngineAudio.CreateRumbleClip` で
   同種の処理を書いた実績がある
   - 現在 `source_assets/audio/preview/make_forcefield_loop.sh` は gitignore の内側にある。
     **C# へ移植したうえで、シェルスクリプトは参照用として `tools/` へ移す**
3. パラメータ（層数・ピッチ比・オフセット・クロスフェード長）と**加工後の実測値**
   （長さ・ピーク・段差比）を [audio-candidates.md](audio-candidates.md) に記録する。
   再生成物が一致することを検証可能にするため
4. **ループ端の段差比は EditMode テストで検証する。**
   隣接サンプル差の平均に対する連結点の段差比を測れば、聴かずに回帰を捕まえられる
   （CLAUDE.md §0-B のホップ表で「人手不要にできる」と分類した項目）
`forceField_000.ogg` は 0.954 秒しかなく、素のままループさせると約 1 秒周期で
反復に気付く。ピッチ 0.97 / 1.00 / 1.03 の 3 層を開始位置をずらして重ね、
末尾 0.2 秒を先頭へクロスフェードして 8 秒のループにする。
手順とパラメータは [audio-candidates.md](audio-candidates.md) の
「コックピット音のループ加工」に記録済み。**加工版の採否は試聴して決める。**

> 当初の表は「WAV / OGG」を用途ごとに指定していたが、**素材形式ではなくインポート設定で決める**方針に変更した（上表）。

> freesound は**不採用**。ライセンス未確認だったことと、採用素材を CC0 に統一したため。
> 上の「CC0 のみを選び」という方針自体は変えない。将来 freesound を使うなら要確認。

### 10-2 AudioMixer 構成

- Master ─ Engine / Cockpit / SFX / UI の 4 グループ。各グループに露出パラメータ（Volume）
- Docked 状態で Engine グループに **ローパス（2kHz）**をかけ「エンジンが休んでいる」感を出す
- **ローパスは Mixer 側に一本化する（決定）。** Step 6 の `EngineAudio` は AudioSource に
  `AudioLowPassFilter` を付けて cutoff 220〜1400 Hz をスラストで駆動しているが、**撤去する。**
  二重掛けを残さない
- Mixer と Snapshot（Flying / Docked）は Editor スクリプトで生成し、シーンと同様に手作業を残さない

### 10-3 エンジン音の速度連動

**Step 6 の `EngineAudio` は置換する（決定）。**

撤去するもの:
- `Scripts/Unity/EngineAudio.cs`（プロシージャル生成の 4 秒クリップ、volume 0.06→0.45）
- `Editor/PostProcessProfileBuilder.cs` 内の `EngineAudioClipBuilder`（生成コード）
- `Assets/Materials/EngineRumble.asset`（生成物。リポジトリに追跡されている）
- AudioSource 側の `AudioLowPassFilter`（§10-2 のとおり Mixer に一本化）

一本化後の構成: **Core の `EngineAudioModel` ＋ 素材クリップ ＋ Mixer 側ローパス。**

- AudioSource（2D、Loop）を Ship に 1 つ。制御は Core 側に `EngineAudioModel`（UnityEngine 非依存）を置き、入力（スラスト 0〜1、速度ダイヤル段、AP 状態）→ 出力（volume 0〜1、**pitch 0.9〜1.2**）を純粋関数で決める
  - スラスト 0: volume 0.25 / pitch 0.9（アイドル）＝ pitch の下限
  - スラスト 1: volume 0.7 / pitch 1.2 ＝ pitch の上限
  - 制動中: pitch を 1.05 で一定、volume 0.6（逆噴射感）
  - 変化は 0.5 秒の一次遅れで滑らかに（急変を避ける）
- 亜光速でも音は変えない（速度そのものは無音。「船の状態」だけを音にする方針）

### 10-4 イベント音

- ドッキング状態遷移をフックする: Docking 開始で接合音、Docked 確定で短い確認音、Undocking でクランプ解除音、Approaching 入りで小さな通知音
- Tab / Enter / BackSpace / T / G の各操作に UI 音。「要求NG」表示時は否定音
- 二重再生防止: 同一イベントは 0.2 秒以内に再発火しない

### 10-5 スナップショット切替

- ドッキング状態の Docked ⇄ それ以外で Mixer Snapshot を 1.5 秒クロスフェード

テスト（音は batchmode で聴けないため、パラメータと発火を検証する）
- EditMode: `EngineAudioModel` の入出力表（スラスト 0 / 0.5 / 1、制動中）が仕様値と一致 / 一次遅れの時定数 / 全素材が Audio/ に存在しインポート設定（Load Type、Compression）が仕様通り
- PlayMode: 出港→巡航→制動→ドッキングを InputOverride で通し、AudioSource の volume/pitch のログが単調に変化すること / 各イベント音が期待回数だけ Play() されたこと（AudioSource をラップした `IAudioPlayer` に記録させる）/ Docked でローパスのパラメータが 2000 になること
- **最終的な聴感確認は Editor GUI または build/ の exe で人手**。「うるさすぎる / 小さすぎる」の閾値は数値化せず、体感で決めて数値を記録する

完了条件
- 出港から着港まで無音区間がなく、音量バランスに違和感がない

コミット目安: 10-1〜10-2 / 10-3 / 10-4〜10-5 の 3 回

---

## 6. 実施順序と目安

```
8-0 共通小物       0.5日   F1 トグル・微振動
8   惑星           2〜3日  テクスチャ→シェーダ→雲→自転→整合
9   太陽           1日     HDR ディスク→コロナ→フレア→露出再調整
10  音             1〜2日  素材→Mixer→エンジン→イベント→スナップショット
```

順序の理由: 惑星が最も効果が大きく、太陽の露出調整は惑星の明るさが決まってからでないと再測定が二度手間になる。音は画面と独立なので最後で良い。

各 Step 完了時に **F1 オフのスクショ 3 枚（出港直後の地球 / 巡航中の太陽 / 到着時の火星）**を docs/screenshots/demo2/ に保存し、最小デモの同位置のスクショと並べて差分を確認する。

## 7. Demo 2 で「やらない」こと（要決定として記録）

- 雲の影、地球の自転軸の傾き、大気の物理的散乱（レイリー）
- 火星の砂嵐・極冠の季節変化
- 太陽フレアの物理的正しさ（見た目優先）
- 速度に連動した音の変化（等倍時間・亜光速でも無音が正しい）
- コックピット・ステーションの3Dモデル差し替え（Demo 3）
- **火星の法線マップ**（NASA MOLA 標高図から高さ→法線を生成する案）。§8-6 は Demo 2 では省略で確定。将来やるなら別タスク
- **惑星に深度を書く専用パス**。§9-3 のオクルージョンのために検討したが、Transparent 前提のクロスフェードを壊すため足さない（§9-3）

## 8. リスク

| リスク | 対策 |
|---|---|
| プロキシ殻と実スケールの間で大気の縁の太さが変わる | フレネルは視線角のみに依存するので原理上一致するはず。ずれたら AtmospherePower を段ごとに補正するのではなく原因（法線のスケール）を直す |
| 太陽 HDR 化で惑星が bloom に埋もれる | 太陽光強度は据え置き、bloom 強度側で吸収。Step 6 の測定手順を再実行して数値で確定 |
| ~~Lens Flare のオクルージョンが Near 段を見ない~~ | **解消済み（リスクではなく確定事項）。** 惑星は Transparent で深度を書かないためオクルージョンは原理的に効かない。9-3 で引き渡し率による減衰を本線に決定 |
| 4k 縮小でも public リポジトリが肥大 | 追加分は合計 30MB 以内を目安。超えるなら Git LFS 移行を別タスクで検討 |
| **30MB 枠が法線マップ次第で危うい** | 法線マップは JPEG に落とせない（クロマサブサンプリングが X/Y を壊す）ため PNG 必須で、4k PNG は数〜十数 MB になり得る。**縮小後に実測し、枠を超えたら 2k への再縮小か LFS 移行を判断する。** clouds / nightmap は JPEG で 1MB 前後に収まる見込み |
| 手書き `.shader` が URP 更新で壊れる | 使う関数を `GetMainLight()` 程度に絞り、単一 Forward パスに留める。URP 17.3 に結合していることを CLAUDE.md に記録 |
| freesound の素材が CC BY だった | CC0 のみ採用。見つからなければ Unity 付属のサンプル音か、プロシージャル生成（ノイズ＋ローパス）で代替 |

## 9. ライセンス／クレジット更新

- README.md の Credits に追記: Solar System Scope の追加テクスチャ（雲・夜景・法線・鏡面、CC BY 4.0）
- `docs/credits.md` を新設し、音素材の出典（freesound ID、作者、ライセンス）を一覧化
- コロナ・フレアのテクスチャはプロシージャル生成のため出典不要
