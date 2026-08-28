using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// 計器パネルの配置 (Step 7)。
    ///
    /// 要件 §1「眺めの美しさを優先」。5 行の縦積みが視界中央を塞いでいたので、
    /// 下端寄せ・2 行・半透明にした。惑星が隠れないことと、
    /// それでも文字が読めることの両方を絵で示す。
    /// </summary>
    public sealed class HudPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;

        static string ShotDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify/shots"));

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-hud.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            Directory.CreateDirectory(ShotDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        void AimShip(Vec3d direction)
        {
            var f = new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z);
            if (f.sqrMagnitude > 0f)
            {
                _rig.ShipTransform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }
        }

        void Settle(int frames = 12)
        {
            for (int i = 0; i < frames; i++)
            {
                _root.Tick(Dt);
            }
        }

        Texture2D Render()
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevDeep = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Near.targetTexture = null;
                _stack.Deep.Render();

                RenderTexture.active = rt;
                var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                png.Apply();
                return png;
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        void Save(Texture2D texture, string name)
        {
            File.WriteAllBytes(Path.Combine(ShotDirectory, $"{name}.png"), texture.EncodeToPNG());
        }

        /// <summary>指定した矩形の平均輝度。</summary>
        static float MeanLuma(Texture2D texture, int x, int y, int w, int h)
        {
            Color[] pixels = texture.GetPixels(x, y, w, h);
            float sum = 0f;
            foreach (Color c in pixels)
            {
                sum += c.grayscale;
            }

            return sum / pixels.Length;
        }

        // ---- (b) 惑星が隠れないこと ----

        [UnityTest]
        public IEnumerator 火星が最大に見える位置でパネルに隠れない()
        {
            SpaceStation mars = _root.Model.Stations[1];
            _rig.SetTargetIndex(1);

            Vec3d arrival = mars.AbsolutePosition
                            + mars.PortDirection * UniverseConstants.ArrivalRadiusUnits;
            _root.PlaceObserver(arrival);
            AimShip((mars.Host.AbsolutePosition - arrival).Normalized);
            Settle();
            yield return null;

            Texture2D shot = Render();
            try
            {
                Save(shot, "7_01_hud_mars");

                // 画面を上下に割って、パネルが占める帯を測る。
                // 視界の中央 (上 3/4) にパネルが無いことを数値で言う。
                float upper = MeanLuma(shot, 0, Height / 4, Width, Height * 3 / 4);
                float lower = MeanLuma(shot, 0, 0, Width, Height / 4);

                // パネルの中心付近 (下から約 22%) に文字があるはず。
                float panelBand = MeanLuma(shot, Width / 4, (int)(Height * 0.13f), Width / 2, (int)(Height * 0.14f));

                Debug.Log($"[Step7] 画面上 3/4 の平均輝度 {upper:F4} / 下 1/4 {lower:F4} / " +
                          $"パネル帯 {panelBand:F4}");
                Assert.That(panelBand, Is.GreaterThan(0f), "パネルが描かれていない");

                // 火星の円盤が中央にあること。中央の縦帯が背景 (ほぼ黒) より明るい。
                float marsBand = MeanLuma(shot, (int)(Width * 0.30f), (int)(Height * 0.45f),
                                          (int)(Width * 0.20f), (int)(Height * 0.25f));
                Debug.Log($"[Step7] 火星の円盤 {marsBand:F4}");
                Assert.That(marsBand, Is.GreaterThan(0.01f), "火星が見えていない");
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }
        }

        // ---- 半透明であること ----

        [UnityTest]
        public IEnumerator パネルの背景は半透明で文字は不透明()
        {
            SpaceStation mars = _root.Model.Stations[1];
            _rig.SetTargetIndex(1);

            // 明るい天の川を背にした絵。ここで文字が読めれば最悪ケースを越えている。
            Vec3d arrival = mars.AbsolutePosition
                            + mars.PortDirection * UniverseConstants.ArrivalRadiusUnits;
            _root.PlaceObserver(arrival);
            AimShip((_root.Model.Sun.AbsolutePosition - arrival).Normalized);
            Settle();
            yield return null;

            Texture2D bright = Render();
            try
            {
                Save(bright, "7_02_hud_readable");
            }
            finally
            {
                Object.DestroyImmediate(bright);
            }

            // 透け具合は計器 RenderTexture のアルファを直接測る。
            // 画面から測ると、背景の明るさと文字の明るさが混ざって切り分けられない。
            //
            // **測る先は帯から HUD の面へ移した (11-3c)。** 帯は撤去され、
            // 箱コックピットのときだけ出る。透過の性質は HUD のガラス面が
            // 引き継いでいる（透明マテリアルの面だけ背景を透かす）。
            var screens = Object.FindAnyObjectByType<CockpitScreens>();
            Assert.That(screens, Is.Not.Null, "計器の画面が無い");

            RenderTexture source = null;
            foreach (CockpitScreens.Screen screen in screens.Screens)
            {
                if (screen.Transparent)
                {
                    source = screen.Texture;
                    break;
                }
            }

            if (source == null)
            {
                Assert.Inconclusive("透明な面が無い（箱コックピット）");
            }

            RenderTexture prevActive = RenderTexture.active;
            var readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readback.Apply();

                Color[] pixels = readback.GetPixels();
                float minAlpha = 1f;
                float maxAlpha = 0f;
                int background = 0;
                int opaque = 0;

                foreach (Color c in pixels)
                {
                    minAlpha = Mathf.Min(minAlpha, c.a);
                    maxAlpha = Mathf.Max(maxAlpha, c.a);

                    if (c.a < 0.9f)
                    {
                        background++;
                    }
                    else
                    {
                        opaque++;
                    }
                }

                float backgroundRatio = (float)background / pixels.Length;

                Debug.Log($"[Step7] 計器 RT {source.width}x{source.height}: " +
                          $"アルファ最小 {minAlpha:F3} / 最大 {maxAlpha:F3} / " +
                          $"半透明の画素 {backgroundRatio * 100f:F1}% ({background} 個) / " +
                          $"不透明 {opaque} 個");

                // **背景は完全に透過する (11-3b で 0.55 -> 0.00)。**
                // 帯のときは黒い板の上に描いていたので半透明で足りたが、機内の
                // ガラス面に貼ると、わずかでも不透明だと「半透明の板が浮いている」
                // ように見えた（実機で確認）。文字だけが浮かぶ形にした。
                Assert.That(minAlpha, Is.LessThan(0.02f), "背景が透けていない");
                Assert.That(maxAlpha, Is.GreaterThan(0.95f), "文字まで透けている");
                Assert.That(backgroundRatio, Is.GreaterThan(0.5f), "面積の大半は背景のはず");
            }
            finally
            {
                RenderTexture.active = prevActive;
                Object.DestroyImmediate(readback);
            }
        }

        // ---- (d) exe と同じ絵になること ----

        [UnityTest]
        public IEnumerator 既定の開始地点をEditorでも撮る()
        {
            // exe は引数無しで起動すると、セーブが無いので地球ステーションから始まる。
            // それと同じ状態を Editor 側でも作って、絵を突き合わせられるようにする。
            _root.StartAtStation(0);
            Settle();
            yield return null;

            Debug.Log($"[Step7] Editor 側の開始地点: {_root.StartStationName} / " +
                      $"目標 {_rig.TargetIndex} / 速度 {_root.Ship.SpeedKmPerSec:F3} km/s");

            Texture2D shot = Render();
            try
            {
                Save(shot, "7_04_editor_start");
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }

            Assert.That(_root.StartStationName, Does.Contain("Earth"));
        }
    }
}
