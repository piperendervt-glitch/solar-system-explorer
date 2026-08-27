using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 取り込んだアセットを URP へ変換する (Step 11-1b)。
    ///
    /// ■ **`Converters.RunInBatchMode` は使えない（実測）**
    /// URP 17.3.0 の公式の一括変換は、**呼ぶと必ず例外で落ちる。**
    /// `Converters.GetConvertersInContainer` が
    /// `TypeCache.GetTypesDerivedFrom&lt;RenderPipelineConverter&gt;()` の結果を
    /// **abstract かどうかを見ずに** `Activator.CreateInstance` に渡すため、
    /// 2D 側の抽象クラス `Base2DMaterialUpgrader` で
    /// `MissingMethodException: Default constructor not found` になる
    /// （logs/unity_20260827_224900.log で実測。Converters.cs:263）。
    /// **どのプロジェクトでも起きる URP 側の不具合**で、こちらの書き方では避けられない。
    ///
    /// ■ 代わりに**同じ変換表を直接使う**
    /// あの変換器の中身は `MaterialUpgrader` の一覧を材料に回しているだけで、
    /// その一覧を取る `MaterialUpgrader.FetchAllUpgradersForPipeline` と、
    /// 1 枚ずつ当てる `MaterialUpgrader.Upgrade` は**どちらも public。**
    /// 手で shader を張り替える案（`_MainTex` -&gt; `_BaseMap` の対応付けを自分で書く）は
    /// **採らない。** 対応表を自作すると Unity の変換規則と二重管理になる。
    ///
    /// ■ 副産物として範囲を絞れる
    /// 一括変換はパスで対象を絞れず**プロジェクト全体**に掛かる。1 枚ずつ当てる形なら
    /// `Assets/ThirdParty/` の中だけに掛けられる。**`Main.unity` を毎回生成する
    /// このプロジェクトでは、外を触られないことのほうが大事。**
    /// 念のため前後のスナップショットも取り、外が変わっていたら例外で止める。
    /// </summary>
    public static class UrpConversion
    {
        /// <summary>変換して**よい**範囲。ここの外は対象にしないし、変わっていたら止める。</summary>
        public const string AllowedRoot = "Assets/ThirdParty/";

        /// <summary>変換後に期待するシェーダ。</summary>
        public const string UrpShaderPrefix = "Universal Render Pipeline/";

        /// <summary>陽性対照で使う一時フォルダ。**処理の最後に消す。**</summary>
        public const string SelfTestFolder = "Assets/ThirdParty/_ConversionSelfTest";

        /// <summary>変換の対象になるマテリアルのパス。**ThirdParty の中だけ。**</summary>
        public static string[] MaterialsInScope() => AssetDatabase
            .FindAssets("t:Material", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Where(p => p.StartsWith(AllowedRoot, StringComparison.Ordinal))
            .Distinct()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        /// <summary>変換を走らせ、shader 名が変わったマテリアルを返す。</summary>
        public static Dictionary<string, string> Run()
        {
            Dictionary<string, string> before = SnapshotShaders();

            // **変換表の取得も時間に含める。** 常設テストにするかの判断材料にするため。
            var watch = Stopwatch.StartNew();
            List<MaterialUpgrader> upgraders =
                MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));

            if (upgraders == null || upgraders.Count == 0)
            {
                throw new InvalidDataException(
                    "URP の変換表が 1 件も取れなかった。変換したつもりになるので止める。");
            }

            string[] scope = MaterialsInScope();

            foreach (string path in scope)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    MaterialUpgrader.Upgrade(material, upgraders, MaterialUpgrader.UpgradeFlags.None);
                }
            }

            AssetDatabase.SaveAssets();
            watch.Stop();

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, string> after = SnapshotShaders();

            var changed = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> entry in after)
            {
                if (before.TryGetValue(entry.Key, out string old) && old != entry.Value)
                {
                    changed[entry.Key] = $"{old} -> {entry.Value}";
                }
            }

            // **範囲外が変わっていないか。** 対象は絞ってあるが、保険として見る。
            string[] outside = changed.Keys
                .Where(p => !p.StartsWith(AllowedRoot, StringComparison.Ordinal))
                .ToArray();

            if (outside.Length > 0)
            {
                throw new InvalidDataException(
                    "**変換が ThirdParty の外のマテリアルを書き換えた:**\n  "
                    + string.Join("\n  ", outside.Select(p => $"{p}: {changed[p]}")));
            }

            var sb = new StringBuilder(string.Format(
                "[UrpConversion] 変換: shader 名が変わったマテリアル {0} 件 / 対象 {1} 件 "
                + "/ 変換表 {2} 件 / {3:F2} 秒",
                changed.Count, scope.Length, upgraders.Count, watch.Elapsed.TotalSeconds));

            foreach (KeyValuePair<string, string> entry in changed)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append(entry.Key);
                sb.Append(": ");
                sb.Append(entry.Value);
            }

            Debug.Log(sb.ToString());
            LogRemaining();
            return changed;
        }

        /// <summary>
        /// **陽性対照 (Step 11-1b)。**
        ///
        /// Hi-Rez は既に URP なので、変換は 0 件で終わる。**それだと「呼んだ」と
        /// 「効いた」の区別がつかない。** bloom が Step 6 から 9 まで一度も効いて
        /// いなかったのと同じ形なので、Standard のマテリアルを 1 枚わざと作って
        /// 変換が実際に効くことを見る。
        ///
        /// あわせて**テクスチャの割当が引き継がれるか**（`_MainTex` -&gt; `_BaseMap`）も見る。
        /// 引き継がれないなら、11-6 で有料アセットを入れたときに手当てが要ると分かる。
        /// </summary>
        public static void RunPositiveControl()
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                throw new InvalidDataException("Standard シェーダが見つからない。陽性対照を作れない");
            }

            EnsureFolder(SelfTestFolder);

            string materialPath = SelfTestFolder + "/PositiveControl.mat";
            Texture2D texture = FindAnyThirdPartyTexture();

            var material = new Material(standard);
            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);
            }

            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[UrpConversion] 陽性対照を作った: {materialPath}\n"
                      + $"  shader {standard.name} / _MainTex "
                      + (texture != null ? texture.name : "（無し）"));

            try
            {
                Run();

                var converted = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (converted == null)
                {
                    throw new InvalidDataException("陽性対照のマテリアルが読めない: " + materialPath);
                }

                string shaderName = converted.shader != null ? converted.shader.name : "<null>";
                Texture baseMap = converted.HasProperty("_BaseMap")
                    ? converted.GetTexture("_BaseMap")
                    : null;

                Debug.Log(string.Format(
                    "[UrpConversion] 陽性対照の結果: shader {0} -> {1} / _BaseMap {2}\n"
                    + "  テクスチャの引き継ぎ: {3}",
                    standard.name, shaderName,
                    baseMap != null ? baseMap.name : "（無し）",
                    texture == null ? "元が無いので判定不能"
                        : baseMap == texture ? "**された**"
                        : "**されなかった**（11-6 で手当てが要る）"));

                if (shaderName != UrpShaderPrefix + "Lit")
                {
                    throw new InvalidDataException(
                        "**変換が効いていない。** Standard のマテリアルが "
                        + shaderName + " のままになっている。"
                        + "変換が 0 件で終わっても「効いた」とは言えない状態。");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(SelfTestFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[UrpConversion] 陽性対照の一時フォルダを消した");
            }
        }

        /// <summary>`Assets/` 以下の全マテリアルの shader 名。**範囲外の巻き添えを見るため全部見る。**</summary>
        public static Dictionary<string, string> SnapshotShaders()
        {
            var map = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || map.ContainsKey(path))
                {
                    continue;
                }

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                map[path] = material != null && material.shader != null
                    ? material.shader.name
                    : "<null>";
            }

            return map;
        }

        /// <summary>ThirdParty 配下で URP でないものを列挙する。</summary>
        static void LogRemaining()
        {
            string[] remaining = SnapshotShaders()
                .Where(kv => kv.Key.StartsWith(AllowedRoot, StringComparison.Ordinal))
                .Where(kv => !kv.Value.StartsWith(UrpShaderPrefix, StringComparison.Ordinal))
                .Select(kv => $"{kv.Key}: {kv.Value}")
                .ToArray();

            Debug.Log(remaining.Length == 0
                ? "[UrpConversion] ThirdParty 配下に URP でないマテリアルは無い"
                : "[UrpConversion] **URP でないマテリアルが残っている:**\n  "
                  + string.Join("\n  ", remaining));
        }

        static Texture2D FindAnyThirdPartyTexture()
        {
            string guid = AssetDatabase
                .FindAssets("t:Texture2D", new[] { CockpitPackage.DestinationRoot })
                .FirstOrDefault();

            return guid == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
