# アセットの出典とライセンス

`source_assets/` に置いた素材の出どころ。**`source_assets/` 自体は `.gitignore` で
除外されるので、記録はこのファイル（追跡対象）に残す。**

> 音素材の採用は確定済み。テクスチャ側はまだ Demo 2 の作業前。
> 確定した内容は `docs/credits.md`（Demo 2 で新設予定）と `README.md` へ反映すること。

原本（消さないこと）:

```
D:\Users\pipe_render\Downloads\solar-system-explorer\texture\
D:\Users\pipe_render\Downloads\solar-system-explorer\sound\
```

`%USERPROFILE%` は `C:\Users\pipe_render` だが、**原本は D: ドライブにある。**
`%USERPROFILE%\Downloads\...` では届かない。

---

## 1. テクスチャ — `source_assets/textures/`

| | |
| --- | --- |
| 提供元 | Solar System Scope <https://www.solarsystemscope.com/textures/> |
| ライセンス | **CC BY 4.0**（クレジット表記が必須）<https://creativecommons.org/licenses/by/4.0/> |
| 表記 | `README.md` に記載済み |

| ファイル | サイズ | 状態 |
| --- | ---: | --- |
| `8k_earth_clouds.jpg` | 11.08 MB | Demo 2 で追加 |
| `8k_earth_daymap.jpg` | 4.35 MB | 最小デモで使用中 |
| `8k_earth_nightmap.jpg` | 3.00 MB | Demo 2 で追加 |
| `8k_earth_normal_map.tif` | 9.05 MB | Demo 2 で追加 |
| `8k_earth_specular_map.tif` | 1.76 MB | Demo 2 で追加 |
| `8k_mars.jpg` | 8.01 MB | 最小デモで使用中 |
| `8k_sun.jpg` | 3.53 MB | 最小デモで使用中 |

`starmap_2020_4k.exr`（NASA SVS *Deep Star Maps 2020*）は今回のコピー対象外。
最小デモで導入済みで、出典は [02-assets.md](02-assets.md) と `README.md` に記載済み。

---

## 2. 効果音（Kenney） — `source_assets/audio/kenney/`

4 パックすべて **CC0**（Creative Commons Zero 1.0）。
<http://creativecommons.org/publicdomain/zero/1.0/>

個人・教育・商用で利用可。**クレジット表記は「あると嬉しいが必須ではない」**（License.txt の原文: 
"Credit (Kenney or www.kenney.nl) would be nice but is not mandatory."）。
本プロジェクトでは任意表記だが、`docs/credits.md` には記録する方針。

各パックに同梱されているのは `License.txt` と `.url` ショートカットのみ。**readme は無い。**

| パック | 正式名 | 作者 | 作成日 | 音声 |
| --- | --- | --- | --- | ---: |
| `casino-audio` | Casino Audio (1.1) | Kenney Vleugels (Kenney.nl) | 記載なし | 54 |
| `impact-sounds` | Impact Sounds (1.0) | Kenney (www.kenney.nl) | 2019-12-19 | 130 |
| `interface-sounds` | Interface Sounds (1.0) | Kenney (www.kenney.nl) | 2020-02-11 | 100 |
| `sci-fi-sounds` | Sci-Fi Sounds (1.0) | Kenney (www.kenney.nl) | 2020-10-11 | 73 |

展開元 zip は `source_assets/audio/kenney_<パック名>.zip`、
ライセンス原文は `source_assets/audio/kenney/<パック名>/License.txt`。

### 採用ファイル（試聴のうえ確定）

| 用途 | 採用ファイル | パック |
| --- | --- | --- |
| 1 エンジン | `spaceEngineLow_003.ogg` | sci-fi-sounds |
| 2 コックピット | `forceField_000.ogg`（**ループ加工が前提**） | sci-fi-sounds |
| 3 ドッキング | `impactPlate_heavy_001.ogg` | impact-sounds |
| 4 出港 | `switch_004.ogg` | interface-sounds |
| 5-a UI 選択 | `select_001.ogg` | interface-sounds |
| 5-b UI 確定 | `confirmation_003.ogg` | interface-sounds |
| 6 警告 | `error_008.ogg` | interface-sounds |

選定の経緯と不採用の理由は [audio-candidates.md](audio-candidates.md)。

---

## 3. 効果音（freesound） — **Demo 2 では不採用**

`source_assets/audio/` に置いてあるが、**Demo 2 では使わない。**
採用素材を Kenney の CC0 のみに統一したため（[audio-candidates.md](audio-candidates.md) の「決定」）。

| ID | ファイル | 作者 | タイトル | 実測 | 扱い |
| --- | --- | --- | --- | --- | --- |
| [343738](https://freesound.org/s/343738/) | `343738__vospi__empty-corridor-of-a-spacecraft.wav` | vospi | empty corridor of a spacecraft | 21.9 秒 / 44100 Hz / 2 ch / ピーク -4.9 dB | 不採用 |
| [715475](https://freesound.org/s/715475/) | `715475__kvv_audio__ambtech_server-room-noise-01_kvv_free.wav` | kvv_audio | ambtech server-room-noise-01 (kvv free) | 102.5 秒 / 96000 Hz / 2 ch / ピーク -10.6 dB | 不採用 |

作者名とタイトルは**ファイル名から復元したもの**で、freesound 上の表示名は未確認。

> **将来この 2 本を採用する場合は、先にライセンスを確認すること。**
> freesound は CC0 / CC BY / CC BY-NC が混在する。使う前に各ページで確認し、
> CC BY 以上ならクレジット表記を追加するか、[02-demo2-plan.md](02-demo2-plan.md) §10-1 の
> 「CC0 のみ採用」方針どおり見送る。**未確認のまま `unity/Assets/` へ入れない。**

---

## 4. コックピットモデル（Demo 3 / Step 11-1a） — **取得済み**

| | |
| --- | --- |
| アセット名 | Hi-Rez Spaceships Creator Free Sample |
| 提供元 | Ebal Studios |
| Asset Store ID | 153363 |
| バージョン | 1.31（アセットが作られた Unity: 2022.3.62f3） |
| カテゴリ | 3D Models/Vehicles/Space |
| 入手日 | 2026-08-27 |
| ライセンス | **Standard Unity Asset Store EULA**。ゲームに組み込んでの配布は可、**アセット自体の再配布は不可** |
| ダウンロード物 | `Hi-Rez Spaceships Creator Free Sample.unitypackage`（189,205 KB / 178 エントリ） |
| 取り込み先 | `unity/Assets/ThirdParty/EbalStudios/HiRezSpaceshipsCreatorFree/`（**追跡除外**） |
| プレハブ GUID | `54e1b562c3fea284f8a0ec8cdc70057c`（内装付きコックピット。`CockpitDefinition.HiRezSample`） |

**アセット名・提供元・ID・バージョンは推測ではなく、`.unitypackage` の gzip FEXTRA に
埋まっている Asset Store のメタ情報から読み出した値。** 同じ値を
`unity/Assets/Editor/CockpitPackage.cs` が定数として持つ（記録はこの 2 か所）。

取り込んだ中身の一覧は `verify/hirez-package-contents.txt`（ファイル名のみ。
アセット本体は含まない）。

> **このリポジトリは PUBLIC。** EULA が再配布を禁じるので、
> `unity/Assets/ThirdParty/` 配下は 1 ファイルも追跡しない。
> `.gitignore` の 2 行（`/unity/Assets/ThirdParty/*` と `/unity/Assets/ThirdParty.meta`）と、
> EditMode テスト `ThirdPartyTrackingTests` がこれを縛っている。

### 再現手順（別マシンで復元するとき）

**1 と 2 は GUI が要る。** Asset Store の取得は Package Manager ウィンドウの
対話操作で `-batchmode` から叩く口が無い（CLAUDE.md §0-B）。
3 以降は CLI に閉じる。

1. Unity Editor を開き、**Window > Package Manager > My Assets** から対象アセットを
   Download する（購入済みであること）
2. ダウンロード先を確認する:
   `%APPDATA%\Unity\Asset Store-5.x\<publisher>\<category>\<name>.unitypackage`
   **UPM のレジストリパッケージと違い `Packages/manifest.json` には残らない。**
   だからこのファイルの記録が唯一の手掛かりになる
3. **取り込み先のフォルダを作る。**
   `unity/Assets/ThirdParty/` は `.gitignore` の内側なので、**clone 直後には存在しない。**
   取り込みスクリプトはフォルダの作成から始めること
4. `run_unity.ps1 -Method SolarSetup.ImportCockpit` で取り込む（`CockpitImporter`）。
   別の場所に置いたときは `-ExtraArgs '-package','<path>'` で渡す。
   **取り込みは素のままでは行わない。** `ImportPackageImmediately` は宛先の引数を
   持たず、パッケージが記録している `Assets/HiRezSpaceshipsCreatorFree/…` へ
   そのまま展開してしまう。そこは `.gitignore` の外なので、**取り込んだ瞬間に
   追跡対象になる。** `CockpitImporter` は各エントリの `pathname` を取り込み先へ
   書き換えた一時パッケージ（`%TEMP%`。リポジトリの外）を作り、
   **全エントリが取り込み先の下に来ていることを検査してから**取り込む
5. 不要物（デモシーン・Built-in 専用のパッケージ・例示プレハブ）を削除リストに
   従って消す。**テクスチャは消さない**（参照されないアセットはビルドに含まれない
   ので、消しても exe は軽くならない）
6. URP 変換と棚卸しは 4 の中で続けて走る（Step 11-1b / 11-1c）。やり直すときは
   `SolarSetup.ConvertCockpitToUrp` / `SolarSetup.InventoryCockpit`
7. `run_tests.ps1` で全緑を確認

**URP の公式一括変換 `Converters.RunInBatchMode` は使えない（URP 17.3.0 で実測）。**
`Converters.GetConvertersInContainer` が `TypeCache.GetTypesDerivedFrom<RenderPipelineConverter>()`
の結果を **abstract かどうかを見ずに** `Activator.CreateInstance` に渡すため、2D 側の抽象クラス
`Base2DMaterialUpgrader` で `MissingMethodException` になる（Converters.cs:263）。
代わりに**同じ変換表**（`MaterialUpgrader.FetchAllUpgradersForPipeline` +
`MaterialUpgrader.Upgrade`。どちらも public）を 1 枚ずつ当てている。
対応表を自作しないこと。**Unity の変換規則と二重管理になる。**

**変換が 0 件で終わっても「効いた」とは限らない。** Hi-Rez は取り込み時点で
すべて URP なので、変換の経路が生きているかは陽性対照
（`SolarSetup.VerifyUrpConversion` と同名の EditMode テスト）で確かめる。

**`.unitypackage` は gzip ヘッダに FEXTRA（Asset Store のメタ情報）が入っていて、
Python の `tarfile` では開けない**（`ReadError: invalid compressed data`）。
`gzip.GzipFile` 経由で読むか、`UnityPackageReader` のようにヘッダを自前で
読み飛ばして deflate 本体を渡す。各エントリのフォルダ名が GUID そのものなので、
**取り込む前にプレハブの GUID が読める。**

**アセットが無いままでも Editor・テスト・ビルドは通る。** 箱コックピットへ
フォールバックする（`CockpitCatalog.Resolve`）。判定はフォルダの有無ではなく
**プレハブ GUID が解決できるか**で行う。

---
## 5. 未確認事項

- [x] ~~freesound #343738 / #715475 のライセンス~~ → **不採用にしたので確認不要**（将来使うなら上の注記のとおり要確認）
- [x] ~~各用途に採用するファイルの確定~~ → [audio-candidates.md](audio-candidates.md) の「決定」で確定
- [ ] 計画では単発音は WAV 指定だが Kenney は全て OGG。変換するか指定を緩めるか
- [ ] `forceField_000.ogg` のループ加工版を採用するか（試聴待ち）

**採用素材は Kenney の 4 パック（CC0）のみ。** クレジット表記は任意だが記録は残す。
