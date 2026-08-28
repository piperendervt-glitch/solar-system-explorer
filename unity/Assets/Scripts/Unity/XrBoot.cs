using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **XR の起動経路 (Step 12-0)。**
    ///
    /// ■ **無指定なら XRGeneralSettings に一切触らない。**
    /// 平面での既存の絵と測定は Demo 3 までの資産で、XR を入れたせいで動くと
    /// 切り分けができなくなる。`-xr` / `-xrMock` が無ければこのクラスは**何もしない**
    /// （`XRGeneralSettings.Instance` を読みにすら行かない）。
    ///
    /// ■ 2 つの入口
    ///   `-xr`      実機 (OpenXR)。HMD が要る
    ///   `-xrMock`  batchmode 用 (MockHMD)。HMD 無しで両眼の経路を通す
    ///
    /// ■ Render Mode は **Single Pass Instanced** を明示する
    /// Q1 が問うているのは「SPI で目のインデックスが Overlay 段に伝わるか」。
    /// **MockHMD の既定は MultiPass** なので、明示しないと batchmode の測定が
    /// SPI の経路を一切通らない（数値が緑でも実機で初めて壊れる）。
    /// 値は `Assets/XR/Settings/*.asset` に焼いてあり、テストで縛っている。
    ///
    /// ■ 初期化の成否はここでは判断しない
    /// MockHMD は experimental で `unity=2022.3` 宣言。Unity 6000.3 で初期化できるかは
    /// **セッション C の関門**。ここは「口を作る」ところまで。
    /// </summary>
    public static class XrBoot
    {
        public const string RealArg = "-xr";
        public const string MockArg = "-xrMock";

        /// <summary>ローダのアセット名。`Assets/XR/Loaders/` に Unity が作る。</summary>
        public const string OpenXrLoaderName = "OpenXRLoader";
        public const string MockLoaderName = "MockHMDLoader";

        public enum Mode
        {
            /// <summary>**既定。** XR に一切触らない。</summary>
            None = 0,

            /// <summary>実機 (OpenXR)。</summary>
            Real = 1,

            /// <summary>batchmode (MockHMD)。</summary>
            Mock = 2,
        }

        public sealed class Result
        {
            public Mode Mode;
            public bool Requested;
            public bool LoaderFound;
            public bool Initialized;
            public string LoaderName = string.Empty;
            public string Message = string.Empty;

            /// <summary>実行時に読んだ立体視の実態。**判定はしない。値を残すだけ。**</summary>
            public StereoFacts Facts;

            public override string ToString()
                => $"mode={Mode} / 要求={Requested} / ローダ={LoaderName}"
                   + $" / 見つかった={LoaderFound} / 初期化={Initialized} / {Message}"
                   + (Facts == null ? string.Empty : " / " + Facts);
        }

        /// <summary>
        /// **「SPI で走っているか」の証拠 (Step 12-0c)。**
        ///
        /// 設定アセットの `renderMode: 1` は**設定しただけ**で、実際に片目ずつ
        /// 2 回描いているだけの可能性が残る。MockHMD は実験的なモックなので、
        /// **実行時の値を読まないと「SPI の経路を通った」とは言えない。**
        ///
        /// SPI なら期待される値:
        ///   `stereoRenderingMode` = SinglePassInstanced
        ///   `eyeTextureDesc.dimension` = Tex2DArray / `volumeDepth` = 2
        ///
        /// **ここでは判定しない。** 期待と違っても記録するだけで、
        /// 直しに行くかどうかは人が決める。
        /// </summary>
        public sealed class StereoFacts
        {
            public string StereoRenderingMode = string.Empty;
            public string EyeTextureDimension = string.Empty;
            public int EyeTextureVolumeDepth;
            public int EyeTextureWidth;
            public int EyeTextureHeight;
            public bool XrSettingsEnabled;
            public string DeviceName = string.Empty;

            public override string ToString()
                => $"stereo={StereoRenderingMode}"
                   + $" / eyeTex={EyeTextureDimension} {EyeTextureWidth}x{EyeTextureHeight}"
                   + $" volumeDepth={EyeTextureVolumeDepth}"
                   + $" / XRSettings.enabled={XrSettingsEnabled}"
                   + $" / device={DeviceName}";
        }

        /// <summary>
        /// いまの立体視の実態を読む。**値の取得だけ。判定はしない。**
        /// XR が動いていなければ「動いていない」という値がそのまま入る。
        /// </summary>
        public static StereoFacts ReadStereoFacts()
        {
            RenderTextureDescriptor eye = XRSettings.eyeTextureDesc;
            return new StereoFacts
            {
                StereoRenderingMode = XRSettings.stereoRenderingMode.ToString(),
                EyeTextureDimension = eye.dimension.ToString(),
                EyeTextureVolumeDepth = eye.volumeDepth,
                EyeTextureWidth = eye.width,
                EyeTextureHeight = eye.height,
                XrSettingsEnabled = XRSettings.enabled,
                DeviceName = XRSettings.loadedDeviceName ?? string.Empty,
            };
        }

        /// <summary>直近の起動結果。**触っていなければ null。**</summary>
        public static Result Last { get; private set; }

        /// <summary>
        /// 起動引数から読む。**純関数**（テストから直接叩ける）。
        /// 両方指定されたら実機を優先する。
        /// </summary>
        public static Mode ParseMode(IReadOnlyList<string> args)
        {
            if (args == null)
            {
                return Mode.None;
            }

            bool real = false;
            bool mock = false;
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], RealArg, StringComparison.OrdinalIgnoreCase))
                {
                    real = true;
                }
                else if (string.Equals(args[i], MockArg, StringComparison.OrdinalIgnoreCase))
                {
                    mock = true;
                }
            }

            return real ? Mode.Real : mock ? Mode.Mock : Mode.None;
        }

        public static string LoaderNameFor(Mode mode)
        {
            switch (mode)
            {
                case Mode.Real: return OpenXrLoaderName;
                case Mode.Mock: return MockLoaderName;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// **シーンが読まれる前に 1 度だけ走る。**
        /// 無指定なら即座に戻る（`XRGeneralSettings` を読みもしない）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBoot()
        {
            Mode mode = ParseMode(Environment.GetCommandLineArgs());
            if (mode == Mode.None)
            {
                // **ここで戻る。** XR には一切触らない。
                return;
            }

            Result result = Initialize(mode);
            Debug.Log("[XrBoot] " + result);
        }

        /// <summary>
        /// ローダを選んで初期化する。**mode = None では何もしない。**
        ///
        /// 成否は戻り値で返し、例外にはしない。**XR が無い環境で平面の経路まで
        /// 巻き添えにしない**ため（初期化できないこと自体は次のセッションの関門）。
        /// </summary>
        public static Result Initialize(Mode mode)
        {
            var result = new Result { Mode = mode, Requested = mode != Mode.None };
            Last = result;

            if (mode == Mode.None)
            {
                result.Message = "無指定なので XR に触っていない";
                return result;
            }

            result.LoaderName = LoaderNameFor(mode);

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                result.Message = "XRGeneralSettings が無い（ビルド対象に XR 設定が入っていない）";
                return result;
            }

            XRManagerSettings manager = settings.Manager;
            XRLoader loader = FindLoader(manager, result.LoaderName);
            result.LoaderFound = loader != null;

            if (loader == null)
            {
                result.Message = $"ローダ {result.LoaderName} が見つからない";
                return result;
            }

            // **選んだ 1 つだけを残す。** 実機用と batchmode 用が同時に載っていると、
            // どちらが初期化されたのか分からなくなる。
            var others = new List<XRLoader>(manager.activeLoaders);
            foreach (XRLoader other in others)
            {
                if (other != loader)
                {
                    manager.TryRemoveLoader(other);
                }
            }

            bool alreadyLoaded = false;
            foreach (XRLoader active in manager.activeLoaders)
            {
                if (active == loader)
                {
                    alreadyLoaded = true;
                    break;
                }
            }

            if (!alreadyLoaded)
            {
                manager.TryAddLoader(loader);
            }

            manager.InitializeLoaderSync();
            if (manager.activeLoader == null)
            {
                result.Message = "InitializeLoaderSync がローダを立ち上げられなかった";
                return result;
            }

            manager.StartSubsystems();
            result.Initialized = true;
            result.Message = "初期化した";

            // **「設定した」ではなく「経路が通っている」ことの記録。**
            // SPI なら Tex2DArray / volumeDepth = 2 になるはず。判定はしない。
            result.Facts = ReadStereoFacts();

            // **初期化した直後の値は当てにならない (Step 12-0d)。**
            // `eyeTextureDesc` は表示サブシステムが実際に描き始めるまで埋まらず、
            // XR を使っていないときと同じ既定値 (Tex2D 256x256 / volumeDepth 1) が
            // そのまま読める。**フレームが回ってから読み直す。**
            SpawnFactsLogger();

            Application.quitting += Shutdown;
            return result;
        }

        /// <summary>フレームが回ってから立体視の実態を読み直す。**判定はしない。**</summary>
        static void SpawnFactsLogger()
        {
            if (!Application.isPlaying)
            {
                // Editor の `-executeMethod` にはフレームが無い。**読める場所が無い。**
                Debug.Log("[XrBoot] 再測定は行わない（フレームの回らない実行）");
                return;
            }

            var go = new GameObject("XrFactsLogger");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<XrFactsLogger>();

            // **左右 2 枚の撮影 (Step 12-C)。** `-xrCaptureDir` が無ければ何もしない。
            // XR を立ち上げたときだけ載せる（平面の経路には出さない）。
            go.AddComponent<XrStereoCapture>();
        }

        /// <summary>**立ち上げたときだけ止める。** 触っていなければ何もしない。</summary>
        public static void Shutdown()
        {
            if (Last == null || !Last.Initialized)
            {
                return;
            }

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                return;
            }

            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
            Last.Initialized = false;
        }

        static XRLoader FindLoader(XRManagerSettings manager, string name)
        {
            foreach (XRLoader loader in manager.activeLoaders)
            {
                if (loader != null && loader.name == name)
                {
                    return loader;
                }
            }

            // まだ登録されていないローダは、プロジェクトに置かれたアセットから拾う。
            foreach (XRLoader loader in Resources.FindObjectsOfTypeAll<XRLoader>())
            {
                if (loader != null && loader.name == name)
                {
                    return loader;
                }
            }

            return null;
        }
    }
}
