using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// コックピットの `.unitypackage` を取り込む (Step 11-1a)。
    ///
    /// 手順:
    ///   1. 取り込み先フォルダを作る（clone 直後には存在しない）
    ///   2. **一時パッケージを作り、全 `pathname` を取り込み先へ書き換える**
    ///   3. **自己検査。** 全エントリが取り込み先の下に来ているかを確かめ、
    ///      1 件でも外れていたら **取り込みを呼ばずに例外で止める**
    ///   4. 取り込み → ファイル一覧をログ → 削除リスト適用 → 容量ログ
    ///   5. 一時パッケージを消す
    ///
    /// **一時パッケージは %TEMP%（リポジトリの外）に置く。**
    /// `verify/` は `shots/` 以外が追跡対象なので、落ちて残ると 190MB が
    /// 追跡されてしまう。
    /// </summary>
    public static class CockpitImporter
    {
        /// <summary>`run_unity.ps1 -ExtraArgs '-package','&lt;path&gt;'` で上書きできる。</summary>
        public const string PackageArg = "-package";

        /// <summary>
        /// 書き換えた一時パッケージの置き場。**リポジトリの外（%TEMP%）。**
        ///
        /// `verify/` は `shots/` 以外が追跡対象なので、途中で落ちて 190MB が
        /// 残ると次の `git add -A` で public に乗る。`.gitignore` の内側に置く手も
        /// あるが、**そもそもリポジトリに置かないほうが事故の面が小さい。**
        /// リポジトリの外であることは EditMode テストが縛っている。
        /// </summary>
        public static string TempPackagePath => Path.Combine(
            Path.GetTempPath(), "solar-system-explorer-cockpit-remapped.unitypackage");

        /// <summary>SolarSetup から呼ぶ入口。</summary>
        public static void Run()
        {
            string package = ResolvePackagePath();
            Import(package);
        }

        public static string ResolvePackagePath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == PackageArg)
                {
                    return args[i + 1];
                }
            }

            return CockpitPackage.DefaultPackagePath;
        }

        public static void Import(string packagePath)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException(
                    $".unitypackage が見つからない: {packagePath}\n"
                    + "Package Manager の My Assets からダウンロードすること"
                    + "（docs/asset-sources.md §4 の再現手順 1〜2）。", packagePath);
            }

            // ---- 1. 取り込み先を作る ----
            // Assets/ThirdParty は .gitignore の内側なので clone 直後には存在しない。
            foreach (string folder in CockpitPackage.FoldersToCreate)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                    string leaf = Path.GetFileName(folder);
                    AssetDatabase.CreateFolder(parent, leaf);
                    Debug.Log($"[CockpitImporter] 作成: {folder}");
                }
            }

            string temp = TempPackagePath;

            try
            {
                // ---- 2. 取り込み先へ書き換えた一時パッケージ ----
                int rewritten = UnityPackageReader.Rewrite(
                    packagePath, temp, CockpitPackage.MapToDestination);

                Debug.Log($"[CockpitImporter] 宛先を書き換えた: {rewritten} 件\n"
                          + $"  元 : {CockpitPackage.SourceRoot}\n"
                          + $"  先 : {CockpitPackage.DestinationRoot}\n"
                          + $"  一時ファイル: {temp}");

                // ---- 3. 自己検査（ここを通らなければ取り込まない）----
                Verify(temp);

                // ---- 4. 取り込み ----
                ImportSynchronously(temp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                LogImported("削除前");
                long before = FolderSize(CockpitPackage.DestinationRoot);

                DeleteListed();

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                long after = FolderSize(CockpitPackage.DestinationRoot);

                Debug.Log(string.Format(
                    "[CockpitImporter] Assets/ThirdParty の実容量: "
                    + "削除前 {0:F2} MB -> 削除後 {1:F2} MB（{2:F2} MB 減）",
                    before / 1e6, after / 1e6, (before - after) / 1e6));

                VerifyPrefab();
            }
            finally
            {
                // ---- 5. 一時パッケージを消す ----
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                    Debug.Log("[CockpitImporter] 一時パッケージを削除した");
                }
            }
        }

        /// <summary>
        /// **同期取り込みが使えるか。** EditMode テストがこれを見る。
        ///
        /// `AssetDatabase.ImportPackageImmediately` は internal なので
        /// リフレクションで引く。**引けなくなったことをテストで先に知るための窓。**
        /// </summary>
        public static bool SynchronousImportAvailable => FindImmediateImport() != null;

        static MethodInfo FindImmediateImport() => typeof(AssetDatabase).GetMethod(
            "ImportPackageImmediately",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// パッケージを**同期的に**取り込む。
        ///
        /// ■ 公開 API は使えない（このリポジトリでの実測）
        /// 公開されているのは `AssetDatabase.ImportPackage(path, interactive)` だけだが、
        /// **これは非同期で、次のエディタ tick まで完了しない。**
        /// `-batchmode -quit` ではその tick が来ないので**何も起きない。**
        /// TMP Essential Resources の取り込みで同じことを踏んでいる
        /// （`TmpSetup.EnsureImported` のコメント。実測済み）。
        ///
        /// ■ だから internal をリフレクションで引く
        /// `AssetDatabase.ImportPackageImmediately` は
        /// Unity 6000.3.11f1 の `UnityEditor.CoreModule.dll` を読むと
        /// `[public=False assembly=True]`。`PackageUtility` は型ごと internal。
        /// **公開 API で代替できないので、`TmpSetup` と同じ経路に乗る。**
        ///
        /// 「壊れたときに実行時まで分からない」ことへの手当ては 2 つ:
        /// **引けなければ黙って進まず例外で止める**ことと、
        /// **同じ実行の中でプレハブが解決できるか確かめる**こと（`VerifyPrefab`）。
        /// 引けるかどうかは EditMode テストでも見ている。
        /// </summary>
        static void ImportSynchronously(string packagePath)
        {
            MethodInfo immediate = FindImmediateImport();
            if (immediate == null)
            {
                throw new MissingMethodException(
                    "AssetDatabase.ImportPackageImmediately が引けない。"
                    + "公開 API の ImportPackage は batchmode + -quit では完了しない"
                    + "（TmpSetup で実測済み）ので、代わりにはならない。");
            }

            immediate.Invoke(null, new object[] { packagePath });
        }

        /// <summary>
        /// **取り込み前の自己検査。**
        ///
        /// 「危険な窓が無い」を主張ではなくコードで示す。全エントリの pathname が
        /// 取り込み先の下に来ていること、エントリ数が変わっていないことを確かめ、
        /// 1 件でも外れていたら例外で止める。**ここを通るまで
        /// ImportPackage を呼ばない。**
        /// </summary>
        public static void Verify(string remappedPackagePath)
        {
            Dictionary<string, string> paths =
                UnityPackageReader.ReadPathnames(remappedPackagePath);

            if (paths.Count != CockpitPackage.ExpectedEntryCount)
            {
                throw new InvalidDataException(
                    $"エントリ数が変わった: {paths.Count} "
                    + $"(期待 {CockpitPackage.ExpectedEntryCount})。取り込みを中止する。");
            }

            string prefix = CockpitPackage.DestinationRoot;
            var strays = paths
                .Where(kv => !kv.Value.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => $"{kv.Key} -> {kv.Value}")
                .ToArray();

            if (strays.Length > 0)
            {
                throw new InvalidDataException(
                    $"**{strays.Length} 件が取り込み先の外を指している。取り込みを中止する。**\n"
                    + "そのまま取り込むと .gitignore が効かず、追跡対象になる。\n  "
                    + string.Join("\n  ", strays.Take(10)));
            }

            Debug.Log($"[CockpitImporter] 自己検査 OK: {paths.Count} 件すべてが "
                      + $"{prefix}/ の下を指している");
        }

        static void DeleteListed()
        {
            var missing = new List<string>();
            var deleted = new List<string>();

            foreach (string relative in CockpitPackage.DeleteAfterImport)
            {
                string path = CockpitPackage.DeletePath(relative);

                // **再取り込みしても同じ結果になること。** 既に無ければ何もしない。
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null
                    && !File.Exists(path) && !AssetDatabase.IsValidFolder(path))
                {
                    missing.Add(relative);
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    deleted.Add(relative);
                }
                else
                {
                    missing.Add(relative + "（削除に失敗）");
                }
            }

            Debug.Log($"[CockpitImporter] 削除リスト: {deleted.Count} 件を削除"
                      + (missing.Count > 0 ? $" / {missing.Count} 件は既に無い" : string.Empty)
                      + "\n  " + string.Join("\n  ", deleted));
        }

        /// <summary>
        /// 取り込まれたものを一覧にする。
        ///
        /// ■ **パッケージのエントリ数とそろえて数える。**
        /// `AssetDatabase.FindAssets` は**探索の起点にしたフォルダ自身を返さない。**
        /// パッケージ側の 178 エントリには起点（`…/HiRezSpaceshipsCreatorFree`）も
        /// 1 件として入っているので、起点を足さないと**必ず 1 件少なく出る。**
        /// 数え方の違いでしかないが、ログだけを読む人には**取り込み漏れに見える。**
        /// </summary>
        static void LogImported(string label)
        {
            string[] all = AssetDatabase.FindAssets(string.Empty,
                                                    new[] { CockpitPackage.DestinationRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // 起点のフォルダ自身。`FindAssets` の結果には出てこない。
            int root = AssetDatabase.IsValidFolder(CockpitPackage.DestinationRoot) ? 1 : 0;
            int total = all.Length + root;

            var sb = new StringBuilder(string.Format(
                "[CockpitImporter] 取り込み結果（{0}）: {1} 件"
                + "（パッケージのエントリ {2} 件と{3}）"
                + "  内訳: 起点の下で見つかった {4} 件 + 起点 {5} 自身 {6} 件。"
                + "FindAssets は起点のフォルダ自身を返さないので、"
                + "足さないと数え方の違いで 1 件ずれる。",
                label, total, CockpitPackage.ExpectedEntryCount,
                total == CockpitPackage.ExpectedEntryCount ? "一致" : "**不一致**",
                all.Length, CockpitPackage.DestinationRoot, root));
            foreach (string p in all)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append(p.Replace(CockpitPackage.DestinationRoot + "/", string.Empty));
            }

            Debug.Log(sb.ToString());
        }

        static long FolderSize(string assetFolder)
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetFolder));
            if (!Directory.Exists(full))
            {
                return 0;
            }

            long total = 0;
            foreach (string f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
            }

            return total;
        }

        /// <summary>定数の GUID が実際に解決できるか。**定数と実態の一致を縛る。**</summary>
        static void VerifyPrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(CockpitPackage.CockpitWithInteriorGuid);
            var prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                throw new InvalidDataException(
                    $"内装コックピットのプレハブが解決できない "
                    + $"(GUID {CockpitPackage.CockpitWithInteriorGuid})。取り込みが不完全。");
            }

            Debug.Log($"[CockpitImporter] 内装プレハブ: {path}\n"
                      + $"  レンダラー {prefab.GetComponentsInChildren<Renderer>(true).Length} 個");
        }
    }
}
