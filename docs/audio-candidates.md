# Demo 2 音素材の候補（docs/02-demo2-plan.md §10-1 の 6 用途）

Kenney の展開後ファイル名から、用途ごとに **3〜5 個** に絞ったものです。
数値は `source_assets/AUDIO_INVENTORY.txt` と同じ実測値（ピーク・平均は dBFS）。
この inventory は ffprobe から再生成できるので `source_assets/`（gitignore 済み）に置いたままです。

> **最終決定はしていません。** 試聴して選んでください。
> ここに挙げていない候補も各パックに多数あります（error 8 種、select 8 種など）。

---

## 1. エンジン

要件: 低い持続ハム（ループ、5〜10 秒、継ぎ目なし） / OGG

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `spaceEngineLow_000.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.6 dB |
| `spaceEngineLow_003.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.4 dB |
| `spaceEngineLarge_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -0.9 dB | -6.1 dB |
| `spaceEngine_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 2 | -1.0 dB | -8.4 dB |
| `engineCircular_002.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -12.2 dB |

  いずれも 5.000 秒ちょうど・44.1 kHz。Low 系が最も低域寄り（平均 -7 dB 台）、
  engineCircular は回転感が強く平均 -12 dB と控えめ。spaceEngine_001 のみ 2ch。
  ★ ループ継ぎ目は未検証。試聴時に先頭と末尾のつながりを確認してください。

## 2. コックピット

要件: 微かな電子ハム・空調（ループ） / OGG

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `computerNoise_000.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.7 dB |
| `computerNoise_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -8.9 dB |
| `computerNoise_002.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -0.9 dB | -11.5 dB |
| `computerNoise_003.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -11.3 dB |
| `forceField_000.ogg` | sci-fi-sounds | 0.954 | 44100 | 1 | -0.9 dB | -11.5 dB |

  computerNoise 4 種が用途に最も近い。forceField は持続音だが音程感が強い。
  ★ 別枠: freesound の 2 ファイルはまさにこの用途向けです（下の「参考」を参照）。

## 3. ドッキング

要件: 金属の接合音（単発 1〜2 秒） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `impactMetal_heavy_000.ogg` | impact-sounds | 0.168 | 44100 | 2 | -0.9 dB | -20.4 dB |
| `impactMetal_heavy_003.ogg` | impact-sounds | 0.207 | 44100 | 2 | -1.0 dB | -21.2 dB |
| `impactPlate_heavy_001.ogg` | impact-sounds | 0.352 | 44100 | 2 | -0.9 dB | -17.4 dB |
| `impactMetal_004.ogg` | sci-fi-sounds | 0.390 | 44100 | 1 | -0.9 dB | -21.2 dB |
| `impactTin_medium_002.ogg` | impact-sounds | 0.134 | 44100 | 2 | -1.1 dB | -21.5 dB |

  impactMetal_004 のみ sci-fi パック、他は impact パック。
  ★ 計画では WAV 指定ですが Kenney は全て OGG です。変換するか、指定を OGG に緩めるか要判断。

## 4. 出港

要件: クランプ解除音（単発） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `doorOpen_000.ogg` | sci-fi-sounds | 0.532 | 44100 | 1 | -0.9 dB | -17.6 dB |
| `doorOpen_002.ogg` | sci-fi-sounds | 0.533 | 44100 | 1 | -1.0 dB | -16.0 dB |
| `switch_004.ogg` | interface-sounds | 0.500 | 44100 | 2 | -1.0 dB | -23.9 dB |
| `impactMetal_light_002.ogg` | impact-sounds | 0.236 | 44100 | 2 | -1.4 dB | -20.6 dB |
| `forceField_003.ogg` | sci-fi-sounds | 0.956 | 44100 | 1 | -0.9 dB | -11.6 dB |

  doorOpen は機構が外れる感じ、switch はより短く乾いた音。
  ★ ドッキング音と対になるので、3 と組で試聴するのが良いはずです。

## 5-a. UI（選択）

要件: 短い電子音・選択 / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `select_001.ogg` | interface-sounds | 0.043 | 44100 | 2 | -1.1 dB | -17.3 dB |
| `select_004.ogg` | interface-sounds | 0.383 | 44100 | 1 | -0.7 dB | -19.2 dB |
| `select_007.ogg` | interface-sounds | 0.047 | 44100 | 1 | -1.0 dB | -14.7 dB |
| `click_002.ogg` | interface-sounds | 0.010 | 44100 | 1 | -0.9 dB | -16.2 dB |
| `tick_002.ogg` | interface-sounds | 0.023 | 44100 | 1 | -1.2 dB | -16.6 dB |

  select 系は 8 種、click 系は 5 種あります。ここでは代表を抜き出しています。

## 5-b. UI（確定）

要件: 短い電子音・確定 / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `confirmation_001.ogg` | interface-sounds | 0.290 | 44100 | 1 | -0.9 dB | -11.3 dB |
| `confirmation_002.ogg` | interface-sounds | 0.539 | 44100 | 1 | -1.0 dB | -14.9 dB |
| `confirmation_004.ogg` | interface-sounds | 0.490 | 44100 | 1 | -0.9 dB | -11.8 dB |
| `switch_002.ogg` | interface-sounds | 0.611 | 44100 | 2 | -0.9 dB | -25.0 dB |
| `toggle_001.ogg` | interface-sounds | 0.139 | 44100 | 1 | -0.9 dB | -14.0 dB |

  confirmation 系 4 種が用途どおり。switch / toggle はより素っ気ない。

## 6. 警告

要件: 「要求NG」用の否定音（単発） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `error_002.ogg` | interface-sounds | 0.165 | 44100 | 2 | -0.0 dB | -17.6 dB |
| `error_005.ogg` | interface-sounds | 0.500 | 44100 | 1 | -1.0 dB | -18.7 dB |
| `error_008.ogg` | interface-sounds | 0.139 | 44100 | 1 | -1.0 dB | -20.6 dB |
| `question_003.ogg` | interface-sounds | 0.332 | 44100 | 1 | -0.9 dB | -10.0 dB |
| `glitch_002.ogg` | interface-sounds | 0.030 | 44100 | 2 | -1.2 dB | -13.7 dB |

  error 系は 8 種あります。glitch はノイズ寄りで、機械的な拒否に近い。

---

## 参考: freesound の 2 ファイル（Kenney 外）

用途 2（コックピットの微かなハム・空調）に直接使えそうなのはこの 2 本です。
ライセンスが未確認なので [asset-sources.md](asset-sources.md) では「要確認」のままにしてあります。

| ファイル | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `343738__vospi__empty-corridor-of-a-spacecraft.wav` | 21.898 | 44100 | 2 | -4.9 dB | -18.1 dB |
| `715475__kvv_audio__ambtech_server-room-noise-01_kvv_free.wav` | 102.500 | 96000 | 2 | -10.6 dB | -25.7 dB |

715475 は 96 kHz / 102.5 秒と重いので、採用するならリサンプルと切り出しが要ります。
