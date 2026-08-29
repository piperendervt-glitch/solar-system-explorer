using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **ステーションのプレハブを 6 方向から正投影で撮る (Step 13-3 コミット2)。**
    ///
    /// ■ なぜ正投影か
    /// 透視投影だと奥行きで大きさが歪み、**画像から開口の実寸が読めない。**
    /// 正投影なら「1 px が何メートルか」が画面全体で一定なので、目盛を入れれば
    /// 画像だけで寸法が読める。
    ///
    /// ■ 目盛と軸のラベルは画像に焼く
    /// OnGUI は `Camera.Render()` → RenderTexture の経路に写らない（§0-B）ので、
    /// `TinyFont` で CPU 側から画素に書く。
    ///
    /// **口の特定はしない。絵と一覧を出すだけ。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.CaptureStationOrtho
    /// </summary>
    public static class StationOrthoCapture
    {
        const string PrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        const int Size = 1024;

        /// <summary>bbox に対する余白（1.0 = ぴったり）。</summary>
        const float Margin = 1.12f;

        /// <summary>発光を持つマテリアル（13-2 の実測）。</summary>
        static readonly string[] EmissiveMaterials = { "module6", "module7", "module10", "module11" };

        static readonly Color32 Ink = new Color32(255, 255, 255, 255);
        static readonly Color32 Tick = new Color32(160, 200, 255, 255);
        static readonly Color32 BoxLine = new Color32(255, 120, 60, 255);
        static readonly Color32 Name = new Color32(255, 220, 90, 255);
        static readonly Color BackgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);

        sealed class View
        {
            public string Name;
            public Vector3 Direction;   // モデル -> カメラ
            public Vector3 UpHint;
        }

        static readonly View[] Views =
        {
            new View { Name = "posX", Direction = Vector3.right,   UpHint = Vector3.up },
            new View { Name = "negX", Direction = Vector3.left,    UpHint = Vector3.up },
            new View { Name = "posY", Direction = Vector3.up,      UpHint = Vector3.forward },
            new View { Name = "negY", Direction = Vector3.down,    UpHint = Vector3.forward },
            new View { Name = "posZ", Direction = Vector3.forward, UpHint = Vector3.up },
            new View { Name = "negZ", Direction = Vector3.back,    UpHint = Vector3.up },
        };

        public static void Run()
        {
            string path = AssetDatabase.GUIDToAssetPath(PrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[StationOrtho] 取り込まれていない（GUID が解決できない）: " + PrefabGuid);
                return;
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null)
            {
                Debug.LogWarning("[StationOrtho] プレハブとして読めない: " + path);
                return;
            }

            string outDir = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "docs", "screenshots", "demo4"));
            Directory.CreateDirectory(outDir);

            // **空のシーンで撮る。** Main.unity には触らない（保存もしない）。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = WorldBounds(renderers);

            var sb = new StringBuilder();
            WriteHeader(sb, path, bounds, renderers.Length);
            WriteRendererTable(sb, instance, renderers);

            GameObject camGo = CreateCamera(out Camera camera);

            foreach (View v in Views)
            {
                Color32[] pixels = Render(camera, bounds, v, out float orthoSize);
                Annotate(pixels, bounds, v, orthoSize, "STATION " + Label(v), null);
                Save(pixels, Path.Combine(outDir, "13-3_ortho_" + v.Name + ".png"));

                // **名前入りの版 (13-3a)。** 絵とレンダラー一覧が一発で対応する。
                Color32[] named = Render(camera, bounds, v, out float _);
                Annotate(named, bounds, v, orthoSize, "NAMED " + Label(v), null);
                DrawRendererNames(named, renderers, bounds.center, orthoSize, v);
                Save(named, Path.Combine(outDir, "13-3_ortho_" + v.Name + "_named.png"));

                sb.AppendLine("  " + v.Name + " : orthographicSize " +
                              orthoSize.ToString("F3", CultureInfo.InvariantCulture) +
                              " m / 1 px = " +
                              (2.0 * orthoSize / Size).ToString("F5", CultureInfo.InvariantCulture) + " m");
            }

            sb.AppendLine();
            sb.AppendLine("  **目盛はプレハブ座標 (13-3a の訂正)。**");
            sb.AppendLine("  以前は画像中心を 0 として振っていたので、上の一覧の座標と");
            sb.AppendLine("  Z で 6.5522 ずれていた（bbox 中心 -6.5522 が原点ではない）。");
            sb.AppendLine();

            CaptureHatch(camera, renderers, bounds, outDir, sb);

            CaptureEmissiveSheet(camera, instance, renderers, bounds, outDir, sb);

            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(instance);

            string report = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "verify", "station-renderers.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log("[StationOrtho] 画像: " + outDir);
            Debug.Log("[StationOrtho] 一覧: " + report);
        }

        // ---- 場面 ----

        static void SetupLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.30f, 0.34f, 1f);
            RenderSettings.skybox = null;

            AddLight("Key", new Vector3(35f, -35f, 0f), 1.6f);
            AddLight("Fill", new Vector3(-20f, 145f, 0f), 0.7f);
        }

        static void AddLight(string name, Vector3 euler, float intensity)
        {
            var go = new GameObject(name);
            go.transform.rotation = Quaternion.Euler(euler);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        static GameObject CreateCamera(out Camera camera)
        {
            var go = new GameObject("OrthoCamera");
            camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.cullingMask = ~0;
            camera.allowMSAA = false;
            camera.allowHDR = false;
            return go;
        }

        static Bounds WorldBounds(IEnumerable<Renderer> renderers)
        {
            bool first = true;
            var bounds = new Bounds();

            foreach (Renderer r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 p = r.transform.TransformPoint(v);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            return bounds;
        }

        // ---- 撮影 ----

        /// <summary>bbox 全体が入る `orthographicSize`。</summary>
        static float FrameSize(Bounds bounds, View v)
        {
            Quaternion rotation = Quaternion.LookRotation(-v.Direction, v.UpHint);
            float halfW = HalfExtentAlong(bounds, rotation * Vector3.right);
            float halfH = HalfExtentAlong(bounds, rotation * Vector3.up);

            // 画像は正方形なので aspect 1。縦横の大きいほうに合わせる。
            return Mathf.Max(halfH, halfW) * Margin;
        }

        static Color32[] Render(Camera camera, Bounds bounds, View v, out float orthoSize)
        {
            orthoSize = FrameSize(bounds, v);
            return Render(camera, bounds.center, orthoSize, v, bounds.extents.magnitude);
        }

        /// <summary>
        /// **画像の中心に来るワールド点を明示して撮る。**
        /// 近接図（ハッチ）では bbox 中心ではなく、見たい部品の中心を渡す。
        /// </summary>
        static Color32[] Render(Camera camera, Vector3 frameCenter, float orthoSize,
                                View v, float sceneRadius)
        {
            Quaternion rotation = Quaternion.LookRotation(-v.Direction, v.UpHint);

            float distance = sceneRadius * 3f + 10f;
            camera.transform.position = frameCenter + v.Direction * distance;
            camera.transform.rotation = rotation;
            camera.orthographic = true;
            camera.orthographicSize = orthoSize;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = distance * 2f + sceneRadius * 2f;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);

            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
            texture.Apply(false, false);
            RenderTexture.active = previous;

            camera.targetTexture = null;

            Color32[] pixels = texture.GetPixels32();

            UnityEngine.Object.DestroyImmediate(texture);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return pixels;
        }

        static float HalfExtentAlong(Bounds b, Vector3 axis)
            => Mathf.Abs(b.extents.x * axis.x)
               + Mathf.Abs(b.extents.y * axis.y)
               + Mathf.Abs(b.extents.z * axis.z);

        static void Save(Color32[] pixels, string path) => Save(pixels, Size, Size, path);

        static void Save(Color32[] pixels, int width, int height, string path)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // ---- レンダラー名を投影位置に焼く ----

        static void DrawRendererNames(Color32[] pixels, Renderer[] renderers,
                                      Vector3 frameCenter, float orthoSize, View v)
        {
            float pixelsPerMeter = Size / (2f * orthoSize);
            Quaternion rotation = Quaternion.LookRotation(-v.Direction, v.UpHint);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            foreach (Renderer r in renderers.OrderBy(r => r.name, StringComparer.Ordinal))
            {
                Vector3 d = r.bounds.center - frameCenter;
                int x = Size / 2 + Mathf.RoundToInt(Vector3.Dot(d, right) * pixelsPerMeter);
                int y = Size / 2 + Mathf.RoundToInt(Vector3.Dot(d, up) * pixelsPerMeter);

                // 中心の印（十字）。名前だけだとどこを指しているか分からない。
                HLine(pixels, x - 5, y, 11, Name);
                VLine(pixels, x, y - 5, 11, Name);

                string label = r.name.ToUpperInvariant();
                TinyFont.Draw(pixels, Size, Size,
                              x - TinyFont.MeasureWidth(label, 2) / 2, y + 8, label, 2, Name);
            }
        }

        // ---- ハッチ（+Z 端）の近接図 ----

        /// <summary>
        /// **+Z 端の近接図 (13-3a)。** ドッキングポートは +Z と決まったので、
        /// 開口を目で読めるだけの倍率で撮る。**口の寸法は指名しない。**
        /// </summary>
        static void CaptureHatch(Camera camera, Renderer[] renderers, Bounds bounds,
                                 string outDir, StringBuilder sb)
        {
            Renderer front = renderers
                .OrderByDescending(r => r.bounds.max.z)
                .FirstOrDefault();

            if (front == null)
            {
                sb.AppendLine("-- ハッチの近接図: レンダラーが無い");
                sb.AppendLine();
                return;
            }

            Bounds b = front.bounds;
            float half = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.6f;

            sb.AppendLine("-- ハッチの近接図 (+Z 端) --");
            sb.AppendLine("  最も +Z 側のレンダラー : " + front.name
                          + " / bounds.max.z = "
                          + b.max.z.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  近接図の中心 : " + F(b.center)
                          + " / orthographicSize " + half.ToString("F3", CultureInfo.InvariantCulture)
                          + " m / 1 px = "
                          + (2.0 * half / Size).ToString("F5", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine();

            foreach (View v in Views)
            {
                if (v.Name != "posZ" && v.Name != "posY" && v.Name != "posX")
                {
                    continue;
                }

                Color32[] pixels = Render(camera, b.center, half, v, bounds.extents.magnitude);
                Annotate(pixels, bounds, b.center, v, half, "HATCH " + Label(v), null);
                Save(pixels, Path.Combine(outDir, "13-3_hatch_" + v.Name + ".png"));
            }
        }

        // ---- 発光の 7 枚目（6 面を 3x2 に敷き詰めた 1 枚）----

        static void CaptureEmissiveSheet(Camera camera, GameObject instance, Renderer[] all,
                                         Bounds bounds, string outDir, StringBuilder sb)
        {
            Renderer[] emissive = all.Where(IsEmissive).ToArray();

            sb.AppendLine("-- 発光マテリアルを持つレンダラー（13-4 の窓・航法灯の制約）--");
            if (emissive.Length == 0)
            {
                sb.AppendLine("  **0 件。** 発光マテリアルが付いたレンダラーが見つからない");
                sb.AppendLine();
                return;
            }

            foreach (Renderer r in emissive)
            {
                sb.AppendLine("  " + PathOf(instance, r.transform) + " / マテリアル "
                              + string.Join(", ", r.sharedMaterials
                                  .Where(x => x != null).Select(x => x.name).ToArray())
                              // **本表と同じ量を出す**（`Renderer.bounds.center`）。
                              // `transform.position` を混ぜると同じ物が 2 通りの数字になる。
                              + " / 中心 " + F(r.bounds.center)
                              + " / サイズ " + F(MeshWorldSize(r)));
            }

            sb.AppendLine();

            // 発光を持たないレンダラーを一時的に消して撮る。**マテリアルは作らない。**
            var hidden = new List<Renderer>();
            foreach (Renderer r in all)
            {
                if (!IsEmissive(r) && r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
            }

            // **2 列 3 行。** セルは 512 px なので用紙は 1024 x 1536。
            // （正方形に詰めると行間 341 px にセル 512 px を置くことになり、重なる）
            const int cell = Size / 2;
            const int sheetWidth = cell * 2;
            const int sheetHeight = cell * 3;

            var sheet = new Color32[sheetWidth * sheetHeight];
            for (int i = 0; i < sheet.Length; i++)
            {
                sheet[i] = new Color32(12, 14, 18, 255);
            }

            for (int i = 0; i < Views.Length; i++)
            {
                View v = Views[i];
                Color32[] pixels = Render(camera, bounds, v, out float orthoSize);
                Annotate(pixels, bounds, v, orthoSize, "EMISSIVE " + Label(v), BoxLine);

                Color32[] small = Downsample(pixels, Size, cell);

                int col = i % 2;
                int row = i / 2;
                int ox = col * cell;
                int oy = sheetHeight - (row + 1) * cell;

                Blit(sheet, sheetWidth, sheetHeight, small, cell, ox, oy);
            }

            foreach (Renderer r in hidden)
            {
                r.enabled = true;
            }

            Save(sheet, sheetWidth, sheetHeight, Path.Combine(outDir, "13-3_ortho_emissive.png"));
        }

        static bool IsEmissive(Renderer r)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && EmissiveMaterials.Contains(m.name))
                {
                    return true;
                }
            }

            return false;
        }

        static Vector3 MeshWorldSize(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return Vector3.zero;
            }

            Vector3 raw = filter.sharedMesh.bounds.size;
            Vector3 s = r.transform.lossyScale;
            return new Vector3(raw.x * s.x, raw.y * s.y, raw.z * s.z);
        }

        static Color32[] Downsample(Color32[] src, int srcSize, int dstSize)
        {
            var dst = new Color32[dstSize * dstSize];
            int factor = srcSize / dstSize;

            for (int y = 0; y < dstSize; y++)
            {
                for (int x = 0; x < dstSize; x++)
                {
                    int rr = 0, gg = 0, bb = 0;
                    for (int sy = 0; sy < factor; sy++)
                    {
                        for (int sx = 0; sx < factor; sx++)
                        {
                            Color32 c = src[(y * factor + sy) * srcSize + x * factor + sx];
                            rr += c.r;
                            gg += c.g;
                            bb += c.b;
                        }
                    }

                    int n = factor * factor;
                    dst[y * dstSize + x] = new Color32(
                        (byte)(rr / n), (byte)(gg / n), (byte)(bb / n), 255);
                }
            }

            return dst;
        }

        static void Blit(Color32[] dst, int dstWidth, int dstHeight,
                         Color32[] src, int srcSize, int ox, int oy)
        {
            for (int y = 0; y < srcSize; y++)
            {
                int dy = oy + y;
                if (dy < 0 || dy >= dstHeight)
                {
                    continue;
                }

                for (int x = 0; x < srcSize; x++)
                {
                    int dx = ox + x;
                    if (dx < 0 || dx >= dstWidth)
                    {
                        continue;
                    }

                    dst[dy * dstWidth + dx] = src[y * srcSize + x];
                }
            }
        }

        // ---- 目盛・ラベル ----

        static string Label(View v)
        {
            string axis = v.Name.StartsWith("pos") ? "+" + v.Name.Substring(3)
                                                   : "-" + v.Name.Substring(3);
            return "FROM " + axis;
        }

        /// <summary>
        /// 目盛・スケールバー・軸のラベルを画素へ焼く。
        /// **単位はメートル**（プレハブ単位 = メートル / 13-3a の実測）。
        /// </summary>
        static void Annotate(Color32[] pixels, Bounds bounds, View v, float orthoSize,
                             string title, Color32? boxColor)
            => Annotate(pixels, bounds, bounds.center, v, orthoSize, title, boxColor);

        static void Annotate(Color32[] pixels, Bounds bounds, Vector3 frameCenter, View v,
                             float orthoSize, string title, Color32? boxColor)
        {
            float metersPerPixel = 2f * orthoSize / Size;
            float pixelsPerMeter = 1f / metersPerPixel;

            Quaternion rotation = Quaternion.LookRotation(-v.Direction, v.UpHint);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            // 目盛の刻み: 画面上で 60〜240 px になる「切りのいい」長さ。
            float step = NiceStep(pixelsPerMeter);

            int cx = Size / 2;
            int cy = Size / 2;

            // **目盛はプレハブ座標で振る (13-3a の訂正)。**
            // 以前は画像中心を 0 として振っていたので、レンダラー一覧の座標と
            // Z で 6.5522 ずれていた（bbox 中心が原点ではない）。
            DrawAxisTicks(pixels, right, frameCenter, pixelsPerMeter, orthoSize, step,
                          cx, cy, true);
            DrawAxisTicks(pixels, up, frameCenter, pixelsPerMeter, orthoSize, step,
                          cx, cy, false);

            // bbox の外形（発光の枚では位置の手がかりになる）。
            if (boxColor.HasValue)
            {
                int halfW = Mathf.RoundToInt(HalfExtentAlong(bounds, right) * pixelsPerMeter);
                int halfH = Mathf.RoundToInt(HalfExtentAlong(bounds, up) * pixelsPerMeter);
                int bcx = cx + Mathf.RoundToInt(
                    Vector3.Dot(bounds.center - frameCenter, right) * pixelsPerMeter);
                int bcy = cy + Mathf.RoundToInt(
                    Vector3.Dot(bounds.center - frameCenter, up) * pixelsPerMeter);
                Rect32(pixels, bcx - halfW, bcy - halfH, halfW * 2, halfH * 2, boxColor.Value);
            }

            // スケールバー（右下）。
            int barPx = Mathf.RoundToInt(step * pixelsPerMeter);
            int bx = Size - 40 - barPx;
            int by = 46;
            HLine(pixels, bx, by, barPx, Ink);
            VLine(pixels, bx, by - 6, 13, Ink);
            VLine(pixels, bx + barPx - 1, by - 6, 13, Ink);
            string bar = step.ToString("0.#", CultureInfo.InvariantCulture) + " M";
            TinyFont.Draw(pixels, Size, Size,
                          bx + barPx / 2 - TinyFont.MeasureWidth(bar, 2) / 2, by + 10, bar, 2, Ink);

            // 見出しと軸。
            TinyFont.Draw(pixels, Size, Size, 14, Size - 26, title, 3, Ink);
            TinyFont.Draw(pixels, Size, Size, 14, Size - 52,
                          "ORTHOGRAPHIC / METERS / TICKS: PREFAB COORDS", 2, Ink);
            TinyFont.Draw(pixels, Size, Size, 14, Size - 74,
                          "1 PX = " + metersPerPixel.ToString("0.####", CultureInfo.InvariantCulture)
                          + " M", 2, Ink);

            string rightLabel = "RIGHT " + AxisName(right);
            string upLabel = "UP " + AxisName(up);
            TinyFont.Draw(pixels, Size, Size,
                          Size - TinyFont.MeasureWidth(rightLabel, 2) - 14, Size - 26,
                          rightLabel, 2, Ink);
            TinyFont.Draw(pixels, Size, Size,
                          Size - TinyFont.MeasureWidth(upLabel, 2) - 14, Size - 48,
                          upLabel, 2, Ink);
        }

        /// <summary>
        /// **プレハブ座標で目盛を振る。** 画面軸 `screenAxis` が対応するプレハブ軸を
        /// 判定し、その軸の値をラベルにする。
        /// </summary>
        static void DrawAxisTicks(Color32[] pixels, Vector3 screenAxis, Vector3 frameCenter,
                                  float pixelsPerMeter, float orthoSize, float step,
                                  int cx, int cy, bool horizontal)
        {
            AxisOf(screenAxis, out int index, out float sign);

            float center = frameCenter[index];
            float lo = center - orthoSize;
            float hi = center + orthoSize;

            int kMin = Mathf.CeilToInt(lo / step);
            int kMax = Mathf.FloorToInt(hi / step);

            for (int k = kMin; k <= kMax; k++)
            {
                float value = k * step;
                int offset = Mathf.RoundToInt(sign * (value - center) * pixelsPerMeter);
                int len = k == 0 ? 26 : 14;
                string label = value.ToString("0.#", CultureInfo.InvariantCulture);

                if (horizontal)
                {
                    int x = cx + offset;
                    if (x < 0 || x >= Size)
                    {
                        continue;
                    }

                    VLine(pixels, x, 0, len, Tick);
                    TinyFont.Draw(pixels, Size, Size,
                                  x - TinyFont.MeasureWidth(label, 2) / 2, len + 4, label, 2, Tick);
                }
                else
                {
                    int y = cy + offset;
                    if (y < 0 || y >= Size)
                    {
                        continue;
                    }

                    HLine(pixels, 0, y, len, Tick);
                    TinyFont.Draw(pixels, Size, Size, len + 4,
                                  y - TinyFont.MeasureHeight(2) / 2, label, 2, Tick);
                }
            }
        }

        static void AxisOf(Vector3 v, out int index, out float sign)
        {
            if (Mathf.Abs(v.x) > 0.9f) { index = 0; sign = Mathf.Sign(v.x); return; }
            if (Mathf.Abs(v.y) > 0.9f) { index = 1; sign = Mathf.Sign(v.y); return; }
            index = 2;
            sign = Mathf.Sign(v.z);
        }

        /// <summary>画面上で 60〜240 px になる切りのいい長さ [m]。</summary>
        static float NiceStep(float pixelsPerMeter)
        {
            float[] candidates = { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f, 200f, 500f };
            foreach (float c in candidates)
            {
                float px = c * pixelsPerMeter;
                if (px >= 60f && px <= 240f)
                {
                    return c;
                }
            }

            return candidates[candidates.Length - 1];
        }

        static string AxisName(Vector3 v)
        {
            if (Mathf.Abs(v.x) > 0.9f) { return v.x > 0 ? "+X" : "-X"; }
            if (Mathf.Abs(v.y) > 0.9f) { return v.y > 0 ? "+Y" : "-Y"; }
            if (Mathf.Abs(v.z) > 0.9f) { return v.z > 0 ? "+Z" : "-Z"; }
            return "?";
        }

        static void HLine(Color32[] p, int x, int y, int length, Color32 c)
        {
            if (y < 0 || y >= Size) { return; }
            for (int i = 0; i < length; i++)
            {
                int px = x + i;
                if (px >= 0 && px < Size) { p[y * Size + px] = c; }
            }
        }

        static void VLine(Color32[] p, int x, int y, int length, Color32 c)
        {
            if (x < 0 || x >= Size) { return; }
            for (int i = 0; i < length; i++)
            {
                int py = y + i;
                if (py >= 0 && py < Size) { p[py * Size + x] = c; }
            }
        }

        static void Rect32(Color32[] p, int x, int y, int w, int h, Color32 c)
        {
            HLine(p, x, y, w, c);
            HLine(p, x, y + h - 1, w, c);
            VLine(p, x, y, h, c);
            VLine(p, x + w - 1, y, h, c);
        }

        // ---- 一覧 ----

        static void WriteHeader(StringBuilder sb, string path, Bounds bounds, int rendererCount)
        {
            sb.AppendLine("== ステーションのレンダラー一覧と正投影レンダ (Step 13-3 コミット2) ==");
            sb.AppendLine("プレハブ : " + path);
            sb.AppendLine("**単位はメートル**（プレハブ単位 = メートル / 13-3a の実測）。");
            sb.AppendLine("プレハブを scale 1 / 回転なしで原点へ置いたときのワールド座標。");
            sb.AppendLine();
            sb.AppendLine("bbox size   : " + F(bounds.size));
            sb.AppendLine("bbox center : " + F(bounds.center));
            sb.AppendLine("bbox min    : " + F(bounds.min));
            sb.AppendLine("bbox max    : " + F(bounds.max));
            sb.AppendLine("レンダラー  : " + rendererCount + " 件");
            sb.AppendLine();
        }

        static void WriteRendererTable(StringBuilder sb, GameObject root, Renderer[] renderers)
        {
            sb.AppendLine("-- レンダラーごとの bbox（**口を探す手がかり**）--");
            sb.AppendLine("  name / マテリアル / 中心 (x,y,z) [m] / サイズ (x,y,z) [m] / 最小辺 [m]");

            foreach (Renderer r in renderers.OrderBy(r => r.name, StringComparer.Ordinal))
            {
                Vector3 size = MeshWorldSize(r);
                float min = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
                string materials = string.Join(",", r.sharedMaterials
                    .Where(m => m != null).Select(m => m.name).ToArray());

                sb.AppendLine("  " + PathOf(root, r.transform)
                              + " / " + materials
                              + " / " + F(r.bounds.center)
                              + " / " + F(size)
                              + " / " + min.ToString("F3", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine("  **`中心` は Renderer.bounds.center（ワールド軸に平行な箱の中心）、");
            sb.AppendLine("  `サイズ` はメッシュ自身の bounds に lossyScale を掛けた値**"
                          + "（回転前の寸法）。");
            sb.AppendLine("  回転しているレンダラーでは 2 つが一致しない。**口の実寸は");
            sb.AppendLine("  `サイズ` のほうを見ること。**");
            sb.AppendLine();
            sb.AppendLine("-- 撮影の条件 --");
        }

        static string PathOf(GameObject root, Transform t)
        {
            var parts = new List<string>();
            Transform current = t;
            while (current != null && current != root.transform)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static string F(Vector3 v) =>
            "(" + v.x.ToString("F3", CultureInfo.InvariantCulture)
                + ", " + v.y.ToString("F3", CultureInfo.InvariantCulture)
                + ", " + v.z.ToString("F3", CultureInfo.InvariantCulture) + ")";
    }
}
