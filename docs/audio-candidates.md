# Demo 2 音素材の選定（docs/02-demo2-plan.md §10-1 の 6 用途）

数値は `source_assets/AUDIO_INVENTORY.txt` と同じ実測値（ピーク・平均は dBFS）。
この inventory は ffprobe から再生成できるので `source_assets/`（gitignore 済み）に置いたままです。

---

## 決定

試聴のうえ確定。**採用素材は全て Kenney の CC0** で統一した。

| 用途 | 採用ファイル | パック | 長さ[s] | ch | ピーク | 平均 |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| 1 エンジン | `spaceEngineLow_003.ogg` | sci-fi-sounds | 5.000 | 1 | -1.0 dB | -7.4 dB |
| 2 コックピット | `forceField_000.ogg` ※ | sci-fi-sounds | 0.954 | 1 | -0.9 dB | -11.5 dB |
| 3 ドッキング | `impactPlate_heavy_001.ogg` | impact-sounds | 0.352 | 2 | -0.9 dB | -17.4 dB |
| 4 出港 | `switch_004.ogg` | interface-sounds | 0.500 | 2 | -1.0 dB | -23.9 dB |
| 5-a UI 選択 | `select_001.ogg` | interface-sounds | 0.043 | 2 | -1.1 dB | -17.3 dB |
| 5-b UI 確定 | `confirmation_003.ogg` | interface-sounds | 0.322 | 1 | -1.0 dB | -15.2 dB |
| 6 警告 | `error_008.ogg` | interface-sounds | 0.139 | 1 | -1.0 dB | -20.6 dB |

※ **`forceField_000.ogg` はループ加工が前提。** 素のままでは 0.954 秒周期で
反復に気付く。加工手順は下の「コックピット音のループ加工」を参照。

`confirmation_003.ogg` は比較ファイル `compare_ui-confirm.wav` に入れていなかった
（001 / 002 / 004 / switch_002 / toggle_001 の 5 本だった）。実在を確認したうえで採用。
confirmation 系 4 種の中で平均音量が最も低い。

### 不採用の理由

| 対象 | 理由 |
| --- | --- |
| `computerNoise_*`（コックピット候補 4 種） | 電子音が目立ちすぎて背景に回らない |
| `error_002`（警告候補） | ピーク **-0.0 dB** でフルスケールに張り付いている |
| freesound `715475` / `343738` | 不採用。採用素材を Kenney の CC0 に統一した（[asset-sources.md](asset-sources.md) 参照） |

---

## 候補一覧（選定の経緯）

以下は選定時に並べた候補で、記録として残す。★ が採用。
ここに挙げていない候補も各パックに多数ある（error 8 種、select 8 種など）。

---

## 1. エンジン

要件: 低い持続ハム（ループ、5〜10 秒、継ぎ目なし） / OGG

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `spaceEngineLow_000.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.6 dB |
| ★ `spaceEngineLow_003.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.4 dB |
| `spaceEngineLarge_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -0.9 dB | -6.1 dB |
| `spaceEngine_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 2 | -1.0 dB | -8.4 dB |
| `engineCircular_002.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -12.2 dB |

  いずれも 5.000 秒ちょうど・44.1 kHz。Low 系が最も低域寄り（平均 -7 dB 台）、
  engineCircular は回転感が強く平均 -12 dB と控えめ。spaceEngine_001 のみ 2ch。
  ※ ループ継ぎ目は未検証。試聴時に先頭と末尾のつながりを確認してください。

## 2. コックピット

要件: 微かな電子ハム・空調（ループ） / OGG

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `computerNoise_000.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -7.7 dB |
| `computerNoise_001.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -8.9 dB |
| `computerNoise_002.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -0.9 dB | -11.5 dB |
| `computerNoise_003.ogg` | sci-fi-sounds | 5.000 | 44100 | 1 | -1.0 dB | -11.3 dB |
| ★ `forceField_000.ogg` | sci-fi-sounds | 0.954 | 44100 | 1 | -0.9 dB | -11.5 dB |

  computerNoise 4 種が用途に最も近い。forceField は持続音だが音程感が強い。
  ※ 別枠: freesound の 2 ファイルはまさにこの用途向けです（下の「参考」を参照）。

## 3. ドッキング

要件: 金属の接合音（単発 1〜2 秒） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `impactMetal_heavy_000.ogg` | impact-sounds | 0.168 | 44100 | 2 | -0.9 dB | -20.4 dB |
| `impactMetal_heavy_003.ogg` | impact-sounds | 0.207 | 44100 | 2 | -1.0 dB | -21.2 dB |
| ★ `impactPlate_heavy_001.ogg` | impact-sounds | 0.352 | 44100 | 2 | -0.9 dB | -17.4 dB |
| `impactMetal_004.ogg` | sci-fi-sounds | 0.390 | 44100 | 1 | -0.9 dB | -21.2 dB |
| `impactTin_medium_002.ogg` | impact-sounds | 0.134 | 44100 | 2 | -1.1 dB | -21.5 dB |

  impactMetal_004 のみ sci-fi パック、他は impact パック。
  ※ 計画では WAV 指定ですが Kenney は全て OGG です。変換するか、指定を OGG に緩めるか要判断。

## 4. 出港

要件: クランプ解除音（単発） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `doorOpen_000.ogg` | sci-fi-sounds | 0.532 | 44100 | 1 | -0.9 dB | -17.6 dB |
| `doorOpen_002.ogg` | sci-fi-sounds | 0.533 | 44100 | 1 | -1.0 dB | -16.0 dB |
| ★ `switch_004.ogg` | interface-sounds | 0.500 | 44100 | 2 | -1.0 dB | -23.9 dB |
| `impactMetal_light_002.ogg` | impact-sounds | 0.236 | 44100 | 2 | -1.4 dB | -20.6 dB |
| `forceField_003.ogg` | sci-fi-sounds | 0.956 | 44100 | 1 | -0.9 dB | -11.6 dB |

  doorOpen は機構が外れる感じ、switch はより短く乾いた音。
  ※ ドッキング音と対になるので、3 と組で試聴するのが良いはずです。

## 5-a. UI（選択）

要件: 短い電子音・選択 / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| ★ `select_001.ogg` | interface-sounds | 0.043 | 44100 | 2 | -1.1 dB | -17.3 dB |
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

  **この 5 本からは選ばれなかった。** 採用は候補に入れていなかった
  `confirmation_003.ogg`（0.322 秒 / ピーク -1.0 dB / 平均 -15.2 dB）。

## 6. 警告

要件: 「要求NG」用の否定音（単発） / WAV

| 候補 | パック | 長さ[s] | Hz | ch | ピーク | 平均 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `error_002.ogg` | interface-sounds | 0.165 | 44100 | 2 | -0.0 dB | -17.6 dB |
| `error_005.ogg` | interface-sounds | 0.500 | 44100 | 1 | -1.0 dB | -18.7 dB |
| ★ `error_008.ogg` | interface-sounds | 0.139 | 44100 | 1 | -1.0 dB | -20.6 dB |
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

715475 は 96 kHz / 102.5 秒と重く、採用するならリサンプルと切り出しが要る。

**どちらも Demo 2 では不採用。** ライセンス未確認だったことと、
採用素材を Kenney の CC0 に統一したため。将来使うならライセンス確認が要る
（[asset-sources.md](asset-sources.md) の注記を参照）。

---

## コックピット音のループ加工

`forceField_000.ogg` は 0.954 秒しかなく、そのままループさせると約 1 秒周期で反復に気付く。
周期を目立たなくするため、3 層を重ねて 8 秒のループに加工する。

生成スクリプト: `source_assets/audio/preview/make_forcefield_loop.sh`
（`source_assets/` は gitignore 済みなので、**手順はこのファイルにも残す**）

```bash
SRC=kenney/sci-fi-sounds/Audio/forceField_000.ogg
SR=44100
PITCHES=(0.97 1.00 1.03)     # 層のピッチ比
OFFSETS=(0    0.31 0.62)     # 層の開始位置 [秒]
LEN=8.0                      # ループ長 [秒]
XF=0.2                       # クロスフェード長 [秒]
REPEAT=30                    # 素材を何回ぶん流し込むか

# 1. ピッチ違いの 3 層を作り、開始位置をずらして重ねる
#    asetrate は再生速度ごと変える (Unity の AudioSource.pitch と同じ挙動)。
#    amix の normalize=1 は入力数で割るので、層ごとの音量比は等倍のままクリップしない。
# 2. LEN+XF 秒ぶん切り出す
# 3. 末尾 XF 秒を先頭 XF 秒へクロスフェードし、LEN 秒のループ端を繋ぐ
ffmpeg -stream_loop $REPEAT -i $SRC -stream_loop $REPEAT -i $SRC -stream_loop $REPEAT -i $SRC \
  -filter_complex "\
    [0:a]asetrate=${SR}*0.97,aresample=$SR,atrim=start=0,asetpts=N/SR/TB[l0]; \
    [1:a]asetrate=${SR}*1.00,aresample=$SR,atrim=start=0.31,asetpts=N/SR/TB[l1]; \
    [2:a]asetrate=${SR}*1.03,aresample=$SR,atrim=start=0.62,asetpts=N/SR/TB[l2]; \
    [l0][l1][l2]amix=inputs=3:duration=shortest:normalize=1, \
      atrim=0:8.2,asetpts=N/SR/TB[a]; \
    [a]asplit=3[h][b][t]; \
    [h]atrim=0:0.2,asetpts=N/SR/TB,afade=t=in:st=0:d=0.2[hf]; \
    [b]atrim=0.2:8.0,asetpts=N/SR/TB[bb]; \
    [t]atrim=8.0:8.2,asetpts=N/SR/TB,afade=t=out:st=0:d=0.2[tf]; \
    [hf][tf]amix=inputs=2:normalize=0[xf]; \
    [xf][bb]concat=n=2:v=0:a=1[loop]" \
  -map "[loop]" -c:a pcm_s16le preview/forceField_000_loop8s.wav
```

層の数・ピッチ比・クロスフェード長はスクリプト先頭の変数で変えられる。

### 加工前後の実測

| | 素のまま 3 連結 | 加工後 8 秒 × 3 連結 |
| --- | ---: | ---: |
| 長さ | 2.862 秒 | 24.000 秒 |
| 反復の周期 | 0.954 秒 | 8.000 秒 |
| ピーク | -0.9 dB | -4.6 dB |
| 隣接サンプル差の平均 | 129.7 | 114.1 |
| 連結点での段差 | 7.0（平均の 0.05 倍） | 13.0（平均の 0.11 倍） |

> **補足: 素材にもともとプチノイズは無い。** 素のままでも連結点の段差は
> 隣接サンプル差の平均の 0.05 倍しかなく、波形は不連続でない。
> 耳につくのは段差ではなく **0.954 秒ごとに同じ音型が戻ってくること**なので、
> この加工は「クリックを消す」のではなく **周期を 0.954 秒から 8 秒へ伸ばす**もの。

試聴用: `source_assets/audio/preview/forceField_000_loop8s_x3.wav`（8 秒 × 3 連結）
