using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// `.unitypackage` の取り込み (Step 11-1a)。
    ///
    /// ■ 二段構え
    /// 前半は**取り込みが無くても通る**（宛先の写し方・削除リスト・一時ファイルの
    /// 置き場）。後半は取り込み済みのときだけ実体を見て、**取り込まれていなければ
    /// Inconclusive**。clone しただけの環境ではアセットが無いのが正常なので、
    /// Fail にはしない。「確かめられなかった」を Pass にもしない。
    ///
    /// ■ ここが縛っているのは「取り込み先が `ThirdParty/` の下から出ないこと」
    /// `AssetDatabase.ImportPackageImmediately` は**宛先の引数を持たない**ので、
    /// パッケージが記録しているパスへそのまま展開される。それを避けるために
    /// `CockpitImporter` は `pathname` を書き換えてから取り込む。
    /// **その仕掛けが効いているかを、定数側（前半）と実体側（後半）の両方から見る。**
    /// </summary>
    public sealed class CockpitImportTests
    {
        /// <summary>取り込み済みか。**フォルダの有無ではなくプレハブが読めるかで見る。**</summary>
        static bool Imported =>
            !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(CockpitPackage.CockpitWithInteriorGuid));

        static void RequireImported()
        {
            if (!Imported)
            {
                Assert.Inconclusive(
                    "まだ取り込まれていない（clone 直後は正常）。"
                    + "run_unity.ps1 -Method SolarSetup.ImportCockpit で取り込む。");
            }
        }

        // ---- 取り込みが無くても通る部分 ----

        [Test]
        public void 宛先はThirdPartyの下にある()
        {
            Assert.That(CockpitPackage.DestinationRoot,
                        Does.StartWith(CockpitCatalog.ThirdPartyFolder + "/"),
                        "**宛先が追跡除外の外にある。** EULA が再配布を禁じている");

            // 元のパスは `.gitignore` の外。だから書き換えが要る。
            Assert.That(CockpitPackage.SourceRoot,
                        Does.Not.StartWith(CockpitCatalog.ThirdPartyFolder),
                        "元のパスが既に宛先の下なら書き換えは不要のはず。前提が変わっている");
        }

        [Test]
        public void 元のパスを宛先へ写す()
        {
            string mapped = CockpitPackage.MapToDestination(
                CockpitPackage.SourceRoot + "/Prefabs/Modules/Cockpit.prefab");

            Assert.That(mapped, Is.EqualTo(
                CockpitPackage.DestinationRoot + "/Prefabs/Modules/Cockpit.prefab"));
        }

        [Test]
        public void 関係のないパスは書き換えない()
        {
            // **写し損ねより、余計な書き換えのほうが見つけにくい。**
            const string unrelated = "Assets/Scripts/Unity/CockpitIdentity.cs";
            Assert.That(CockpitPackage.MapToDestination(unrelated), Is.EqualTo(unrelated));
        }

        [Test]
        public void 取り込み前に作るフォルダが宛先の祖先を埋めている()
        {
            // `AssetDatabase.CreateFolder` は**親が無いと作れない。**
            // 末端はパッケージの取り込みが作るので、要るのは祖先だけ。
            string[] expected = { "Assets/ThirdParty", "Assets/ThirdParty/EbalStudios" };
            Assert.That(CockpitPackage.FoldersToCreate, Is.EqualTo(expected));

            Assert.That(CockpitPackage.DestinationRoot,
                        Does.StartWith(expected[expected.Length - 1] + "/"),
                        "宛先の親が作成リストの末尾と食い違っている");
        }

        [Test]
        public void 削除リストは宛先の下だけを指す()
        {
            foreach (string relative in CockpitPackage.DeleteAfterImport)
            {
                string path = CockpitPackage.DeletePath(relative);
                Assert.That(path, Does.StartWith(CockpitPackage.DestinationRoot + "/"),
                            "取り込んだもの以外を消そうとしている: " + relative);
                Assert.That(relative, Does.Not.Contain(".."),
                            "相対指定で外へ出ようとしている: " + relative);
            }
        }

        [Test]
        public void 削除リストに重複が無い()
        {
            string[] duplicated = CockpitPackage.DeleteAfterImport
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            Assert.That(duplicated, Is.Empty, "削除リストが重複している: "
                                              + string.Join(", ", duplicated));
        }

        [Test]
        public void テクスチャは削除リストに入っていない()
        {
            // **参照されないアセットはビルドに含まれない**ので、消しても exe は軽く
            // ならない。消すと再取り込みが要るぶん損になる（11-1a の判断）。
            string[] textures = CockpitPackage.DeleteAfterImport
                .Where(p => p.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(textures, Is.Empty, "方針と食い違っている: " + string.Join(", ", textures));
        }

        [Test]
        public void 一時パッケージはリポジトリの外に置く()
        {
            // **190MB が追跡対象の場所に残ると public に乗る。**
            // `run_unity.ps1` は 15 分でプロセスツリーを Kill するので、
            // 「最後に消す」だけでは足りない。置き場そのものを外にする。
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string temp = Path.GetFullPath(CockpitImporter.TempPackagePath);

            Assert.That(temp, Does.Not.StartWith(repoRoot),
                        "一時パッケージがリポジトリの中にある: " + temp);
        }

        [Test]
        public void プレハブのGUIDは定義側にしかない()
        {
            // 二重定義していないこと。片方だけ直る事故を防ぐ。
            Assert.That(CockpitPackage.CockpitWithInteriorGuid,
                        Is.EqualTo(CockpitDefinition.HiRezSample.PrefabGuid));
        }

        [Test]
        public void 同期取り込みのAPIが引ける()
        {
            // **公開 API では代替できない。**
            // `AssetDatabase.ImportPackage(path, interactive)` は非同期で、
            // `-batchmode -quit` では次の tick が来ないまま終わる（TmpSetup で実測済み）。
            // 同期版は internal なのでリフレクションで引いている。**Unity を上げて
            // 引けなくなったら、取り込みを走らせる前にここで気づける。**
            Assert.That(CockpitImporter.SynchronousImportAvailable, Is.True,
                        "AssetDatabase.ImportPackageImmediately が引けない。"
                        + "取り込みの経路を作り直す必要がある");
        }

        // ---- 取り込み済みのときだけ見る部分 ----

        [Test]
        public void 取り込んだアセットが宛先の外に出ていない()
        {
            RequireImported();

            string[] strays = AssetDatabase.FindAssets(string.Empty,
                                                       new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => p.Contains("HiRezSpaceships"))
                .Where(p => !p.StartsWith(CockpitPackage.DestinationRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(strays, Is.Empty,
                        "**第三者アセットが ThirdParty の外にある:**\n  "
                        + string.Join("\n  ", strays));
        }

        [Test]
        public void 内装コックピットのプレハブが読める()
        {
            RequireImported();

            string path = AssetDatabase.GUIDToAssetPath(CockpitPackage.CockpitWithInteriorGuid);
            Assert.That(path, Does.StartWith(CockpitPackage.DestinationRoot + "/"),
                        "プレハブが宛先の外にある: " + path);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, "GUID は解決できたがプレハブとして読めない: " + path);

            int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
            Assert.That(renderers, Is.GreaterThan(0), "レンダラーが 1 つも無い");

            Debug.Log($"  [Step11-1a] 内装プレハブ: {path} / レンダラー {renderers} 個");
        }

        [Test]
        public void 取り込んだ件数がパッケージのエントリ数と合う()
        {
            RequireImported();

            // **数え方をそろえる。**
            // `FindAssets` は探索の起点にしたフォルダ自身を返さないので、
            // パッケージ側のエントリ数（起点も 1 件として入っている）と比べるには
            // 起点を足す。ログの「取り込み結果」も同じ数え方にしてある。
            int found = AssetDatabase.FindAssets(string.Empty,
                                                 new[] { CockpitPackage.DestinationRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Count();

            int root = AssetDatabase.IsValidFolder(CockpitPackage.DestinationRoot) ? 1 : 0;
            int expected = CockpitPackage.ExpectedEntryCount
                           - CockpitPackage.DeleteAfterImport.Length;

            Assert.That(found + root, Is.EqualTo(expected),
                        "取り込み後の件数が合わない（パッケージ "
                        + CockpitPackage.ExpectedEntryCount + " 件 - 削除 "
                        + CockpitPackage.DeleteAfterImport.Length + " 件）");
        }

        [Test]
        public void 削除リストのものが残っていない()
        {
            RequireImported();

            string[] remaining = CockpitPackage.DeleteAfterImport
                .Select(CockpitPackage.DeletePath)
                .Where(p => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) != null)
                .ToArray();

            Assert.That(remaining, Is.Empty,
                        "取り込み後の削除が効いていない:\n  " + string.Join("\n  ", remaining));
        }

        [Test]
        public void 定義がフォールバックせずに解決できる()
        {
            RequireImported();

            Assert.That(CockpitCatalog.IsAvailable(CockpitDefinition.HiRezSample), Is.True,
                        "取り込み済みなのに利用可能と判定されない");
        }
    }
}
