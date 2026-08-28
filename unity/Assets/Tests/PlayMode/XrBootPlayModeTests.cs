using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// **本番経路で「無指定なら XR が動かない」ことを縛る (Step 12-0c)。**
    ///
    /// EditMode は Editor プロセスで走るので、そこで「起動していない」と確かめても
    /// **もともと起動していないものを見ているだけ。** PlayMode はシーンを読み込んで
    /// `RuntimeInitializeOnLoadMethod` まで通る本番の経路なので、ここで縛る必要がある。
    ///
    /// **既存 462 件を汚さないことがこのスパイクの最重要リグレッション。**
    /// </summary>
    public sealed class XrBootPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath =
                Path.Combine(Path.GetTempPath(), "solar-system-explorer-xrboot.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        [UnityTest]
        public IEnumerator 無指定の起動ではXRが動いていない()
        {
            yield return null;

            // この PlayMode の実行に -xr / -xrMock は付いていない。
            Assert.That(XrBoot.ParseMode(System.Environment.GetCommandLineArgs()),
                        Is.EqualTo(XrBoot.Mode.None), "テストの実行に XR の引数が付いている");

            // **AutoBoot は走っているが、何もしていないはず。**
            Assert.That(XRSettings.enabled, Is.False, "**XRSettings が有効になっている**");
            Assert.That(XRSettings.isDeviceActive, Is.False, "XR デバイスが動いている");
            Assert.That(XRSettings.loadedDeviceName, Is.Empty.Or.EqualTo("None"),
                        "XR デバイスが読み込まれている: " + XRSettings.loadedDeviceName);

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            Assert.That(settings, Is.Not.Null, "XRGeneralSettings が無い");
            Assert.That(settings.InitManagerOnStart, Is.False,
                        "**自動起動が入っている**（引数を見る前に XR が立ち上がる）");
            Assert.That(settings.Manager, Is.Not.Null);
            Assert.That(settings.Manager.activeLoader, Is.Null,
                        "**ローダが立ち上がっている**");
            Assert.That(settings.Manager.isInitializationComplete, Is.False);
        }

        [UnityTest]
        public IEnumerator 無指定ならXrBootは何も記録していない()
        {
            yield return null;

            // `AutoBoot` は None で即 return するので、結果すら残さない。
            Assert.That(XrBoot.Last, Is.Null.Or.Matches<XrBoot.Result>(r => !r.Initialized),
                        "無指定なのに初期化の記録がある");
        }

        [UnityTest]
        public IEnumerator 立体視の実態を読める()
        {
            yield return null;

            // **値の取得だけ。判定はしない。**
            // XR が動いていない平面では「動いていない」値がそのまま入る。
            XrBoot.StereoFacts facts = XrBoot.ReadStereoFacts();
            Assert.That(facts, Is.Not.Null);

            Debug.Log("  [Step12-0c] 平面での立体視の実態: " + facts);

            Assert.That(facts.XrSettingsEnabled, Is.False);
        }
    }
}
