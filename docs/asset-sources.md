# アセットの出典とライセンス

`source_assets/` に置いた素材の出どころ。**`source_assets/` 自体は `.gitignore` で
除外されるので、記録はこのファイル（追跡対象）に残す。**

> このファイルは**雛形**です。「要確認」の欄はまだ埋まっていません。
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

### 採用ファイル（試聴して決まったら埋める）

| 用途 | 採用 |
| --- | --- |
| 1 エンジン | **要確認** |
| 2 コックピット | **要確認** |
| 3 ドッキング | **要確認** |
| 4 出港 | **要確認** |
| 5 UI（選択 / 確定） | **要確認** |
| 6 警告 | **要確認** |

候補は [audio-candidates.md](audio-candidates.md)。

---

## 3. 効果音（freesound） — `source_assets/audio/`

> **ライセンスは「要確認」。** freesound は CC0 / CC BY / CC BY-NC が混在する。
> [02-demo2-plan.md](02-demo2-plan.md) §10-1 は「CC0 のみ採用」としているので、
> **確認するまで採用しない。** CC BY だった場合は §8 のリスク表どおり不採用、または表記を追加する。

| ID | ファイル | 作者 | タイトル | 実測 | ライセンス |
| --- | --- | --- | --- | --- | --- |
| [343738](https://freesound.org/s/343738/) | `343738__vospi__empty-corridor-of-a-spacecraft.wav` | vospi | empty corridor of a spacecraft | 21.9 秒 / 44100 Hz / 2 ch / ピーク -4.9 dB | **要確認** |
| [715475](https://freesound.org/s/715475/) | `715475__kvv_audio__ambtech_server-room-noise-01_kvv_free.wav` | kvv_audio | ambtech server-room-noise-01 (kvv free) | 102.5 秒 / 96000 Hz / 2 ch / ピーク -10.6 dB | **要確認** |

作者名とタイトルは**ファイル名から復元したもの**で、freesound 上の表示名は未確認。
どちらもコックピットの環境音（用途 2）の候補。

---

## 4. 未確認事項

- [ ] freesound #343738 のライセンス（CC0 か否か）と作者の表示名
- [ ] freesound #715475 のライセンス（CC0 か否か）と作者の表示名
- [ ] 各用途に採用するファイルの確定（試聴が必要）
- [ ] 計画では単発音は WAV 指定だが Kenney は全て OGG。変換するか指定を緩めるか
- [ ] Kenney の持続音は全て 5.000 秒。ループ継ぎ目が使えるかは未検証
