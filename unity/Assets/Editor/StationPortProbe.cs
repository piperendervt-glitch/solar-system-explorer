using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **ドッキングポート（+Z 端）の幾何を数値で測る (Step 13-3a)。**
    ///
    /// ポートは +Z に決まった（人間の指定）。ここで確かめるのは 3 つ:
    ///   1. +Z 端に来ているレンダラーはどれか
    ///   2. その前面（z が最大の面）の中心と広がり。**開口（穴）が幾何として
    ///      存在するかどうかも見る**
    ///   3. **その面より前方に頂点を持つレンダラーが 1 つも無いこと**
    ///
    /// **口の寸法を指名しない。** 幾何から開口を特定できないときは、
    /// 「特定できない」と書いて止まる（絵から人間が読む形に切り替える）。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ProbeStationPort
    /// </summary>
    public static class StationPortProbe
    {
        const string PrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        /// <summary>「前面」とみなす z の許容幅 [m]。</summary>
        const float FaceEpsilon = 0.02f;

        /// <summary>半径のヒストグラムの階級数。</summary>
        const int RadialBins = 24;

        public static void Run()
        {
            string path = AssetDatabase.GUIDToAssetPath(PrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[StationPort] 取り込まれていない: " + PrefabGuid);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[StationPort] プレハブとして読めない: " + path);
                return;
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);

            var sb = new StringBuilder();
            sb.AppendLine("== ドッキングポート（+Z 端）の実測 (Step 13-3a) ==");
            sb.AppendLine("プレハブ : " + path);
            sb.AppendLine("**座標はプレハブ単位 = メートル。** scale 1 / 回転なし。");
            sb.AppendLine();

            List<Part> parts = Collect(prefab, renderers);

            ReportFrontRanking(sb, parts);
            Part front = parts.OrderByDescending(p => p.MaxZ).FirstOrDefault();

            if (front == null)
            {
                sb.AppendLine("**頂点を持つレンダラーが無い。** ここで止まる。");
                Write(sb);
                return;
            }

            ReportFace(sb, front);
            ReportSlices(sb, front);
            ReportNothingAhead(sb, parts, front);
            ReportNeighbours(sb, parts, front);

            Write(sb);
        }

        sealed class Part
        {
            public string Name;
            public Vector3[] Vertices;
            public float MaxZ;
            public float MinZ;
            public Bounds Bounds;
        }

        static List<Part> Collect(GameObject prefab, Renderer[] renderers)
        {
            var parts = new List<Part>();

            foreach (Renderer r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 m = LocalMatrix(prefab, r.transform);
                Vector3[] raw = mesh.vertices;
                var points = new Vector3[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    points[i] = m.MultiplyPoint3x4(raw[i]);
                }

                if (points.Length == 0)
                {
                    continue;
                }

                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (Vector3 p in points)
                {
                    bounds.Encapsulate(p);
                }

                parts.Add(new Part
                {
                    Name = PathOf(prefab, r.transform),
                    Vertices = points,
                    MaxZ = bounds.max.z,
                    MinZ = bounds.min.z,
                    Bounds = bounds,
                });
            }

            return parts;
        }

        // ---- 1. +Z 端の順位 ----

        static void ReportFrontRanking(StringBuilder sb, List<Part> parts)
        {
            sb.AppendLine("-- (1) +Z 側から見た順位（頂点の実測）--");
            sb.AppendLine("  name / max z / min z / bbox 中心 / bbox サイズ");

            foreach (Part p in parts.OrderByDescending(p => p.MaxZ).Take(6))
            {
                sb.AppendLine("  " + p.Name
                              + " / " + p.MaxZ.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + p.MinZ.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + F(p.Bounds.center)
                              + " / " + F(p.Bounds.size));
            }

            sb.AppendLine();
        }

        // ---- 2. 前面の形 ----

        static void ReportFace(StringBuilder sb, Part front)
        {
            sb.AppendLine("-- (2) 最前端のレンダラーの前面 --");
            sb.AppendLine("  レンダラー : " + front.Name);
            sb.AppendLine("  max z      : "
                          + front.MaxZ.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  前面とみなす範囲 : z >= "
                          + (front.MaxZ - FaceEpsilon).ToString("F4", CultureInfo.InvariantCulture)
                          + "（幅 " + FaceEpsilon.ToString("F2", CultureInfo.InvariantCulture) + " m）");

            Vector3[] face = front.Vertices
                .Where(v => v.z >= front.MaxZ - FaceEpsilon)
                .ToArray();

            sb.AppendLine("  前面の頂点 : " + face.Length + " / " + front.Vertices.Length + " 件");

            if (face.Length == 0)
            {
                sb.AppendLine("  **前面の頂点が取れない。** ここで止まる。");
                sb.AppendLine();
                return;
            }

            float minX = face.Min(v => v.x);
            float maxX = face.Max(v => v.x);
            float minY = face.Min(v => v.y);
            float maxY = face.Max(v => v.y);
            var centroid = new Vector3(face.Average(v => v.x), face.Average(v => v.y), front.MaxZ);
            var boxCenter = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, front.MaxZ);

            sb.AppendLine("  前面の広がり X : "
                          + minX.ToString("F4", CultureInfo.InvariantCulture) + " 〜 "
                          + maxX.ToString("F4", CultureInfo.InvariantCulture)
                          + " （幅 " + (maxX - minX).ToString("F4", CultureInfo.InvariantCulture) + " m）");
            sb.AppendLine("  前面の広がり Y : "
                          + minY.ToString("F4", CultureInfo.InvariantCulture) + " 〜 "
                          + maxY.ToString("F4", CultureInfo.InvariantCulture)
                          + " （幅 " + (maxY - minY).ToString("F4", CultureInfo.InvariantCulture) + " m）");
            sb.AppendLine("  前面の重心   : " + F(centroid));
            sb.AppendLine("  前面の外接矩形の中心 : " + F(boxCenter));
            sb.AppendLine();

            ReportRadial(sb, face, boxCenter);
        }

        /// <summary>
        /// **穴があるかを半径のヒストグラムで見る。**
        /// 中心付近に頂点が無い（内側の階級が空）なら、幾何としての開口がある。
        /// 中心まで頂点が詰まっていれば、ハッチは**板に描かれた絵**で、
        /// **幾何からは開口を特定できない。**
        /// </summary>
        static void ReportRadial(StringBuilder sb, Vector3[] face, Vector3 center)
        {
            float maxR = face.Max(v => Distance2D(v, center));
            if (maxR <= 0f)
            {
                sb.AppendLine("  半径 0。ヒストグラムを出せない");
                sb.AppendLine();
                return;
            }

            var counts = new int[RadialBins];
            foreach (Vector3 v in face)
            {
                int bin = Mathf.Clamp(
                    Mathf.FloorToInt(Distance2D(v, center) / maxR * RadialBins), 0, RadialBins - 1);
                counts[bin]++;
            }

            sb.AppendLine("  -- 外接矩形の中心からの距離のヒストグラム（穴の有無を見る）--");
            sb.AppendLine("     最大半径 " + maxR.ToString("F4", CultureInfo.InvariantCulture) + " m");
            for (int i = 0; i < RadialBins; i++)
            {
                float lo = maxR * i / RadialBins;
                float hi = maxR * (i + 1) / RadialBins;
                sb.AppendLine("     " + lo.ToString("F3", CultureInfo.InvariantCulture)
                              + " 〜 " + hi.ToString("F3", CultureInfo.InvariantCulture)
                              + " : " + counts[i]);
            }

            int emptyInner = 0;
            while (emptyInner < RadialBins && counts[emptyInner] == 0)
            {
                emptyInner++;
            }

            sb.AppendLine();
            if (emptyInner == 0)
            {
                sb.AppendLine("  **中心まで頂点が詰まっている = 幾何としての穴は無い。**");
                sb.AppendLine("  前面は塞がった板で、ハッチの八角形は**テクスチャの絵**。");
                sb.AppendLine("  **開口の実寸は幾何からは特定できない。**");
                sb.AppendLine("  近接図 13-3_hatch_posZ.png から人間が読むこと。");
            }
            else
            {
                float innerRadius = maxR * emptyInner / RadialBins;
                sb.AppendLine("  **内側 " + emptyInner + " 階級が空 = 半径 "
                              + innerRadius.ToString("F4", CultureInfo.InvariantCulture)
                              + " m まで頂点が無い。**");
                sb.AppendLine("  幾何としての開口がある可能性。直径にすると "
                              + (innerRadius * 2f).ToString("F4", CultureInfo.InvariantCulture) + " m");
                sb.AppendLine("  **ただしこれは「頂点が無い」だけで、面が張られていない証拠ではない。**");
            }

            sb.AppendLine();
        }

        // ---- 2b. 前面から後ろへの断面 ----

        /// <summary>
        /// **最前端のレンダラーを z で薄切りにして、各切片の広がりを出す。**
        /// 前面の板が何段になっているか（縁・フランジ・胴）を数値で見るため。
        /// **どれが「開口」かは指名しない。**
        /// </summary>
        static void ReportSlices(StringBuilder sb, Part front)
        {
            const float depth = 0.60f;
            const int bins = 12;

            sb.AppendLine("-- (2b) 最前端のレンダラーの z 断面（前面から後ろへ "
                          + depth.ToString("F2", CultureInfo.InvariantCulture) + " m）--");
            sb.AppendLine("  中心は前面の外接矩形の中心 (0.0300, 0.2400) を使う。");
            sb.AppendLine("  z の範囲 / 頂点数 / |x| 幅 / |y| 幅 / 中心からの最大半径 [m]");

            var center = new Vector2(0.03f, 0.24f);

            for (int i = 0; i < bins; i++)
            {
                float hi = front.MaxZ - depth * i / bins;
                float lo = front.MaxZ - depth * (i + 1) / bins;

                Vector3[] slice = front.Vertices.Where(v => v.z > lo && v.z <= hi).ToArray();
                if (slice.Length == 0)
                {
                    sb.AppendLine("  " + lo.ToString("F3", CultureInfo.InvariantCulture)
                                  + " 〜 " + hi.ToString("F3", CultureInfo.InvariantCulture)
                                  + " / 0 / - / - / -");
                    continue;
                }

                float wx = slice.Max(v => v.x) - slice.Min(v => v.x);
                float wy = slice.Max(v => v.y) - slice.Min(v => v.y);
                float r = slice.Max(v => Vector2.Distance(new Vector2(v.x, v.y), center));

                sb.AppendLine("  " + lo.ToString("F3", CultureInfo.InvariantCulture)
                              + " 〜 " + hi.ToString("F3", CultureInfo.InvariantCulture)
                              + " / " + slice.Length
                              + " / " + wx.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + wy.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + r.ToString("F4", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        // ---- 3. 前方に何も無いこと ----

        static void ReportNothingAhead(StringBuilder sb, List<Part> parts, Part front)
        {
            sb.AppendLine("-- (3) 最前端の面より前方 (z > max z) に頂点があるか --");

            float threshold = front.MaxZ;
            var offenders = new List<string>();

            foreach (Part p in parts)
            {
                int ahead = p.Vertices.Count(v => v.z > threshold + 1e-4f);
                if (ahead > 0)
                {
                    offenders.Add(p.Name + " : " + ahead + " 頂点 / max z "
                                  + p.MaxZ.ToString("F4", CultureInfo.InvariantCulture));
                }
            }

            sb.AppendLine("  しきい値 z > "
                          + threshold.ToString("F4", CultureInfo.InvariantCulture)
                          + "（" + front.Name + " の前面）");

            if (offenders.Count == 0)
            {
                sb.AppendLine("  **0 件。** " + front.Name + " の前面が構造全体の最前端。");
            }
            else
            {
                sb.AppendLine("  **" + offenders.Count + " 件ある。前提が崩れている:**");
                foreach (string o in offenders)
                {
                    sb.AppendLine("    " + o);
                }
            }

            sb.AppendLine();
        }

        // ---- 近傍（接近円錐の入力）----

        static void ReportNeighbours(StringBuilder sb, List<Part> parts, Part front)
        {
            sb.AppendLine("-- (参考) 前面から見た各レンダラーの「後退量」と横の張り出し --");
            sb.AppendLine("  接近円錐（13-3b）の入力。**ここでは判定しない。**");
            sb.AppendLine("  name / 前面からの後退 (max z との差) [m] / |x| の最大 [m] / |y| の最大 [m]");

            foreach (Part p in parts.OrderByDescending(p => p.MaxZ))
            {
                float setback = front.MaxZ - p.MaxZ;
                float maxAbsX = p.Vertices.Max(v => Mathf.Abs(v.x));
                float maxAbsY = p.Vertices.Max(v => Mathf.Abs(v.y));

                sb.AppendLine("  " + p.Name
                              + " / " + setback.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + maxAbsX.ToString("F4", CultureInfo.InvariantCulture)
                              + " / " + maxAbsY.ToString("F4", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        // ---- 下回り ----

        static float Distance2D(Vector3 v, Vector3 c)
            => Mathf.Sqrt((v.x - c.x) * (v.x - c.x) + (v.y - c.y) * (v.y - c.y));

        static Matrix4x4 LocalMatrix(GameObject prefab, Transform t)
        {
            Matrix4x4 m = Matrix4x4.identity;
            Transform current = t;
            while (current != null && current != prefab.transform)
            {
                m = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * m;
                current = current.parent;
            }

            return m;
        }

        static string PathOf(GameObject prefab, Transform t)
        {
            var parts = new List<string>();
            Transform current = t;
            while (current != null && current != prefab.transform)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static string F(Vector3 v) =>
            "(" + v.x.ToString("F4", CultureInfo.InvariantCulture)
                + ", " + v.y.ToString("F4", CultureInfo.InvariantCulture)
                + ", " + v.z.ToString("F4", CultureInfo.InvariantCulture) + ")";

        static void Write(StringBuilder sb)
        {
            string report = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "verify", "station-port.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log("[StationPort] 書き出した: " + report);
        }
    }
}
