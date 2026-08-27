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
    /// <summary>雲層と自転の画素検証 (Step 8-3 / 8-4)。</summary>
    public sealed class CloudSpinPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;
        /// <summary>
        /// 計器パネルが占める行数。**GetPixels32 は下から上に並ぶ。**
        /// パネルは画面下端にあるので、除外するのは y の小さい側。
        /// (以前は y < Height - PanelTop と書いており、画面下 1/3 だけを
        ///  走査していた。円盤が小さい距離では標本が 0 個になっていた)
        /// </summary>
        const int PanelRows = 380;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        ScenarioRunner _runner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-cloud.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _runner = Object.FindAnyObjectByType<ScenarioRunner>();
            Assert.That(_runner, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        CelestialBodyView EarthView()
        {
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body.Name == "Earth")
                {
                    return v;
                }
            }

            return null;
        }

        void ApplyScenario(string name)
        {
            Assert.That(_runner.Select(_root.Model, name), Is.True, name + " が無い");
            _runner.Apply(_root, _rig, _stack, Object.FindAnyObjectByType<DebugOverlay>());
            for (int i = 0; i < 20; i++)
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
                var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();
                return shot;
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>円盤の内側だけの平均輝度。計器パネルは外す。</summary>
        static float DiscMean(Texture2D shot, float radius)
        {
            Color32[] px = shot.GetPixels32();
            double sum = 0;
            int n = 0;
            for (int y = PanelRows; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float dx = x - Width * 0.5f;
                    float dy = y - Height * 0.5f;
                    if (dx * dx + dy * dy > radius * radius * 0.9f)
                    {
                        continue;
                    }

                    Color32 c = px[y * Width + x];
                    sum += Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    n++;
                }
            }

            return n > 0 ? (float)(sum / n) : 0f;
        }

        /// <summary>2 枚の円盤内の平均差。模様が動いたかを見る。</summary>
        static float DiscDiff(Texture2D a, Texture2D b, float radius)
        {
            Color32[] pa = a.GetPixels32();
            Color32[] pb = b.GetPixels32();
            double sum = 0;
            int n = 0;
            for (int y = PanelRows; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float dx = x - Width * 0.5f;
                    float dy = y - Height * 0.5f;
                    if (dx * dx + dy * dy > radius * radius * 0.8f)
                    {
                        continue;
                    }

                    int i = y * Width + x;
                    sum += Mathf.Abs(pa[i].r - pb[i].r)
                           + Mathf.Abs(pa[i].g - pb[i].g)
                           + Mathf.Abs(pa[i].b - pb[i].b);
                    n++;
                }
            }

            return n > 0 ? (float)(sum / (n * 3.0)) : 0f;
        }

        [UnityTest]
        public IEnumerator 自転はMeshの親に載っている()
        {
            yield return null;
            CelestialBodyView earth = EarthView();
            Assert.That(earth, Is.Not.Null);

            ApplyScenario(ScenarioLibrary.EarthSpinT6hName);

            Debug.Log($"[Step8-4] Spin の回転 {earth.Spin.localRotation.eulerAngles} / " +
                      $"Mesh の回転 {earth.Mesh.localRotation.eulerAngles} / " +
                      $"自転角 {earth.LastSpinDegrees:F1} 度");

            Assert.That(earth.Spin, Is.Not.Null, "Spin が無い");
            Assert.That(earth.Mesh.parent, Is.EqualTo(earth.Spin), "Mesh の親が Spin でない");
            Assert.That(Quaternion.Angle(earth.Mesh.localRotation, Quaternion.identity),
                Is.LessThan(0.01f), "Mesh 自身に回転が載っている (localScale と競合する)");
            Assert.That(earth.LastSpinDegrees, Is.GreaterThan(1f), "自転していない");
        }

        [UnityTest]
        public IEnumerator カメラ角度を振っても雲が地表の手前に出続ける()
        {
            yield return null;
            ApplyScenario(ScenarioLibrary.EarthDayName);

            CelestialBodyView earth = EarthView();
            Renderer cloud = earth.RealCloudRenderer;
            Assert.That(cloud, Is.Not.Null, "実スケールの雲が無い");

            float radius = (float)earth.LastAngularPixels * 0.5f;

            // ロールを振る。同心球なので距離ソートが同値になり、順序が不定なら
            // 角度によって雲が地表の裏に回る。
            var deltas = new System.Collections.Generic.List<float>();
            foreach (float roll in new[] { 0f, 37f, 91f, 143f, 216f, 289f })
            {
                _rig.ShipTransform.Rotate(0f, 0f, roll, Space.Self);
                _root.Tick(Dt);

                cloud.enabled = true;
                Texture2D withClouds = Render();
                cloud.enabled = false;
                Texture2D without = Render();
                cloud.enabled = true;

                float delta = DiscMean(withClouds, radius) - DiscMean(without, radius);
                deltas.Add(delta);
                Object.DestroyImmediate(withClouds);
                Object.DestroyImmediate(without);
            }

            Debug.Log("[Step8-3] ロール別の雲の寄与: " +
                      string.Join(" / ", deltas.ConvertAll(d => d.ToString("F2"))));

            foreach (float d in deltas)
            {
                Assert.That(d, Is.GreaterThan(1f),
                    "ある角度で雲が地表の裏に回った (renderQueue の順序が効いていない)");
            }
        }

        [UnityTest]
        public IEnumerator 時刻を6時間進めると地表の模様が動く()
        {
            yield return null;

            ApplyScenario(ScenarioLibrary.EarthSpinT0Name);
            CelestialBodyView earth = EarthView();
            float radius = (float)earth.LastAngularPixels * 0.5f;
            float spin0 = earth.LastSpinDegrees;
            Texture2D t0 = Render();

            ApplyScenario(ScenarioLibrary.EarthSpinT6hName);
            float spin6 = earth.LastSpinDegrees;
            Texture2D t6 = Render();

            try
            {
                float diff = DiscDiff(t0, t6, radius);
                Debug.Log($"[Step8-4] 自転角 {spin0:F1} 度 -> {spin6:F1} 度 / 円盤内の平均画素差 {diff:F2}");

                Assert.That(spin6 - spin0, Is.EqualTo(90.3f).Within(0.5f), "6 時間で 90.3 度のはず");
                Assert.That(diff, Is.GreaterThan(5f), "模様が動いていない");
            }
            finally
            {
                Object.DestroyImmediate(t0);
                Object.DestroyImmediate(t6);
            }
        }

        [UnityTest]
        public IEnumerator 同じ6時間で雲の移動量が地表より大きい()
        {
            yield return null;
            ApplyScenario(ScenarioLibrary.EarthSpinT6hName);

            CelestialBodyView earth = EarthView();
            float surface = earth.LastSpinDegrees;
            float cloud = earth.LastCloudSpinDegrees;

            Debug.Log($"[Step8-4] 6 時間で 地表 {surface:F1} 度 / 雲 {cloud:F1} 度 / 差 {cloud - surface:F1} 度");

            // 実時間 5 分では差が 1.5 px 程度でレンダリング誤差に埋もれるので 6 時間で測る。
            Assert.That(cloud, Is.GreaterThan(surface), "雲が地表より遅い");
            Assert.That(cloud - surface, Is.GreaterThan(10f), "差が小さすぎる");
        }

        [UnityTest]
        public IEnumerator 引き渡し帯で雲のアルファが地表と同じ曲線で追従する()
        {
            yield return null;
            CelestialBodyView earth = EarthView();
            Assert.That(earth.RealCloudRenderer, Is.Not.Null);

            var block = new MaterialPropertyBlock();
            int baseColor = Shader.PropertyToID("_BaseColor");

            foreach (double distance in new[] { 5.2e4, 5.0e4, 4.5e4, 4.0e4, 3.5e4, 3.0e4, 2.8e4 })
            {
                Vec3d dir = new Vec3d(1.0, 0.0, 0.0);
                _root.PlaceObserver(_root.Model.Earth.AbsolutePosition + dir * distance);
                _root.Tick(Dt);

                float surfaceAlpha = 0f;
                float cloudAlpha = 0f;

                var surfaceRenderer = earth.Mesh.parent.parent.Find("RealAnchor/RealSpin/RealMesh");
                Renderer sr = surfaceRenderer != null ? surfaceRenderer.GetComponent<Renderer>() : null;
                if (sr != null && sr.gameObject.activeInHierarchy)
                {
                    sr.GetPropertyBlock(block);
                    surfaceAlpha = block.GetColor(baseColor).a;
                }

                if (earth.RealCloudRenderer.gameObject.activeInHierarchy)
                {
                    earth.RealCloudRenderer.GetPropertyBlock(block);
                    cloudAlpha = block.GetColor(baseColor).a;
                }

                Debug.Log($"[Step8-3] 距離 {distance:E1} units / 引き渡し率 {earth.RealScaleBlend:F3} / " +
                          $"地表 α {surfaceAlpha:F3} / 雲 α {cloudAlpha:F3}");

                Assert.That(cloudAlpha, Is.EqualTo(surfaceAlpha).Within(1e-4f),
                    $"距離 {distance:E1} で雲だけ浮いている");
            }
        }
    }
}
