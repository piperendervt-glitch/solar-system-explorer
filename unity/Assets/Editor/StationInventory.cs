using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **取り込んだステーションアセットの棚卸し (Step 13-2)。**
    /// 13-3（配置・スケール・ポート）の入力。**13-4（遠景と発光）は実施しないので、
    /// 発光まわりの出力は使われていない。**
    ///
    /// **候補を挙げるだけで、指名はしない。** 窓・航法灯・ドッキング口が
    /// どれかは人間が決める。
    ///
    ///   run_unity.ps1 -Method SolarSetup.InventoryStation
    /// </summary>
    public static class StationInventory
    {
        public static void Report()
        {
            string root = StationPackage.DestinationRoot;
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning("[StationInventory] 取り込まれていない: " + root);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("== ステーションアセットの棚卸し (Step 13-2) ==");
            sb.AppendLine("取り込み先: " + root);
            sb.AppendLine();

            ReportSize(sb, root);
            ReportMaterials(sb, root);
            ReportPrefabs(sb, root);

            Debug.Log(sb.ToString());
        }

        // ---- サイズ ----

        static void ReportSize(StringBuilder sb, string root)
        {
            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, root);

            long bytes = 0;
            int files = 0;
            if (Directory.Exists(absolute))
            {
                foreach (string f in Directory.GetFiles(absolute, "*", SearchOption.AllDirectories))
                {
                    bytes += new FileInfo(f).Length;
                    files++;
                }
            }

            sb.AppendLine($"-- サイズ --");
            sb.AppendLine($"  {files} ファイル / {bytes} バイト = {bytes / 1048576.0:F2} MB"
                          + "（.meta を含む）");
            sb.AppendLine();
        }

        // ---- マテリアル ----

        sealed class MaterialEntry
        {
            public string Name = string.Empty;
            public string Shader = string.Empty;
            public bool Emission;
            public string EmissionMap = string.Empty;
            public int RendererCount;
            public readonly HashSet<string> Meshes = new HashSet<string>();
        }

        static void ReportMaterials(StringBuilder sb, string root)
        {
            var entries = new Dictionary<string, MaterialEntry>();

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    continue;
                }

                Texture emission = m.HasProperty("_EmissionMap")
                    ? m.GetTexture("_EmissionMap")
                    : null;

                entries[path] = new MaterialEntry
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Shader = m.shader != null ? m.shader.name : "<null>",
                    Emission = m.IsKeywordEnabled("_EMISSION") || emission != null,
                    EmissionMap = emission != null ? emission.name : string.Empty,
                };
            }

            // どのレンダラーが使っているか（参照メッシュ数）。
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null)
                        {
                            continue;
                        }

                        string mp = AssetDatabase.GetAssetPath(m);
                        if (!entries.TryGetValue(mp, out MaterialEntry e))
                        {
                            continue;
                        }

                        e.RendererCount++;
                        var filter = r.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            e.Meshes.Add(filter.sharedMesh.name);
                        }
                    }
                }
            }

            sb.AppendLine("-- マテリアル --");
            sb.AppendLine("  name / shader / 発光 / 発光マップ / レンダラー / メッシュ");
            foreach (MaterialEntry e in entries.Values.OrderBy(e => e.Name))
            {
                sb.AppendLine($"  {e.Name} / {e.Shader} / {(e.Emission ? "あり" : "無し")}"
                              + $" / {(e.EmissionMap.Length > 0 ? e.EmissionMap : "-")}"
                              + $" / {e.RendererCount} / {e.Meshes.Count}");
            }

            int urp = entries.Values.Count(e => e.Shader.StartsWith("Universal Render Pipeline/"));
            sb.AppendLine($"  合計 {entries.Count} 件 / URP {urp} 件");
            foreach (MaterialEntry e in entries.Values.Where(
                         e => !e.Shader.StartsWith("Universal Render Pipeline/")))
            {
                sb.AppendLine($"  **URP でない**: {e.Name} / {e.Shader}");
            }

            sb.AppendLine();
            sb.AppendLine("-- 発光を持つマテリアル（13-4 の入力だった。13-4 は実施しない）--");
            foreach (MaterialEntry e in entries.Values.Where(e => e.Emission).OrderBy(e => e.Name))
            {
                sb.AppendLine($"  {e.Name} / 発光マップ {e.EmissionMap}");
            }

            sb.AppendLine();
            sb.AppendLine("-- 候補（**候補として挙げるだけ。指名は人間がする**）--");
            sb.AppendLine("  窓・航法灯の候補 = 発光を持つマテリアル（上の一覧）");
            sb.AppendLine("  ドッキング口の候補 = 13-3 でプレハブの形を見てから決める");
            sb.AppendLine();
        }

        // ---- プレハブ ----

        static void ReportPrefabs(StringBuilder sb, string root)
        {
            sb.AppendLine("-- プレハブ（13-3 で Scale を決める入力）--");

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}"
                              + $" / GUID {guid} / レンダラー {renderers.Length}");

                if (renderers.Length == 0)
                {
                    continue;
                }

                // **バウンディングボックスはプレハブのローカル座標で出す。**
                // ワールドの Bounds はプレハブ配置前だと意味を持たない。
                Bounds bounds = LocalBounds(prefab, renderers);

                sb.AppendLine($"    ピボット（プレハブ原点）からの中心オフセット: {bounds.center}");
                sb.AppendLine($"    バウンディングボックス（プレハブのローカル単位）: {bounds.size}");
                sb.AppendLine($"    **単位の読み方**: このアセットは Unity の既定（1 = 1 m）で"
                              + " 作られている前提。この世界は 1 unit = 1 km なので、");
                sb.AppendLine($"    メートルとして読むなら {bounds.size} m、"
                              + $"units に直すには 0.001 倍（13-3 で確定する）");
                sb.AppendLine($"    前方軸: プレハブのローカル +Z を前方と仮定"
                              + "（**実測ではない。13-3 で 2 軸で確定する**）");
            }

            sb.AppendLine();
        }

        static Bounds LocalBounds(GameObject prefab, Renderer[] renderers)
        {
            var bounds = new Bounds();
            bool first = true;

            foreach (Renderer r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 toPrefab = WorldToPrefab(prefab, r.transform);
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 p = toPrefab.MultiplyPoint3x4(v);
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

        /// <summary>レンダラーのローカル座標 -> プレハブ原点座標。</summary>
        static Matrix4x4 WorldToPrefab(GameObject prefab, Transform t)
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
    }
}
