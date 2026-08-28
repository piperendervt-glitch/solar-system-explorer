using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// XR の起動経路 (Step 12-0)。
    ///
    /// **ここで縛るのは「無指定なら何も起きない」ことと「指定したらローダが選ばれる」こと。**
    /// 初期化の成否（MockHMD が Unity 6000.3 で立ち上がるか）はセッション C の関門で、
    /// ここでは判断しない。
    /// </summary>
    public sealed class XrBootTests
    {
        // ---- 引数の読み取り ----

        [Test]
        public void 無指定ならNone()
        {
            Assert.That(XrBoot.ParseMode(new[] { "Unity.exe", "-batchmode", "-scenario", "xr" }),
                        Is.EqualTo(XrBoot.Mode.None));
            Assert.That(XrBoot.ParseMode(new string[0]), Is.EqualTo(XrBoot.Mode.None));
            Assert.That(XrBoot.ParseMode(null), Is.EqualTo(XrBoot.Mode.None));
        }

        [Test]
        public void 指定するとそのモードになる()
        {
            Assert.That(XrBoot.ParseMode(new[] { "app.exe", "-xr" }),
                        Is.EqualTo(XrBoot.Mode.Real));
            Assert.That(XrBoot.ParseMode(new[] { "app.exe", "-xrMock" }),
                        Is.EqualTo(XrBoot.Mode.Mock));

            // 大文字小文字は問わない。
            Assert.That(XrBoot.ParseMode(new[] { "app.exe", "-XRMOCK" }),
                        Is.EqualTo(XrBoot.Mode.Mock));

            // 両方あれば実機を優先する。
            Assert.That(XrBoot.ParseMode(new[] { "app.exe", "-xrMock", "-xr" }),
                        Is.EqualTo(XrBoot.Mode.Real));
        }

        [Test]
        public void モードごとにローダの名前が決まる()
        {
            Assert.That(XrBoot.LoaderNameFor(XrBoot.Mode.Real), Is.EqualTo("OpenXRLoader"));
            Assert.That(XrBoot.LoaderNameFor(XrBoot.Mode.Mock), Is.EqualTo("MockHMDLoader"));
            Assert.That(XrBoot.LoaderNameFor(XrBoot.Mode.None), Is.Empty);
        }

        // ---- 無指定では XR を起動しない ----

        [Test]
        public void 無指定ではXRサブシステムが起動していない()
        {
            // **この EditMode の実行そのものが「無指定の起動」。**
            // `-xr` も `-xrMock` も付いていないので、XR は 1 つも動いていないはず。
            Assert.That(XrBoot.ParseMode(Environment.GetCommandLineArgs()),
                        Is.EqualTo(XrBoot.Mode.None), "テストの実行に XR の引数が付いている");

            Assert.That(XRSettings.enabled, Is.False, "**XRSettings が有効になっている**");
            Assert.That(XRSettings.isDeviceActive, Is.False, "XR デバイスが動いている");

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings != null && settings.Manager != null)
            {
                Assert.That(settings.Manager.activeLoader, Is.Null,
                            "**ローダが立ち上がっている**（無指定では触ってはいけない）");
                Assert.That(settings.Manager.isInitializationComplete, Is.False);
            }
        }

        [Test]
        public void Noneで呼んでも何もしない()
        {
            XrBoot.Result result = XrBoot.Initialize(XrBoot.Mode.None);

            Assert.That(result.Requested, Is.False);
            Assert.That(result.Initialized, Is.False);
            Assert.That(result.LoaderName, Is.Empty);
            Assert.That(XRSettings.enabled, Is.False);
        }

        // ---- ローダのアセット ----

        [Test]
        public void 両方のローダのアセットがある()
        {
            // `Assets/XR/Loaders/` は XR パッケージが自動生成する。
            // **どちらかが消えるとモードを選べなくなる**ので、存在を縛る。
            string loaders = Path.Combine(Application.dataPath, "XR", "Loaders");
            Assert.That(Directory.Exists(loaders), Is.True, "Assets/XR/Loaders/ が無い");

            string[] names = Directory.GetFiles(loaders, "*.asset")
                .Select(Path.GetFileNameWithoutExtension)
                .ToArray();

            Assert.That(names, Does.Contain(XrBoot.OpenXrLoaderName));
            Assert.That(names, Does.Contain(XrBoot.MockLoaderName));
        }

        [Test]
        public void 自動起動が切ってある()
        {
            // **これが「無指定なら XR が動かない」の構造的な保証。**
            // InitManagerOnStart が true だと、ローダを登録した時点で起動と同時に
            // XR が立ち上がり、引数を見る前に平面の絵が変わってしまう。
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            Assert.That(settings, Is.Not.Null,
                        "XRGeneralSettings が無い（SolarSetup.ConfigureXr を回すこと）");

            Assert.That(settings.InitManagerOnStart, Is.False,
                        "**XR が起動と同時に立ち上がる設定になっている**");
        }

        [Test]
        public void 両方のローダが登録されている()
        {
            // **-xr / -xrMock でどちらを選ぶかは XrBoot が決める。**
            // 登録そのものは両方あってよい（自動起動は切ってある）。
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            Assert.That(settings?.Manager, Is.Not.Null);

            string[] names = settings.Manager.activeLoaders
                .Where(l => l != null).Select(l => l.name).ToArray();

            Assert.That(names, Does.Contain(XrBoot.OpenXrLoaderName));
            Assert.That(names, Does.Contain(XrBoot.MockLoaderName));
        }

        [Test]
        public void MockHMDのローダが選ばれる()
        {
            // **-xrMock でどのローダが選ばれるか**を、初期化せずに確かめる。
            // **初期化の成否はセッション C の関門**なので、ここでは触らない。
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            Assert.That(settings?.Manager, Is.Not.Null);

            XRLoader mock = settings.Manager.activeLoaders
                .FirstOrDefault(l => l != null && l.name == XrBoot.MockLoaderName);

            Assert.That(mock, Is.Not.Null,
                        "MockHMDLoader が登録されていない（-xrMock でローダを選べない）");
            Assert.That(XrBoot.LoaderNameFor(XrBoot.Mode.Mock), Is.EqualTo(mock.name),
                        "-xrMock が選ぶ名前と、登録されているローダの名前が違う");

            // **選んだだけでは起動しない。**
            Assert.That(settings.Manager.activeLoader, Is.Null, "ローダが立ち上がっている");
            Assert.That(XRSettings.enabled, Is.False);
        }

        // ---- Render Mode ----

        [Test]
        public void 両方のRenderModeがSinglePassInstanced()
        {
            // **列挙はパッケージごとに読んで確かめた (CLAUDE.md §0-D)。**
            //   OpenXR  `OpenXRSettings.RenderMode`       0 = MultiPass / 1 = SinglePassInstanced
            //   MockHMD `MockHMDBuildSettings.RenderMode` 0 = MultiPass / 1 = SinglePassInstanced
            //
            // **MockHMD の生成時の既定は 0 = MultiPass。** 明示しないと batchmode の
            // 測定が SPI の経路を一切通らず、数値が緑でも実機で初めて壊れる。
            Assert.That(RenderModeOf("MockHMDBuildSettings.asset", "renderMode:"), Is.EqualTo(1),
                        "**MockHMD が MultiPass のまま**（SPI の経路を通らない）");

            Assert.That(RenderModeOf("OpenXR Package Settings.asset", "m_renderMode:"),
                        Is.EqualTo(1), "OpenXR が MultiPass になっている");
        }

        /// <summary>設定アセットから RenderMode を読む。**すべての行が同じ値**であること。</summary>
        static int RenderModeOf(string assetName, string key)
        {
            string path = Path.Combine(Application.dataPath, "XR", "Settings", assetName);
            Assert.That(File.Exists(path), Is.True, path + " が無い");

            int[] values = File.ReadAllLines(path)
                .Where(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal))
                .Select(l => int.Parse(l.Split(':')[1].Trim()))
                .ToArray();

            Assert.That(values, Is.Not.Empty, assetName + " に " + key + " が無い");
            Assert.That(values.Distinct().Count(), Is.EqualTo(1),
                        "ビルド対象ごとに値が違う: " + string.Join(", ", values));

            return values[0];
        }
    }
}
