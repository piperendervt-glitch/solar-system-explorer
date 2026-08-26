# 02 — アセット記録

外部から持ってきた素材の出所とライセンス。**記録のないアセットは使わない。**

原本は `source_assets/` に置く。ここは `.gitignore` で除外してあり、
リポジトリには `unity/Assets/` へ取り込んだ複製だけが入る。
別の環境で clone したときは、下表の入手元から同じファイル名で
`source_assets/` へ置き直してから `SolarSetup.ImportTextures` を回す。

---

## 使用中のアセット

| ファイル名 | 用途 | 解像度 | 入手元 | ライセンス |
| --- | --- | --- | --- | --- |
| `starmap_2020_4k.exr` | 星空スカイボックス | 4096 x 2048（Cubemap 化後は 2048 px/面） | NASA SVS *Deep Star Maps 2020* (celestial) <br> https://svs.gsfc.nasa.gov/4851 | NASA の画像はパブリックドメイン扱い（下記「NASA について」） |
| `8k_earth_daymap.jpg` | 地球のアルベド | 8192 x 4096 | Solar System Scope <br> https://www.solarsystemscope.com/textures/ | **CC BY 4.0（クレジット表記が必須）** |
| `8k_mars.jpg` | 火星のアルベド | 8192 x 4096 | Solar System Scope <br> https://www.solarsystemscope.com/textures/ | **CC BY 4.0（クレジット表記が必須）** |
| `8k_sun.jpg` | 太陽の表面 | 4096 x 2048 （ファイル名は 8k だが実測はこれ） | Solar System Scope <br> https://www.solarsystemscope.com/textures/ | **CC BY 4.0（クレジット表記が必須）** |

地球の雲・夜景・法線マップは使っていない。

### スカイボックスを描く段

4 段のカメラのうち、**スカイボックスを描くのは Deep 段（Base カメラ）だけ**。
Near / Nearfield / Cockpit はすべて Overlay で `clearFlags = Depth`
（深度だけクリアして色は引き継ぐ）ため、星空は Deep 段が一度だけ描く。
`CameraStackController.Configure()` が Deep の `clearFlags` を
`CameraClearFlags.Skybox` に固定している。ここを SolidColor にすると星空が消える。

### 露出とトーンマッピング

星図は**リニア HDR の EXR** で、素の値は暗い。
スカイボックスマテリアルの `_Exposure` は **1.0**、
そのうえで ACES トーンマッピング（ポストプロセス）を通して見せている。
`_Exposure` を上げて明るさを稼ぐと星ではなく背景ごと持ち上がり、
空が一様な灰色になる。明るさはトーンマッピング側で調整すること。

---

## クレジット表記（必須）

Solar System Scope のテクスチャは **CC BY 4.0** なので、
デモを人に見せる形にするときは以下の表記を画面またはドキュメントに入れること。

```
Planet textures by Solar System Scope (https://www.solarsystemscope.com/textures/)
Licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)

Star map: NASA/Goddard Space Flight Center Scientific Visualization Studio
"Deep Star Maps 2020" (https://svs.gsfc.nasa.gov/4851)
```

**まだ画面には出していない。** 表示は Step 7 以降、または公開を考える段になってから。
要件 §1 は「個人で楽しむデモ。本格的な公開・販売は現時点では考えていない」なので、
現時点ではこの記録が表記の代わりになっている。

---

## NASA について

NASA の画像・映像は一般にパブリックドメイン扱いだが、
**第三者の著作物が含まれる場合がある。** Deep Star Maps 2020 は
Gaia (ESA) と 2MASS のデータを元にしているので、
公開する形にするときは SVS のページの注記を読み直すこと。

---

## 取り込みの手順

```powershell
# source_assets/ から unity/Assets/Textures/ へ複製し、インポート設定を当てる
.\tools\run_unity.ps1 -Method SolarSetup.ImportTextures

# その後にシーンを生成する (マテリアルがテクスチャを参照するため別 Run)
.\tools\run_unity.ps1 -Method SolarSetup.Run
```

`source_assets/` にファイルが無ければ `ImportTextures` は
足りないファイル名を並べて止まる（`TmpSetup` と同じ流儀）。
