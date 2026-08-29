using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **ステーションアセットを取り込む (Step 13-2)。経路 C。**
    ///
    /// 手順:
    ///   1. 外側から**入れ子の `URP/URP.unitypackage` だけ**を作業ディレクトリへ取り出す
    ///      （外側の Built-in 一式も HDRP も `Assets/` に一切書かない）
    ///   2. 取り出したものの `pathname` を取り込み先へ書き換え、除外分を落とす
    ///   3. 取り込み前に自己検査（全 pathname が取り込み先の下に来ているか）
    ///   4. `AssetDatabase.ImportPackageImmediately`（internal / リフレクション）
    ///   5. 一時ファイルを消す（**リポジトリ配下には最初から置かない**）
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportStation
    /// </summary>
    public static class StationImporter
    {
        public const string PackageArg = "-package";

        /// <summary>
        /// 一時ファイルの置き場。**リポジトリの外**（`Temp/` でもなく OS の temp）。
        /// 取り込み中に落ちても追跡対象の場所に 2.4 GB が残らない。
        /// </summary>
        public static string TempDirectory => Path.Combine(Path.GetTempPath(), "solar-station-import");

        static string NestedPath => Path.Combine(TempDirectory, "Cobble-URP.unitypackage");

        static string RemappedPath => Path.Combine(TempDirectory, "Cobble-URP-remapped.unitypackage");

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
                if (string.Equals(args[i], PackageArg, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return StationPackage.DefaultPackagePath;
        }

        public static void Import(string outerPackagePath)
        {
            if (!File.Exists(outerPackagePath))
            {
                throw new FileNotFoundException(
                    "外側のパッケージが無い。Package Manager の My Assets から"
                    + "ダウンロードすること（GUI ホップ / CLAUDE.md §0-B）: " + outerPackagePath,
                    outerPackagePath);
            }

            var total = Stopwatch.StartNew();
            Directory.CreateDirectory(TempDirectory);

            try
            {
                // ---- 1. 入れ子の URP を取り出す ----
                var step = Stopwatch.StartNew();
                Dictionary<string, string> outer = UnityPackageReader.ScanPathnames(outerPackagePath);
                Debug.Log($"[StationImport] 外側のエントリ {outer.Count} 件 "
                          + $"({step.ElapsedMilliseconds} ms)");

                string nestedGuid = outer.FirstOrDefault(
                    kv => kv.Value.Replace('\\', '/').EndsWith(
                        StationPackage.NestedUrpPath, StringComparison.Ordinal)).Key;

                if (string.IsNullOrEmpty(nestedGuid))
                {
                    throw new InvalidOperationException(
                        "入れ子の URP パッケージが見つからない: " + StationPackage.NestedUrpPath);
                }

                step.Restart();
                if (!UnityPackageReader.ExtractAssetTo(outerPackagePath, nestedGuid, NestedPath))
                {
                    throw new InvalidOperationException("入れ子の asset を取り出せなかった");
                }

                Debug.Log($"[StationImport] 入れ子を取り出した: {new FileInfo(NestedPath).Length} バイト "
                          + $"({step.ElapsedMilliseconds} ms)");

                // ---- 2. pathname の書き換えと除外 ----
                step.Restart();
                Dictionary<string, string> nested = UnityPackageReader.ScanPathnames(NestedPath);
                var skip = new HashSet<string>();
                var skipped = new List<string>();

                foreach (KeyValuePair<string, string> kv in nested)
                {
                    string relative = kv.Value.Replace('\\', '/');
                    foreach (string exclude in StationPackage.ExcludeFromImport)
                    {
                        if (relative.EndsWith("/" + exclude, StringComparison.Ordinal))
                        {
                            skip.Add(kv.Key);
                            skipped.Add(relative);
                        }
                    }
                }

                if (skipped.Count != StationPackage.ExcludeFromImport.Length)
                {
                    throw new InvalidOperationException(
                        $"除外リストの {StationPackage.ExcludeFromImport.Length} 件のうち "
                        + $"{skipped.Count} 件しか当たらなかった: "
                        + string.Join(", ", skipped.ToArray()));
                }

                // **元パッケージは SourceRoot より上のフォルダも持っている**
                // （"Assets" / "Assets/Cobble Games"）。写し先が決まらないので落とす。
                // フォルダは Unity がファイルの取り込み時に作るので、無くても困らない。
                var ancestors = new List<string>();
                foreach (KeyValuePair<string, string> kv in nested)
                {
                    string relative = kv.Value.Replace('\\', '/');
                    if (StationPackage.SourceRoot.StartsWith(relative + "/", StringComparison.Ordinal))
                    {
                        skip.Add(kv.Key);
                        ancestors.Add(relative);
                    }
                }

                foreach (string a in ancestors)
                {
                    Debug.Log("[StationImport]   落とした親フォルダ: " + a);
                }

                int rewritten = UnityPackageReader.RewriteFiltered(
                    NestedPath, RemappedPath, StationPackage.MapToDestination, skip, out int dropped);

                Debug.Log($"[StationImport] 宛先を書き換えた: {rewritten} 件 / "
                          + $"除外で落としたエントリ {dropped} 件 ({step.ElapsedMilliseconds} ms)");
                foreach (string s in skipped)
                {
                    Debug.Log("[StationImport]   除外: " + s);
                }

                // ---- 3. 取り込み前の自己検査 ----
                Verify(RemappedPath, nested.Count - skip.Count);

                // ---- 4. 取り込み ----
                step.Restart();
                ImportSynchronously(RemappedPath);
                AssetDatabase.Refresh();
                Debug.Log($"[StationImport] 取り込んだ ({step.ElapsedMilliseconds} ms)");
            }
            finally
            {
                // ---- 5. 一時ファイルを消す ----
                foreach (string p in new[] { NestedPath, RemappedPath })
                {
                    try
                    {
                        if (File.Exists(p))
                        {
                            File.Delete(p);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[StationImport] 一時ファイルを消せない: " + p + " / " + e.Message);
                    }
                }
            }

            Debug.Log($"[StationImport] 合計 {total.Elapsed.TotalSeconds:F1} 秒");
            StationInventory.Report();
        }

        /// <summary>**取り込み前の自己検査。** 1 件でも外れていたら例外で止める。</summary>
        public static void Verify(string remappedPackagePath, int expectedCount)
        {
            Dictionary<string, string> paths = UnityPackageReader.ScanPathnames(remappedPackagePath);

            if (paths.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"エントリ数が合わない: {paths.Count} != {expectedCount}");
            }

            foreach (KeyValuePair<string, string> kv in paths)
            {
                if (!kv.Value.Replace('\\', '/').StartsWith(
                        StationPackage.DestinationRoot, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "**取り込み先の外へ展開されるエントリがある**（追跡対象に乗る）: "
                        + kv.Value);
                }
            }

            Debug.Log($"[StationImport] 自己検査を通った: {paths.Count} 件すべてが "
                      + StationPackage.DestinationRoot + " の下");
        }

        /// <summary>`ImportPackageImmediately` が引けるか。テストが見る窓。</summary>
        public static bool SynchronousImportAvailable => FindImmediateImport() != null;

        static MethodInfo FindImmediateImport() => typeof(AssetDatabase).GetMethod(
            "ImportPackageImmediately",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        static void ImportSynchronously(string packagePath)
        {
            MethodInfo immediate = FindImmediateImport();
            if (immediate == null)
            {
                throw new MissingMethodException(
                    "AssetDatabase.ImportPackageImmediately が引けない。"
                    + "公開 API の ImportPackage は batchmode + -quit では完了しない。");
            }

            immediate.Invoke(null, new object[] { packagePath });
        }
    }
}
