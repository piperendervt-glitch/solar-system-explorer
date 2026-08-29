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
    /// **取り込んだモデルの単位を実測する (Step 13-3 コミット1)。**
    ///
    /// ■ なぜ要るか
    /// 13-2 が出した bbox は「プレハブのローカル単位」としか分かっておらず、
    /// **メートルである保証が無い。** Scale / PortStandoff / RequestRange は
    /// この数字の上に載るので、単位を取り違えると以降が全部ずれる。
    ///
    /// ■ 何を根拠にするか（推測しない）
    ///   1. FBX 自身が申告する `UnitScaleFactor`（GlobalSettings / cm 単位）
    ///   2. `ModelImporter` の `useFileScale` / `globalScale` / `bakeAxisConversion`
    ///   3. 取り込まれたメッシュの bounds（**インポータ適用後**の値）
    ///   4. プレハブの Transform 階層に掛かっている localScale
    /// この 4 つを並べて、掛け算が合うことを見せる。
    ///
    /// **配線はしない。数字を出すだけ。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.MeasureModelUnits
    /// </summary>
    public static class ModelUnitProbe
    {
        /// <summary>ステーション（Cobble Games / 13-2 で取り込み）。</summary>
        const string StationPrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        /// <summary>コックピット（Hi-Rez / Demo 3 で取り込み）。</summary>
        const string CockpitPrefabGuid = "54e1b562c3fea284f8a0ec8cdc70057c";

        /// <summary>Demo 3 で確定した目の位置（プレハブ原点基準・メートル）。</summary>
        static readonly Vector3 EyeLocal = new Vector3(0.0f, 0.429f, -1.436f);

        /// <summary>参考表で振る Scale。**選ばない。並べるだけ。**</summary>
        static readonly double[] CandidateScales = { 0.001, 0.002, 0.004, 0.008 };

        // ---- 現行の箱（対比用。StationCatalog / SolarSystemModel の値）----
        const double BoxRadiusUnits = 0.25;
        const double BoxPortStandoffUnits = 0.3;
        const double BoxRequestRangeUnits = 20.0;

        /// <summary>1 unit = 1 km（`CLAUDE.md` §5）。</summary>
        const double MetersPerUnit = 1000.0;

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== モデルの単位の実測 (Step 13-3 コミット1) ==");
            sb.AppendLine("**配線はしていない。数字を出すだけ。**");
            sb.AppendLine("この世界は 1 unit = 1 km。コックピットは 1000 倍の描画空間");
            sb.AppendLine("（そこでは 1 m = 1 unit）にあるが、**それはモデル自身の寸法とは別の話。**");
            sb.AppendLine();

            Measurement station = Measure(sb, "ステーション (Cobble Games)", StationPrefabGuid);
            Measurement cockpit = Measure(sb, "コックピット (Hi-Rez)", CockpitPrefabGuid);

            ReportUnitConclusion(sb, station, cockpit);
            ReportEye(sb, cockpit);
            ReportScaleTable(sb, station, cockpit);

            string outPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "verify", "model-units.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log("[ModelUnits] 書き出した: " + outPath);
        }

        // ---- 実測 ----

        public sealed class Measurement
        {
            public bool Available;
            public string PrefabPath = string.Empty;
            public Vector3 RootLocalScale = Vector3.one;
            public Bounds Bounds;
            public bool HasBounds;
            /// <summary>ピボット（プレハブ原点）から bbox の最遠の角までの距離。</summary>
            public float PivotRadius;
            /// <summary>bbox 中心から角までの距離（外接球の半径）。</summary>
            public float CenterRadius;

            // ---- 単位の導出に使う実測値 ----
            public string ModelPath = string.Empty;
            public bool HasImporter;
            public bool UseFileScale;
            public float GlobalScale;
            public float FileScale;
            public bool HasFbxUnit;
            public double FbxUnitScaleFactor;

            /// <summary>子の累積倍率のうち**最も多く現れた値**（X 成分）。</summary>
            public float BaseChildScale = 1f;
            public int BaseChildScaleCount;
            public int RendererCount;
        }

        static Measurement Measure(StringBuilder sb, string label, string prefabGuid)
        {
            var m = new Measurement();

            sb.AppendLine("############################################################");
            sb.AppendLine("## " + label);
            sb.AppendLine("############################################################");
            sb.AppendLine();

            string path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine("**取り込まれていない**（GUID " + prefabGuid + " が解決できない）。");
                sb.AppendLine("clone 直後は正常。この節の数字は取れていない。");
                sb.AppendLine();
                return m;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                sb.AppendLine("**GUID は解決できたがプレハブとして読めない**: " + path);
                sb.AppendLine();
                return m;
            }

            m.Available = true;
            m.PrefabPath = path;
            m.RootLocalScale = prefab.transform.localScale;

            sb.AppendLine("プレハブ : " + path);
            sb.AppendLine("ルート Transform の localScale : " + F(prefab.transform.localScale));
            sb.AppendLine();

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);

            ReportImporters(sb, renderers, m);
            ReportHierarchy(sb, prefab, renderers, m);

            return m;
        }

        // ---- (1) FBX とインポータ ----

        static void ReportImporters(StringBuilder sb, Renderer[] renderers, Measurement m)
        {
            var models = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Renderer r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                string p = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (!string.IsNullOrEmpty(p))
                {
                    models.Add(p);
                }
            }

            sb.AppendLine("-- (1) 元モデルとインポータの設定 --");
            if (models.Count == 0)
            {
                sb.AppendLine("  メッシュの元アセットが引けない");
                sb.AppendLine();
                return;
            }

            foreach (string p in models)
            {
                sb.AppendLine("  " + p);

                var importer = AssetImporter.GetAtPath(p) as ModelImporter;
                if (importer == null)
                {
                    sb.AppendLine("    ModelImporter ではない（" +
                                  (AssetImporter.GetAtPath(p) == null
                                       ? "importer が引けない"
                                       : AssetImporter.GetAtPath(p).GetType().Name) + "）");
                    continue;
                }

                if (!m.HasImporter)
                {
                    m.HasImporter = true;
                    m.ModelPath = p;
                    m.UseFileScale = importer.useFileScale;
                    m.GlobalScale = importer.globalScale;
                    m.FileScale = importer.fileScale;
                }

                sb.AppendLine("    useFileScale (Convert Units) : " + importer.useFileScale);
                sb.AppendLine("    globalScale  (Scale Factor)  : "
                              + importer.globalScale.ToString("R", CultureInfo.InvariantCulture));
                sb.AppendLine("    bakeAxisConversion           : " + importer.bakeAxisConversion);
                sb.AppendLine("    fileScale (ファイルが申告する倍率 / 読み取り専用) : "
                              + importer.fileScale.ToString("R", CultureInfo.InvariantCulture));
                sb.AppendLine("    -> インポータがメッシュに掛ける実効倍率 = "
                              + EffectiveImportScale(importer).ToString("R", CultureInfo.InvariantCulture)
                              + "  (useFileScale なら fileScale x globalScale、"
                              + "そうでなければ globalScale)");

                ReportFbxUnitScaleFactor(sb, p, m);
            }

            sb.AppendLine();
        }

        static float EffectiveImportScale(ModelImporter importer) =>
            importer.useFileScale ? importer.fileScale * importer.globalScale : importer.globalScale;

        /// <summary>
        /// FBX (Kaydara binary) の GlobalSettings から `UnitScaleFactor` を読む。
        /// **FBX の `UnitScaleFactor` は「1 ファイル単位が何センチか」。**
        /// 1.0 = cm / 100.0 = m / 2.54 = inch。
        /// **推測ではなくファイルが申告している値。**
        /// </summary>
        static void ReportFbxUnitScaleFactor(StringBuilder sb, string assetPath, Measurement m)
        {
            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);

            if (!File.Exists(absolute) ||
                !Path.GetExtension(absolute).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("    FBX ではないので UnitScaleFactor は読んでいない");
                return;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(absolute);
            }
            catch (Exception e)
            {
                sb.AppendLine("    FBX を読めない: " + e.Message);
                return;
            }

            if (!TryReadFbxDouble(data, "UnitScaleFactor", out double unit))
            {
                sb.AppendLine("    **UnitScaleFactor が読めない**"
                              + "（ASCII FBX か、格納の形が想定と違う）");
                return;
            }

            if (!m.HasFbxUnit)
            {
                m.HasFbxUnit = true;
                m.FbxUnitScaleFactor = unit;
            }

            sb.AppendLine("    FBX GlobalSettings UnitScaleFactor : "
                          + unit.ToString("R", CultureInfo.InvariantCulture)
                          + "  = 1 ファイル単位が " + unit.ToString("R", CultureInfo.InvariantCulture)
                          + " cm（1.0 = cm / 100.0 = m）");

            if (TryReadFbxDouble(data, "OriginalUnitScaleFactor", out double original))
            {
                sb.AppendLine("    OriginalUnitScaleFactor            : "
                              + original.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// バイナリ FBX のプロパティ列から `<name>` に続く最初の double を拾う。
        /// 形は `S<len>name S..'double' S..'Number' S..'' 'D' <8 bytes>`。
        /// **見つからなければ false。嘘の値を返さない。**
        /// </summary>
        static bool TryReadFbxDouble(byte[] data, string name, out double value)
        {
            value = 0.0;
            byte[] key = Encoding.ASCII.GetBytes(name);

            for (int i = 0; i + key.Length < data.Length; i++)
            {
                bool hit = true;
                for (int k = 0; k < key.Length; k++)
                {
                    if (data[i + k] != key[k])
                    {
                        hit = false;
                        break;
                    }
                }

                if (!hit)
                {
                    continue;
                }

                // 直後 128 バイト以内の 'D'（double のタグ）を探す。
                int limit = Math.Min(data.Length - 9, i + key.Length + 128);
                for (int j = i + key.Length; j < limit; j++)
                {
                    if (data[j] == (byte)'D')
                    {
                        value = BitConverter.ToDouble(data, j + 1);
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        // ---- (2) Transform 階層と bbox ----

        static void ReportHierarchy(StringBuilder sb, GameObject prefab,
                                    Renderer[] renderers, Measurement m)
        {
            sb.AppendLine("-- (2) Transform 階層に掛かっている倍率と、生メッシュの寸法 --");
            sb.AppendLine("  **累積倍率はルートの localScale を含まない**"
                          + "（プレハブのローカル空間で測るため）。");
            sb.AppendLine();
            sb.AppendLine("  レンダラー / 累積倍率 / 生メッシュの bounds.size [メッシュ単位]"
                          + " / 掛けた後 [プレハブ単位]");

            bool first = true;
            var bounds = new Bounds();
            var scaleHistogram = new Dictionary<float, int>();

            foreach (Renderer r in renderers.OrderBy(r => r.name, StringComparer.Ordinal))
            {
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    sb.AppendLine("  " + PathOf(prefab, r.transform) + " / メッシュ無し");
                    continue;
                }

                Vector3 accumulated = AccumulatedScale(prefab, r.transform);
                float key = (float)Math.Round(accumulated.x, 3);
                scaleHistogram[key] = scaleHistogram.TryGetValue(key, out int n) ? n + 1 : 1;
                m.RendererCount++;

                Vector3 raw = mesh.bounds.size;
                var scaled = new Vector3(raw.x * accumulated.x,
                                         raw.y * accumulated.y,
                                         raw.z * accumulated.z);

                sb.AppendLine("  " + PathOf(prefab, r.transform)
                              + " / " + F(accumulated)
                              + " / " + F(raw)
                              + " / " + F(scaled));

                Matrix4x4 toPrefab = LocalMatrix(prefab, r.transform);
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

            sb.AppendLine();

            if (scaleHistogram.Count > 0)
            {
                KeyValuePair<float, int> top = scaleHistogram
                    .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
                m.BaseChildScale = top.Key;
                m.BaseChildScaleCount = top.Value;

                sb.AppendLine("  累積倍率の内訳（値: 件数）: "
                              + string.Join(" / ", scaleHistogram
                                  .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                                  .Select(kv => kv.Key.ToString("R", CultureInfo.InvariantCulture)
                                                + ": " + kv.Value).ToArray()));
                sb.AppendLine("  **最も多い倍率** : "
                              + m.BaseChildScale.ToString("R", CultureInfo.InvariantCulture)
                              + "（" + m.BaseChildScaleCount + " / " + m.RendererCount + " 件）");
                sb.AppendLine("  **単一の実効倍率は存在しない**（作者がモジュールごとに振っている）。"
                              + "以下の換算は最も多い倍率を代表値として使う。");
                sb.AppendLine();
            }

            if (first)
            {
                sb.AppendLine("  **頂点が 1 つも取れなかった。** bbox を出していない。");
                sb.AppendLine();
                return;
            }

            m.Bounds = bounds;
            m.HasBounds = true;
            m.PivotRadius = MaxCornerDistance(bounds, Vector3.zero);
            m.CenterRadius = MaxCornerDistance(bounds, bounds.center);

            sb.AppendLine("-- (3) プレハブのローカル空間での bbox --");
            sb.AppendLine("  size (X, Y, Z)          : " + F(bounds.size));
            sb.AppendLine("  ピボットからの中心オフセット : " + F(bounds.center));
            sb.AppendLine("  min / max               : " + F(bounds.min) + " / " + F(bounds.max));
            sb.AppendLine("  **全幅（X の寸法）**      : "
                          + bounds.size.x.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  最大の寸法              : "
                          + Math.Max(bounds.size.x, Math.Max(bounds.size.y, bounds.size.z))
                                .ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  外接球の半径（bbox 中心基準）: "
                          + m.CenterRadius.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  外接球の半径（**ピボット基準**）: "
                          + m.PivotRadius.ToString("F4", CultureInfo.InvariantCulture)
                          + "   ← ステーションはピボットに置かれるので、"
                          + "MinStandoff に効くのはこちら");
            sb.AppendLine();
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

        static Vector3 AccumulatedScale(GameObject prefab, Transform t)
        {
            var s = Vector3.one;
            Transform current = t;
            while (current != null && current != prefab.transform)
            {
                Vector3 l = current.localScale;
                s = new Vector3(s.x * l.x, s.y * l.y, s.z * l.z);
                current = current.parent;
            }

            return s;
        }

        /// <summary>レンダラーのローカル座標 -> プレハブ原点座標（ルートの scale は含まない）。</summary>
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

        static float MaxCornerDistance(Bounds b, Vector3 from)
        {
            float best = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                best = Mathf.Max(best, Vector3.Distance(corner, from));
            }

            return best;
        }

        // ---- 単位の導出（どの設定値からそう言えるのか）----

        static void ReportUnitConclusion(StringBuilder sb, Measurement station, Measurement cockpit)
        {
            sb.AppendLine("############################################################");
            sb.AppendLine("## 単位の導出（**どの設定値からそう言えるのか**）");
            sb.AppendLine("############################################################");
            sb.AppendLine();

            if (!station.HasImporter || !cockpit.HasImporter)
            {
                sb.AppendLine("**両方のインポータが読めていないので導出しない。**");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("-- 2 つのアセットの設定は同一 --");
            sb.AppendLine("  項目 / ステーション (Cobble) / コックピット (Hi-Rez)");
            sb.AppendLine("  FBX UnitScaleFactor : "
                          + Show(station.HasFbxUnit, station.FbxUnitScaleFactor) + " / "
                          + Show(cockpit.HasFbxUnit, cockpit.FbxUnitScaleFactor));
            sb.AppendLine("  useFileScale        : " + station.UseFileScale
                          + " / " + cockpit.UseFileScale);
            sb.AppendLine("  globalScale         : "
                          + station.GlobalScale.ToString("R", CultureInfo.InvariantCulture) + " / "
                          + cockpit.GlobalScale.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  fileScale           : "
                          + station.FileScale.ToString("R", CultureInfo.InvariantCulture) + " / "
                          + cockpit.FileScale.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  プレハブ側の最も多い倍率 : "
                          + station.BaseChildScale.ToString("R", CultureInfo.InvariantCulture) + " / "
                          + cockpit.BaseChildScale.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  プレハブのルート localScale : " + F(station.RootLocalScale)
                          + " / " + F(cockpit.RootLocalScale));
            sb.AppendLine();

            sb.AppendLine("-- 導出（掛け算） --");
            sb.AppendLine("  FBX の UnitScaleFactor = 1 は「1 ファイル単位 = 1 cm」の申告。");
            sb.AppendLine("  useFileScale = True なので Unity は fileScale = 0.01 を掛けて");
            sb.AppendLine("  メッシュをメートルに直す。**取り込み後のメッシュはメートル。**");
            sb.AppendLine();
            sb.AppendLine("  ステーション : メッシュ [m] x プレハブ倍率 "
                          + station.BaseChildScale.ToString("R", CultureInfo.InvariantCulture)
                          + " -> プレハブ単位");
            sb.AppendLine("  コックピット : メッシュ [m] x プレハブ倍率 "
                          + cockpit.BaseChildScale.ToString("R", CultureInfo.InvariantCulture)
                          + " -> プレハブ単位");
            sb.AppendLine();
            sb.AppendLine("  ルートの localScale は両方とも 1 なので、"
                          + "**プレハブをシーンへ scale 1 で置いたときの寸法 = 上の bbox。**");
            sb.AppendLine("  Unity の慣習では 1 unit = 1 m なので、その bbox の数値が"
                          + "そのままメートルになる。");
            sb.AppendLine();

            sb.AppendLine("-- 裏取り: 同じ設定のコックピットで寸法が実物大か --");
            sb.AppendLine("  **コックピットは Demo 3 で実機に映しており、人が座る寸法として"
                          + "成立していることが分かっている。** そのアセットが");
            sb.AppendLine("  ステーションと同一の FBX 単位申告・同一のインポータ設定で、");
            sb.AppendLine("  プレハブ倍率だけが違う。だから同じ換算が両方に効く。");
            sb.AppendLine();
            sb.AppendLine("  コックピットの実測（プレハブ単位 = メートルと読んだとき）:");
            AppendMeshSize(sb, cockpit, "Cockpit3_Body", "機体");
            AppendMeshSize(sb, cockpit, "CockpitEquipments_Seat", "座席");
            AppendMeshSize(sb, cockpit, "CockpitEquipments_Button6-1", "ボタン");
            sb.AppendLine();

            sb.AppendLine("-- 結論 --");
            if (station.HasBounds)
            {
                Vector3 s = station.Bounds.size;
                sb.AppendLine("  **ステーションのプレハブ単位は Unity のメートル。**");
                sb.AppendLine("  scale 1 で置いたときの実寸 = "
                              + s.x.ToString("F2", CultureInfo.InvariantCulture) + " x "
                              + s.y.ToString("F2", CultureInfo.InvariantCulture) + " x "
                              + s.z.ToString("F2", CultureInfo.InvariantCulture) + " m");
            }

            sb.AppendLine();
            sb.AppendLine("-- **この導出が抱えている食い違い（隠さない）** --");
            sb.AppendLine("  FBX の申告（UnitScaleFactor = 1 = cm）を額面どおりに読むと、");
            sb.AppendLine("  ステーションは 41.94 **cm** になる。プレハブの x100 は");
            sb.AppendLine("  インポータの x0.01 をちょうど打ち消しており、"
                          + "**結果としてプレハブは FBX の生の数値をそのまま再現している。**");
            sb.AppendLine("  つまり作者は「生の数値をメートルとして扱う」つもりで作り、");
            sb.AppendLine("  FBX の単位申告のほうが実態と合っていない（書き出し時のよくある不一致）。");
            sb.AppendLine();
            sb.AppendLine("  **確定できるのは「scale 1 で置くと Unity 単位で何になるか」まで。**");
            sb.AppendLine("  それがメートルであることは Unity の慣習と、"
                          + "同設定のコックピットが実物大であることから来ている。");
            sb.AppendLine("  **作者が 41.94 m を意図した、という一次資料は持っていない**"
                          + "（デモシーンは 13-2 で取り込まずに落とした）。");
            sb.AppendLine();
        }

        static string Show(bool has, double v) =>
            has ? v.ToString("R", CultureInfo.InvariantCulture) : "（読めない）";

        static void AppendMeshSize(StringBuilder sb, Measurement m, string rendererName, string label)
        {
            if (string.IsNullOrEmpty(m.PrefabPath))
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.PrefabPath);
            if (prefab == null)
            {
                return;
            }

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!string.Equals(r.name, rendererName, StringComparison.Ordinal))
                {
                    continue;
                }

                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    return;
                }

                Vector3 acc = AccumulatedScale(prefab, r.transform);
                Vector3 raw = filter.sharedMesh.bounds.size;
                sb.AppendLine("    " + label + " (" + rendererName + ") : "
                              + (raw.x * acc.x).ToString("F4", CultureInfo.InvariantCulture) + " x "
                              + (raw.y * acc.y).ToString("F4", CultureInfo.InvariantCulture) + " x "
                              + (raw.z * acc.z).ToString("F4", CultureInfo.InvariantCulture) + " m");
                return;
            }
        }

        // ---- 目の位置との関係 ----

        static void ReportEye(StringBuilder sb, Measurement cockpit)
        {
            sb.AppendLine("############################################################");
            sb.AppendLine("## 目の位置と機体の関係（Demo 3 で確定した EyeLocal）");
            sb.AppendLine("############################################################");
            sb.AppendLine();
            sb.AppendLine("EyeLocal (CockpitDefinition.HiRezSample / プレハブ原点基準) : "
                          + F(EyeLocal));

            if (!cockpit.HasBounds)
            {
                sb.AppendLine("**コックピットの bbox が取れていないので、関係を出せない。**");
                sb.AppendLine();
                return;
            }

            Bounds b = cockpit.Bounds;
            sb.AppendLine();
            sb.AppendLine("  機体 bbox min / max : " + F(b.min) + " / " + F(b.max));
            sb.AppendLine("  目が bbox の中で占める位置（0 = min 側 / 1 = max 側）:");
            sb.AppendLine("    X : " + Frac(EyeLocal.x, b.min.x, b.max.x));
            sb.AppendLine("    Y : " + Frac(EyeLocal.y, b.min.y, b.max.y));
            sb.AppendLine("    Z : " + Frac(EyeLocal.z, b.min.z, b.max.z));
            sb.AppendLine();
            sb.AppendLine("  目から機首側（+Z / EyeForward）の端まで : "
                          + (b.max.z - EyeLocal.z).ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  目から機尾側（-Z）の端まで             : "
                          + (EyeLocal.z - b.min.z).ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  目から上（+Y）の端まで                 : "
                          + (b.max.y - EyeLocal.y).ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  目から下（-Y）の端まで                 : "
                          + (EyeLocal.y - b.min.y).ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        static string Frac(float v, float min, float max)
        {
            if (Mathf.Approximately(max, min))
            {
                return "---（寸法 0）";
            }

            float f = (v - min) / (max - min);
            return v.ToString("F4", CultureInfo.InvariantCulture)
                   + "  (" + f.ToString("F4", CultureInfo.InvariantCulture) + ")";
        }

        // ---- 参考の数表（Scale は選ばない）----

        static void ReportScaleTable(StringBuilder sb, Measurement station, Measurement cockpit)
        {
            sb.AppendLine("############################################################");
            sb.AppendLine("## 参考: Scale を振ったときの数字（**選ばない。並べるだけ**）");
            sb.AppendLine("############################################################");
            sb.AppendLine();

            if (!station.HasBounds)
            {
                sb.AppendLine("**ステーションの bbox が取れていないので出せない。**");
                sb.AppendLine();
                return;
            }

            Vector3 size = station.Bounds.size;
            double pivotRadius = station.PivotRadius;

            sb.AppendLine("入力（プレハブのローカル単位）:");
            sb.AppendLine("  bbox size            : " + F(size));
            sb.AppendLine("  ピボット基準の外接球半径 : "
                          + pivotRadius.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("-- 実寸と MinStandoff --");
            sb.AppendLine("  Scale | 実寸 X x Y x Z [m] | 半径(ピボット基準) [units] "
                          + "| 同 [m] | MinStandoff = 半径 + 0.01 [units]");
            foreach (double s in CandidateScales)
            {
                // Scale はプレハブ単位 -> units の倍率。1 unit = 1 km なので m は x1000。
                double mx = size.x * s * MetersPerUnit;
                double my = size.y * s * MetersPerUnit;
                double mz = size.z * s * MetersPerUnit;
                double rUnits = pivotRadius * s;
                double rMeters = rUnits * MetersPerUnit;

                sb.AppendLine("  " + s.ToString("0.000", CultureInfo.InvariantCulture)
                              + " | " + mx.ToString("F1", CultureInfo.InvariantCulture)
                              + " x " + my.ToString("F1", CultureInfo.InvariantCulture)
                              + " x " + mz.ToString("F1", CultureInfo.InvariantCulture)
                              + " | " + rUnits.ToString("F5", CultureInfo.InvariantCulture)
                              + " | " + rMeters.ToString("F1", CultureInfo.InvariantCulture)
                              + " | " + (rUnits + 0.01).ToString("F5", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine("  **前提**: Scale は「プレハブのローカル単位 -> units」の倍率。");
            sb.AppendLine("  プレハブのローカル単位が 1 m なら、Scale 0.001 が実寸配置になる");
            sb.AppendLine("  （1 unit = 1 km なので 1 m = 0.001 units）。");
            sb.AppendLine();

            ReportPortTable(sb, size, cockpit);
            ReportBoxComparison(sb, size, pivotRadius);
        }

        static void ReportPortTable(StringBuilder sb, Vector3 size, Measurement cockpit)
        {
            sb.AppendLine("-- ドッキング口の目安（**実際の口はまだ特定していない**）--");
            sb.AppendLine("  **これは「bbox の最も小さい面」を口と見なしただけの目安で、");
            sb.AppendLine("  実物のドッキング口ではない。** 実際の口は 13-3 でプレハブの");
            sb.AppendLine("  形を見て特定する。ここの比を根拠に Scale を決めないこと。");
            sb.AppendLine();

            float[] dims = { size.x, size.y, size.z };
            Array.Sort(dims);
            float faceSmall = dims[0];
            float faceLarge = dims[1];

            sb.AppendLine("  bbox の 3 辺を昇順に : "
                          + dims[0].ToString("F4", CultureInfo.InvariantCulture) + " / "
                          + dims[1].ToString("F4", CultureInfo.InvariantCulture) + " / "
                          + dims[2].ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  最小の面 = 短い 2 辺 : "
                          + faceSmall.ToString("F4", CultureInfo.InvariantCulture) + " x "
                          + faceLarge.ToString("F4", CultureInfo.InvariantCulture)
                          + " [プレハブ単位]");
            sb.AppendLine();

            if (!cockpit.HasBounds)
            {
                sb.AppendLine("  **船の全幅が取れていないので比を出せない。**");
                sb.AppendLine();
                return;
            }

            double shipWidth = cockpit.Bounds.size.x;
            sb.AppendLine("  船の全幅（コックピット bbox の X / メートル） : "
                          + shipWidth.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("  物差し（計画書 13-3）: 開口が船の全幅の 1.5〜3 倍");
            sb.AppendLine("  -> **物差しを満たす開口の実寸は "
                          + (shipWidth * 1.5).ToString("F3", CultureInfo.InvariantCulture)
                          + " 〜 " + (shipWidth * 3.0).ToString("F3", CultureInfo.InvariantCulture)
                          + " m。**");
            sb.AppendLine();
            sb.AppendLine("  Scale | 開口 [m] (短辺 x 長辺) | 短辺 / 全幅 | 長辺 / 全幅 | 1.5〜3 に入るか");

            foreach (double s in CandidateScales)
            {
                double small = faceSmall * s * MetersPerUnit;
                double large = faceLarge * s * MetersPerUnit;
                double rSmall = small / shipWidth;
                double rLarge = large / shipWidth;

                string verdict = InRange(rSmall) ? "短辺が入る"
                                 : InRange(rLarge) ? "長辺だけ入る"
                                 : "入らない";

                sb.AppendLine("  " + s.ToString("0.000", CultureInfo.InvariantCulture)
                              + " | " + small.ToString("F2", CultureInfo.InvariantCulture)
                              + " x " + large.ToString("F2", CultureInfo.InvariantCulture)
                              + " | " + rSmall.ToString("F3", CultureInfo.InvariantCulture)
                              + " | " + rLarge.ToString("F3", CultureInfo.InvariantCulture)
                              + " | " + verdict);
            }

            sb.AppendLine();
            sb.AppendLine("  **どの Scale でも入らない。** bbox の最小面は構造物の断面であって");
            sb.AppendLine("  ドッキング口ではないので、これは想定どおりの結果で、"
                          + "Scale の可否を何も言っていない。");
            sb.AppendLine();
            sb.AppendLine("  参考: bbox の最小面が物差しに入るには Scale が "
                          + (shipWidth * 1.5 / (faceSmall * MetersPerUnit))
                                .ToString("0.0000000", CultureInfo.InvariantCulture)
                          + " 〜 "
                          + (shipWidth * 3.0 / (faceSmall * MetersPerUnit))
                                .ToString("0.0000000", CultureInfo.InvariantCulture)
                          + " でなければならない");
            sb.AppendLine("  （そのとき構造物全体は "
                          + (Math.Max(dims[2], 0f) * (shipWidth * 1.5 / (faceSmall * MetersPerUnit))
                             * MetersPerUnit).ToString("F2", CultureInfo.InvariantCulture)
                          + " 〜 "
                          + (Math.Max(dims[2], 0f) * (shipWidth * 3.0 / (faceSmall * MetersPerUnit))
                             * MetersPerUnit).ToString("F2", CultureInfo.InvariantCulture)
                          + " m になる）。");
            sb.AppendLine("  **実際の口は構造物のごく一部なので、口の実寸が "
                          + (shipWidth * 1.5).ToString("F2", CultureInfo.InvariantCulture)
                          + " 〜 " + (shipWidth * 3.0).ToString("F2", CultureInfo.InvariantCulture)
                          + " m になる Scale を、口を特定してから逆算すること。**");
            sb.AppendLine();
        }

        static bool InRange(double r) => r >= 1.5 && r <= 3.0;

        static void ReportBoxComparison(StringBuilder sb, Vector3 size, double pivotRadius)
        {
            sb.AppendLine("-- 現行の箱との対比 --");
            sb.AppendLine("  現行の箱 : 半径 " + BoxRadiusUnits.ToString("F3", CultureInfo.InvariantCulture)
                          + " units (= " + (BoxRadiusUnits * MetersPerUnit).ToString("F0", CultureInfo.InvariantCulture)
                          + " m) / PortStandoff " + BoxPortStandoffUnits.ToString("F3", CultureInfo.InvariantCulture)
                          + " units / RequestRange " + BoxRequestRangeUnits.ToString("F1", CultureInfo.InvariantCulture)
                          + " units");
            sb.AppendLine();
            sb.AppendLine("  Scale | 半径 [units] | 箱の半径に対する比 | "
                          + "PortStandoff を箱と同じ 1.2 倍にした値 [units]");

            foreach (double s in CandidateScales)
            {
                double rUnits = pivotRadius * s;
                sb.AppendLine("  " + s.ToString("0.000", CultureInfo.InvariantCulture)
                              + " | " + rUnits.ToString("F5", CultureInfo.InvariantCulture)
                              + " | " + (rUnits / BoxRadiusUnits).ToString("F4", CultureInfo.InvariantCulture)
                              + " | " + (rUnits * 1.2).ToString("F5", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine("  **RequestRange 20 units は現行の箱の半径の 80 倍。**");
            sb.AppendLine("  半径が小さくなると、要求できる距離だけが相対的に遠くなる");
            sb.AppendLine("  （13-1a の宿題「AP の到着半径と RequestRange の関係」に直結）。");
            sb.AppendLine();
        }

        static string F(Vector3 v) =>
            "(" + v.x.ToString("F4", CultureInfo.InvariantCulture)
                + ", " + v.y.ToString("F4", CultureInfo.InvariantCulture)
                + ", " + v.z.ToString("F4", CultureInfo.InvariantCulture) + ")";
    }
}
