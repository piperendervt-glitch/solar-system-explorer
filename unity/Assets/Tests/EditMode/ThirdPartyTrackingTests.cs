using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// Asset Store のアセットが public リポジトリに乗らないこと (Step 11-0b)。
    ///
    /// **守りたい事故は「アセットが追跡される」ことなので、索引を見るのが主。**
    /// `.gitignore` に規則があっても `git add -f` で 1 回入れば以後は追跡され続ける
    /// （`.gitignore` は**未追跡ファイルにしか効かない**）。だから規則の検証だけでは
    /// 目的を果たせない。
    ///
    /// 規則の検証（従）も残す。git が無い環境でも規則そのものが消えた事故は拾えるため。
    /// </summary>
    public sealed class ThirdPartyTrackingTests
    {
        const string TrackedPrefix = "unity/Assets/ThirdParty/";
        const string MetaPath = "unity/Assets/ThirdParty.meta";

        /// <summary>リポジトリのルート。`Application.dataPath` は `<repo>/unity/Assets`。</summary>
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        // ---- A（主）: git の索引を見る ----

        [Test]
        public void 追跡ファイルにThirdParty配下が無い()
        {
            string[] tracked = RunGit("ls-files -- unity/Assets/ThirdParty");

            string[] offenders = tracked
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            Assert.That(offenders, Is.Empty,
                        "**Asset Store のアセットが追跡されている。** EULA が再配布を禁じている:\n  "
                        + string.Join("\n  ", offenders));
        }

        [Test]
        public void ThirdPartyのフォルダmetaも追跡されていない()
        {
            // 追跡すると、アセットが無いクローンで**空の ThirdParty だけが復元される。**
            // フォルダの有無での判定が壊れるので、meta も追跡しない。
            string[] tracked = RunGit("ls-files -- " + MetaPath);

            Assert.That(tracked.Where(l => !string.IsNullOrWhiteSpace(l)), Is.Empty,
                        MetaPath + " が追跡されている");
        }

        /// <summary>
        /// git を叩く。**git が無い / リポジトリでないときは Inconclusive。**
        ///
        /// Pass にしない。「確かめられなかった」を「問題なし」として通すのは、
        /// このプロジェクトが繰り返し踏んできた失敗（bloom 消灯・36x36・走査範囲）と
        /// 同じ形だから。Fail にもしない。git が無いのは環境の話でコードの欠陥ではない。
        ///
        /// run_tests.ps1 は failed で判定するので Inconclusive は緑を壊さないが、
        /// XML の inconclusive 件数に出るので「確かめていない項目がある」と分かる。
        /// </summary>
        static string[] RunGit(string arguments)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        Assert.Inconclusive("git を起動できないので追跡状態を確認できない");
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(30000))
                    {
                        Assert.Inconclusive("git が 30 秒で終わらなかった");
                    }

                    if (process.ExitCode != 0)
                    {
                        Assert.Inconclusive(
                            $"git が失敗した (exit {process.ExitCode})。リポジトリでない可能性: {stderr.Trim()}");
                    }

                    return stdout.Replace("\r\n", "\n").Split('\n');
                }
            }
            catch (Exception e)
            {
                Assert.Inconclusive("git が見つからないので追跡状態を確認できない: " + e.Message);
                throw; // 到達しない
            }
        }

        // ---- B（従）: .gitignore の規則そのもの ----

        [Test]
        public void gitignoreにThirdPartyの規則がある()
        {
            string path = Path.Combine(RepoRoot, ".gitignore");
            Assert.That(File.Exists(path), Is.True, ".gitignore が無い: " + path);

            string[] lines = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToArray();

            // `/source_assets/*` と同じ流儀で**中身だけ**除外する。
            Assert.That(lines, Does.Contain("/unity/Assets/ThirdParty/*"),
                        "取り込み先の除外規則が無い");
            Assert.That(lines, Does.Contain("/unity/Assets/ThirdParty.meta"),
                        "フォルダ meta の除外規則が無い");
        }

        [Test]
        public void 除外の例外がThirdPartyを再包含していない()
        {
            string path = Path.Combine(RepoRoot, ".gitignore");
            string[] reincludes = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("!"))
                .ToArray();

            foreach (string line in reincludes)
            {
                Assert.That(line, Does.Not.Contain("ThirdParty"),
                            "再包含の規則が ThirdParty を拾っている: " + line);
            }
        }

        // ---- フォールバック (11-0c) ----

        [Test]
        public void 箱はプレハブを要求しない()
        {
            Assert.That(CockpitDefinition.Box.NeedsPrefab, Is.False);
            Assert.That(CockpitCatalog.IsAvailable(CockpitDefinition.Box), Is.True,
                        "箱は取り込みが無くても組めなければならない");
        }

        [Test]
        public void 取り込まれていない定義は箱へ落ちる()
        {
            // 実在しない GUID。
            var missing = new CockpitDefinition(
                "missing-for-test", "00000000000000000000000000000000", CockpitDefinition.BoxId);

            Assert.That(CockpitCatalog.IsAvailable(missing), Is.False);

            CockpitDefinition resolved = CockpitCatalog.Resolve(missing, out bool fellBack);

            Assert.That(resolved.Id, Is.EqualTo(CockpitDefinition.BoxId));
            Assert.That(fellBack, Is.True, "落ちたことが呼び手に伝わっていない");
        }

        [Test]
        public void 箱を要求したときはフォールバック扱いにしない()
        {
            CockpitDefinition resolved =
                CockpitCatalog.Resolve(CockpitDefinition.Box, out bool fellBack);

            Assert.That(resolved.Id, Is.EqualTo(CockpitDefinition.BoxId));
            Assert.That(fellBack, Is.False, "箱を要求しただけなのに「落ちた」になっている");
        }

        [Test]
        public void 判定はフォルダの有無ではない()
        {
            // **空フォルダがあっても「アセットがある」と判定してはいけない。**
            // ThirdParty は追跡除外なので、フォルダだけが残る状態が起こりうる。
            var missing = new CockpitDefinition(
                "missing-for-test", "ffffffffffffffffffffffffffffffff", CockpitDefinition.BoxId);

            Assert.That(CockpitCatalog.IsAvailable(missing), Is.False,
                        "GUID が解決できないのに利用可能と判定している");
        }
    }
}
