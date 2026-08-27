using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 取り込んだマテリアルの棚卸し (Step 11-1c)。
    ///
    /// **11-3（計器の画面）と 11-4（照明）の入力を作るのが目的。**
    /// 見たいのは 3 つ。
    ///   1. ガラスが Transparent か（不透明なら窓の外が見えず Demo 3 のゴールが崩れる）
    ///   2. 画面のマテリアルが他のメッシュと共有されていないか
    ///      （共有のままだと Render Texture が内壁にも映る）
    ///   3. 発光しているマテリアルはどれか（11-4b の `Definition.Emissives` の入力）
    ///
    /// **数えるだけで、何も書き換えない。** 張り替えが要ると分かったらそれは別の作業。
    /// </summary>
    public static class CockpitInventory
    {
        /// <summary>マテリアル 1 枚分の棚卸し結果。</summary>
        public sealed class Entry
        {
            public string Path;
            public string Name;
            public string ShaderName;

            /// <summary>URP の `_Surface`。0 = Opaque / 1 = Transparent / 無ければ null。</summary>
            public float? Surface;

            public int RenderQueue;
            public bool EmissionKeyword;
            public Color EmissionColor;
            public bool HasEmissionMap;

            /// <summary>このマテリアルを参照しているレンダラーの数。</summary>
            public int RendererCount;

            /// <summary>参照しているメッシュの種類数。</summary>
            public int MeshCount;

            /// <summary>参照しているレンダラーの名前（プレハブ名付き）。</summary>
            public List<string> Renderers = new List<string>();

            public bool IsTransparent => Surface.HasValue && Surface.Value > 0.5f;

            /// <summary>**発光しているか。** キーワードだけでは判定しない（既定で全部立っている）。</summary>
            public bool IsEmissive =>
                EmissionKeyword
                && (EmissionColor.maxColorComponent > 0.001f || HasEmissionMap);
        }

        /// <summary>ThirdParty 配下のマテリアルを棚卸しする。</summary>
        public static List<Entry> Collect()
        {
            Dictionary<string, Entry> byPath = AssetDatabase
                .FindAssets("t:Material", new[] { CockpitPackage.DestinationRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(Describe)
                .Where(e => e != null)
                .ToDictionary(e => e.Path, e => e);

            CountReferences(byPath);
            return byPath.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static Entry Describe(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                return null;
            }

            return new Entry
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                ShaderName = material.shader != null ? material.shader.name : "<null>",
                Surface = material.HasProperty("_Surface") ? material.GetFloat("_Surface") : (float?)null,
                RenderQueue = material.renderQueue,
                EmissionKeyword = material.IsKeywordEnabled("_EMISSION"),
                EmissionColor = material.HasProperty("_EmissionColor")
                    ? material.GetColor("_EmissionColor")
                    : Color.black,
                HasEmissionMap = material.HasProperty("_EmissionMap")
                                 && material.GetTexture("_EmissionMap") != null,
            };
        }

        /// <summary>
        /// プレハブを走査して、どのレンダラーがどのマテリアルを使っているかを数える。
        ///
        /// **「参照メッシュ数」はレンダラー数とは別に数える。** 同じメッシュが
        /// 複数のレンダラーに載っていることがあるので、共有の判定にはメッシュ側が要る。
        /// </summary>
        static void CountReferences(Dictionary<string, Entry> byPath)
        {
            var meshes = new Dictionary<string, HashSet<string>>();

            foreach (string prefabPath in AssetDatabase
                         .FindAssets("t:Prefab", new[] { CockpitPackage.DestinationRoot })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Distinct()
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    string meshName = MeshNameOf(renderer);

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            continue;
                        }

                        string path = AssetDatabase.GetAssetPath(material);
                        if (!byPath.TryGetValue(path, out Entry entry))
                        {
                            continue;
                        }

                        entry.RendererCount++;
                        entry.Renderers.Add($"{prefabName}/{renderer.name}({meshName})");

                        if (!meshes.TryGetValue(path, out HashSet<string> set))
                        {
                            set = new HashSet<string>();
                            meshes[path] = set;
                        }

                        set.Add(meshName);
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in meshes)
            {
                byPath[pair.Key].MeshCount = pair.Value.Count;
            }
        }

        static string MeshNameOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh != null ? skinned.sharedMesh.name : "<mesh 無し>";
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.name
                : "<mesh 無し>";
        }

        /// <summary>棚卸しをログに出す。**書き換えはしない。**</summary>
        public static void Log()
        {
            List<Entry> entries = Collect();

            var sb = new StringBuilder(
                $"[CockpitInventory] ThirdParty のマテリアル {entries.Count} 件");
            sb.AppendLine();
            sb.Append("  name / shader / surface / queue / 発光 / レンダラー / メッシュ");

            foreach (Entry e in entries)
            {
                sb.AppendLine();
                sb.Append(string.Format(
                    "  {0,-36} {1,-32} {2,-11} {3,4} {4,-6} r={5,-3} m={6}",
                    e.Name,
                    e.ShaderName,
                    e.Surface.HasValue ? (e.IsTransparent ? "Transparent" : "Opaque") : "-",
                    e.RenderQueue,
                    e.IsEmissive ? "発光" : "-",
                    e.RendererCount,
                    e.MeshCount));
            }

            Debug.Log(sb.ToString());

            LogGlass(entries);
            LogScreens(entries);
            LogEmissive(entries);
        }

        static void LogGlass(List<Entry> entries)
        {
            Entry[] glass = entries.Where(e => e.Name.Contains("Glass")).ToArray();

            var sb = new StringBuilder($"[CockpitInventory] ガラス {glass.Length} 件"
                                       + "（**不透明なら窓の外が見えない**）");
            foreach (Entry e in glass)
            {
                sb.AppendLine();
                sb.Append($"  {e.Name}: {(e.IsTransparent ? "Transparent" : "**Opaque**")} "
                          + $"/ queue {e.RenderQueue} / レンダラー {e.RendererCount}");
            }

            Debug.Log(sb.ToString());
        }

        static void LogScreens(List<Entry> entries)
        {
            Entry[] screens = entries.Where(e => e.Name.Contains("Screens")).ToArray();

            var sb = new StringBuilder($"[CockpitInventory] 画面のマテリアル {screens.Length} 件"
                                       + "（**内壁と共有されていると RT が内壁にも映る**）");
            foreach (Entry e in screens)
            {
                sb.AppendLine();
                sb.Append($"  {e.Name}: レンダラー {e.RendererCount} / メッシュ {e.MeshCount}");
                foreach (string r in e.Renderers)
                {
                    sb.AppendLine();
                    sb.Append("    " + r);
                }
            }

            Debug.Log(sb.ToString());
        }

        static void LogEmissive(List<Entry> entries)
        {
            Entry[] emissive = entries.Where(e => e.IsEmissive).ToArray();

            var sb = new StringBuilder(
                $"[CockpitInventory] 発光しているマテリアル {emissive.Length} 件"
                + "（11-4b の Definition.Emissives の入力）");

            foreach (Entry e in emissive)
            {
                sb.AppendLine();
                sb.Append(string.Format(
                    "  {0}: color ({1:F2}, {2:F2}, {3:F2}) / map {4} / レンダラー {5}",
                    e.Name, e.EmissionColor.r, e.EmissionColor.g, e.EmissionColor.b,
                    e.HasEmissionMap ? "あり" : "無し", e.RendererCount));
            }

            Debug.Log(sb.ToString());
        }
    }
}
