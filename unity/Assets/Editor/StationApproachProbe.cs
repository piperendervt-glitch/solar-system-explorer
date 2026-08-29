using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **接近円錐のプローブ (Step 13-3b)。** `CockpitVisibilityProbe` と同型。
    ///
    /// ■ 何を確かめるか
    /// **ポート正面から `PortStandoff` の距離まで、接近の円錐の中に構造物が無いこと。**
    /// 13-1a で置いた `MinStandoff = 半径 + near clip` は
    /// **「構造物が半径 R の球である」という仮定**の式で、実際のモデルでは
    /// ポート付近の突起が球面より手前に来る可能性があった（§0 の宿題）。
    /// **仮定ではなく実際の頂点を数えて置き換える。**
    ///
    /// ■ 数え方
    /// 円錐は `PortForward`（プレハブ +Z）に開く。プレハブ座標のまま、
    /// ポート面の中心から各頂点への差分を取り、
    ///   前方成分 &gt; 0（ポート面より前）
    ///   軸からの角度 &lt;= 半頂角
    ///   距離 &lt;= 円錐の長さ
    /// をすべて満たす頂点を数える。**0 件を期待するが、決めつけずに数える。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.ProbeStationApproach
    /// </summary>
    public static class StationApproachProbe
    {
        /// <summary>円錐の半頂角 [度]。**接近の許容角。**</summary>
        const double HalfAngleDegrees = 30.0;

        /// <summary>円錐の長さを `PortStandoff` の何倍まで見るか。</summary>
        const double LengthMultiplier = 1.0;

        public static void Run()
        {
            StationDefinition definition = StationCatalog.Cobble();
            string path = AssetDatabase.GUIDToAssetPath(definition.PrefabGuid);

            var sb = new StringBuilder();
            sb.AppendLine("== 接近円錐のプローブ (Step 13-3b) ==");
            sb.AppendLine("定義: " + definition.Id);
            sb.AppendLine();

            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine("**取り込まれていない**（GUID " + definition.PrefabGuid + "）。");
                sb.AppendLine("clone 直後は正常。この節の数字は取れていない。");
                Write(sb);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                sb.AppendLine("**プレハブとして読めない**: " + path);
                Write(sb);
                return;
            }

            // ---- 条件（すべてプレハブ単位 = メートル）----
            Vec3d p = definition.PortLocal;
            var port = new Vector3((float)p.X, (float)p.Y, (float)p.Z);

            Vec3d f = definition.PortForward;
            Vector3 axis = new Vector3((float)f.X, (float)f.Y, (float)f.Z).normalized;

            // `PortStandoff` は units。円錐はプレハブ座標で見るので Scale で割る。
            double standoffUnits = definition.PortStandoff;
            double coneLengthLocal = standoffUnits * LengthMultiplier / definition.Scale;

            sb.AppendLine("-- 条件 --");
            sb.AppendLine("  プレハブ         : " + path);
            sb.AppendLine("  Scale            : "
                          + definition.Scale.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  ポート面の中心   : " + F(port) + " [プレハブ単位]");
            sb.AppendLine("  円錐の軸         : " + F(axis) + "（PortForward）");
            sb.AppendLine("  半頂角           : "
                          + HalfAngleDegrees.ToString("F1", CultureInfo.InvariantCulture) + " 度");
            sb.AppendLine("  PortStandoff     : "
                          + standoffUnits.ToString("F5", CultureInfo.InvariantCulture)
                          + " units = "
                          + (standoffUnits * 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine("  円錐の長さ       : "
                          + coneLengthLocal.ToString("F4", CultureInfo.InvariantCulture)
                          + " [プレハブ単位] = "
                          + (coneLengthLocal * definition.Scale * 1000.0)
                              .ToString("F1", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine();

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            double cosHalf = Math.Cos(HalfAngleDegrees * Math.PI / 180.0);

            int total = 0;
            int inside = 0;
            var offenders = new List<string>();
            double nearestAhead = double.PositiveInfinity;
            string nearestName = "（前方に頂点なし）";

            foreach (Renderer r in renderers.OrderBy(r => r.name, StringComparer.Ordinal))
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 m = LocalMatrix(prefab, r.transform);
                int hits = 0;

                foreach (Vector3 v in mesh.vertices)
                {
                    total++;
                    Vector3 d = m.MultiplyPoint3x4(v) - port;
                    float along = Vector3.Dot(d, axis);
                    if (along <= 0f)
                    {
                        continue;
                    }

                    float length = d.magnitude;
                    if (length <= 0f)
                    {
                        continue;
                    }

                    if (length < nearestAhead)
                    {
                        nearestAhead = length;
                        nearestName = PathOf(prefab, r.transform);
                    }

                    if (along / length >= cosHalf && length <= coneLengthLocal)
                    {
                        hits++;
                    }
                }

                if (hits > 0)
                {
                    inside += hits;
                    offenders.Add(PathOf(prefab, r.transform) + " : " + hits + " 頂点");
                }
            }

            sb.AppendLine("-- 結果 --");
            sb.AppendLine("  調べた頂点 : " + total);
            sb.AppendLine("  円錐の中の頂点 : " + inside + " 件");

            if (offenders.Count == 0)
            {
                sb.AppendLine("  **0 件。** ポート正面から "
                              + (standoffUnits * 1000.0).ToString("F1", CultureInfo.InvariantCulture)
                              + " m まで、半頂角 "
                              + HalfAngleDegrees.ToString("F0", CultureInfo.InvariantCulture)
                              + " 度の円錐に構造物は入っていない。");
            }
            else
            {
                sb.AppendLine("  **構造物が円錐に入っている:**");
                foreach (string o in offenders)
                {
                    sb.AppendLine("    " + o);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  ポート面より前方にある最も近い頂点 : "
                          + (double.IsInfinity(nearestAhead)
                                 ? "**無し**"
                                 : nearestAhead.ToString("F4", CultureInfo.InvariantCulture)
                                   + " [プレハブ単位] / " + nearestName));
            sb.AppendLine();

            sb.AppendLine("-- 球の仮定の置き換え (§0 の宿題) --");
            sb.AppendLine("  旧: MinStandoff = 半径 + near clip = "
                          + (definition.RadiusUnits + StationCatalog.NearfieldNearClipUnits)
                              .ToString("F5", CultureInfo.InvariantCulture) + " units = "
                          + ((definition.RadiusUnits + StationCatalog.NearfieldNearClipUnits) * 1000.0)
                              .ToString("F1", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine("      **構造物が半径 R の球だという仮定**の式。");
            sb.AppendLine("  新: MinStandoff = ポートより前方のはみ出し + near clip = "
                          + definition.MinStandoff(StationCatalog.NearfieldNearClipUnits)
                              .ToString("F5", CultureInfo.InvariantCulture) + " units = "
                          + (definition.MinStandoff(StationCatalog.NearfieldNearClipUnits) * 1000.0)
                              .ToString("F1", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine("      はみ出しは "
                          + definition.HullAheadOfPortLocal.ToString("R", CultureInfo.InvariantCulture)
                          + " [プレハブ単位]（上の「前方にある最も近い頂点」が裏付け）。");
            sb.AppendLine();
            sb.AppendLine("  実際の PortStandoff : "
                          + standoffUnits.ToString("F5", CultureInfo.InvariantCulture)
                          + " units（下限 + 余裕 "
                          + StationCatalog.StandoffMarginUnits.ToString("F3", CultureInfo.InvariantCulture)
                          + "）");
            sb.AppendLine("  ドッキング後の隙間 : "
                          + definition.DockedClearance.ToString("F5", CultureInfo.InvariantCulture)
                          + " units = "
                          + (definition.DockedClearance * 1000.0)
                              .ToString("F1", CultureInfo.InvariantCulture) + " m");
            sb.AppendLine();

            Write(sb);
        }

        /// <summary>テストが読む値。**円錐の中の頂点数。**</summary>
        public static int CountInsideCone(GameObject prefab, StationDefinition definition,
                                          double halfAngleDegrees, double coneLengthLocal)
        {
            Vec3d p = definition.PortLocal;
            var port = new Vector3((float)p.X, (float)p.Y, (float)p.Z);

            Vec3d f = definition.PortForward;
            Vector3 axis = new Vector3((float)f.X, (float)f.Y, (float)f.Z).normalized;

            double cosHalf = Math.Cos(halfAngleDegrees * Math.PI / 180.0);
            int inside = 0;

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 m = LocalMatrix(prefab, r.transform);
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 d = m.MultiplyPoint3x4(v) - port;
                    float along = Vector3.Dot(d, axis);
                    float length = d.magnitude;
                    if (along <= 0f || length <= 0f)
                    {
                        continue;
                    }

                    if (along / length >= cosHalf && length <= coneLengthLocal)
                    {
                        inside++;
                    }
                }
            }

            return inside;
        }

        public static double DefaultHalfAngleDegrees => HalfAngleDegrees;

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
                Application.dataPath, "..", "..", "verify", "station-approach.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log("[StationApproach] 書き出した: " + report);
        }
    }
}
