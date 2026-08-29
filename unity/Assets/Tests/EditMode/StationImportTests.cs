using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Editor;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// ステーションアセットの取り込み (Step 13-2)。`CockpitImportTests` と同じ二段構え。
    ///
    /// 前半は**取り込みが無くても通る**（宛先・一時ファイルの置き場・除外リスト）。
    /// 後半は取り込み済みのときだけ実体を見て、**取り込まれていなければ Inconclusive**。
    /// clone しただけの環境ではアセットが無いのが正常なので Fail にしない。
    /// 「確かめられなかった」を Pass にもしない。
    ///
    /// ■ このパッケージ固有の危険
    /// 外側は Built-in 版で、URP と HDRP を**入れ子の `.unitypackage` として持つ**。
    /// 素直に取り込むと 3 パイプライン分が `Assets/` に落ちる。
    /// 経路 C は入れ子の URP だけを取り出して取り込む。**その仕掛けが壊れたことを
    /// サイズの上限で捕まえる。**
    /// </summary>
    public sealed class StationImportTests
    {
        /// <summary>取り込み済みか。**フォルダの有無ではなくプレハブが読めるかで見る。**</summary>
        static bool Imported =>
            !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(StationPackage.StationPrefabGuid));

        static void RequireImported()
        {
            if (!Imported)
            {
                Assert.Inconclusive(
                    "まだ取り込まれていない（clone 直後は正常）。"
                    + "run_unity.ps1 -Method SolarSetup.ImportStation で取り込む。");
            }
        }

        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        // ---- 取り込みが無くても通る部分 ----

        [Test]
        public void 宛先はThirdPartyの下にある()
        {
            Assert.That(StationPackage.DestinationRoot,
                        Does.StartWith("Assets/ThirdParty/"),
                        "**宛先が追跡除外の外にある。** EULA が再配布を禁じている");

            Assert.That(StationPackage.SourceRoot,
                        Does.Not.StartWith("Assets/ThirdParty"),
                        "元のパスが既に宛先の下なら書き換えは不要のはず。前提が変わっている");
        }

        [Test]
        public void 元のパスを宛先へ写す()
        {
            Assert.That(StationPackage.MapToDestination(
                            StationPackage.SourceRoot + "/Prefabs/Space Station.prefab"),
                        Is.EqualTo(StationPackage.DestinationRoot + "/Prefabs/Space Station.prefab"));

            // 宛先の外のものは触らない（自己検査がそれを落とす）。
            Assert.That(StationPackage.MapToDestination("Assets/Other/x.mat"),
                        Is.EqualTo("Assets/Other/x.mat"));
        }

        [Test]
        public void pathnameの2行目を落とさない()
        {
            // **`pathname` は 1 行目がパス、2 行目が "00" の 2 行構成のことがある。**
            // 1 行目だけを見て突き合わせ、残りはそのまま付け直す。
            // （13-2 で 1 度踏んだ。Trim() では内側の改行が残り、入れ子の
            //  URP/URP.unitypackage を EndsWith で見つけられなかった）
            string original = StationPackage.SourceRoot + "/Prefabs/Space Station.prefab\n00";
            string mapped = StationPackage.MapToDestination(original);

            Assert.That(mapped,
                        Is.EqualTo(StationPackage.DestinationRoot
                                   + "/Prefabs/Space Station.prefab\n00"));
        }

        [Test]
        public void 一時ファイルはリポジトリの外に置く()
        {
            // **取り出す入れ子は 640 MB、書き換え後はさらにもう 1 本ある。**
            // run_unity.ps1 は時間切れでプロセスツリーを Kill するので、
            // 「最後に消す」だけでは足りない。置き場そのものを外にする。
            string temp = Path.GetFullPath(StationImporter.TempDirectory);

            Assert.That(temp, Does.Not.StartWith(RepoRoot),
                        "一時ディレクトリがリポジトリの中にある: " + temp);
        }

        [Test]
        public void 同期取り込みのAPIが引ける()
        {
            Assert.That(StationImporter.SynchronousImportAvailable, Is.True,
                        "AssetDatabase.ImportPackageImmediately が引けない。"
                        + "取り込みの経路を作り直す必要がある");
        }

        [Test]
        public void 除外リストにテクスチャが入っていない()
        {
            // hdr.png は HDRI（背景）であってモデルのテクスチャではない。
            // モデルのテクスチャは消さない方針（Demo 3 と同じ）。
            string[] textures = StationPackage.ExcludeFromImport
                .Where(p => p.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(textures, Is.Empty, "方針と食い違っている: " + string.Join(", ", textures));
        }

        // ---- 取り込み済みのときだけ見る部分 ----

        [Test]
        public void 取り込んだアセットが宛先の外に出ていない()
        {
            RequireImported();

            string[] strays = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => p.IndexOf("Cobble", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(p => !p.StartsWith(StationPackage.DestinationRoot, StringComparison.Ordinal))
                // **宛先の親フォルダ自身は「外に出た」ではない。**
                // 取り込みが Assets/ThirdParty/CobbleGames を作るので必ず 1 件出る。
                .Where(p => !StationPackage.DestinationRoot.StartsWith(
                            p + "/", StringComparison.Ordinal))
                .ToArray();

            Assert.That(strays, Is.Empty,
                        "**第三者アセットが ThirdParty の外にある:**\n  "
                        + string.Join("\n  ", strays));
        }

        [Test]
        public void ステーションのプレハブが読めてレンダラーがある()
        {
            RequireImported();

            string path = AssetDatabase.GUIDToAssetPath(StationPackage.StationPrefabGuid);
            Assert.That(path, Does.StartWith(StationPackage.DestinationRoot + "/"),
                        "プレハブが宛先の外にある: " + path);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, "GUID は解決できたがプレハブとして読めない: " + path);

            int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
            Assert.That(renderers, Is.GreaterThan(0), "レンダラーが 1 つも無い");
        }

        [Test]
        public void 取り込んだマテリアルはすべてURP()
        {
            RequireImported();

            // **「URP 版だから変換不要」と決めつけない。** 実体のシェーダを見る。
            // 組み込みシェーダ（Standard 等）のマテリアルが 1 件でも混じると
            // URP では紫になる。
            string[] offenders = AssetDatabase
                .FindAssets("t:Material", new[] { StationPackage.DestinationRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(p => new
                {
                    Path = p,
                    Material = AssetDatabase.LoadAssetAtPath<Material>(p),
                })
                .Where(x => x.Material == null
                            || x.Material.shader == null
                            || !x.Material.shader.name.StartsWith(
                                "Universal Render Pipeline/", StringComparison.Ordinal))
                .Select(x => x.Path + " / "
                             + (x.Material == null || x.Material.shader == null
                                    ? "<読めない>"
                                    : x.Material.shader.name))
                .ToArray();

            Assert.That(offenders, Is.Empty,
                        "**URP でないマテリアルがある:**\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void マテリアルの件数が実測と合う()
        {
            RequireImported();

            int found = AssetDatabase
                .FindAssets("t:Material", new[] { StationPackage.DestinationRoot })
                .Distinct()
                .Count();

            Assert.That(found, Is.EqualTo(StationPackage.MaterialCount),
                        "マテリアルの件数が実測と違う: " + found
                        + " != " + StationPackage.MaterialCount);
        }

        [Test]
        public void 除外したものが取り込まれていない()
        {
            RequireImported();

            string[] remaining = StationPackage.ExcludeFromImport
                .Select(StationPackage.DestinationPath)
                .Where(p => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                .ToArray();

            Assert.That(remaining, Is.Empty,
                        "除外したはずのものが残っている:\n  " + string.Join("\n  ", remaining));
        }

        [Test]
        public void 取り込んだ量が上限を超えていない()
        {
            RequireImported();

            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                StationPackage.DestinationRoot);
            Assert.That(Directory.Exists(absolute), Is.True, "宛先が無い: " + absolute);

            long bytes = 0;
            int files = 0;
            foreach (string f in Directory.GetFiles(absolute, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(f).Length;
                files++;
            }

            // **失敗したときに実測と閾値の両方が読めるようにする。**
            Assert.That(bytes, Is.LessThanOrEqualTo(StationPackage.MaxImportedBytes),
                        "取り込み量が上限を超えた: 実測 " + bytes + " バイト ("
                        + (bytes / 1048576.0).ToString("F2") + " MB / " + files
                        + " ファイル) > 上限 " + StationPackage.MaxImportedBytes + " バイト ("
                        + (StationPackage.MaxImportedBytes / 1048576.0).ToString("F2")
                        + " MB = 実測 " + (StationPackage.ImportedBytes / 1048576.0).ToString("F2")
                        + " MB の +15%)。**入れ子の URP だけを取り出す経路が壊れて "
                        + "Built-in や HDRP まで入った可能性がある。**");
        }
    }
}
