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
    /// <summary>惑星シェーダの画素検証 (Step 8-2)。</summary>
    public sealed class PlanetPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        /// <summary>計器パネルが占める帯。画素検証から外す。</summary>
        const int PanelTop = 700;

        /// <summary>
        /// 画面中央に出る Directional Light の SRP Lens Flare を検証から外す半径。
        /// 夜側は太陽が地球の真後ろに来るため、フレアが画面中央 38x38 px に描かれる。
        /// 惑星は Transparent / ZWrite off で深度を書かないので遮蔽されない
        /// (docs/02-demo2-plan.md 9-3 の確定事項)。Step 9-3 で引き渡し率による
        /// 減衰を入れるまでの回避。
        /// </summary>
        const int FlareGuardRadius = 40;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        ScenarioRunner _runner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-planet.save.json");
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

        Texture2D RenderScenario(string name)
        {
            Assert.That(_runner.Select(_root.Model, name), Is.True, name + " が無い");
            _runner.Apply(_root, _rig, _stack, Object.FindAnyObjectByType<DebugOverlay>());
            for (int i = 0; i < 20; i++)
            {
                _root.Tick(Dt);
            }

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

        /// <summary>円盤の見かけの半径 [px]。</summary>
        float DiscRadius()
        {
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body.Name == "Earth")
                {
                    return (float)v.LastAngularPixels * 0.5f;
                }
            }

            return 356f;
        }

        [UnityTest]
        public IEnumerator 昼側では円盤の縁が中心より青い()
        {
            yield return null;
            Texture2D shot = RenderScenario(ScenarioLibrary.EarthDayName);
            try
            {
                float r = DiscRadius();
                Color32[] px = shot.GetPixels32();

                double rimB = 0, coreB = 0;
                int rimN = 0, coreN = 0;
                for (int y = 0; y < Height - PanelTop; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        float dx = x - Width * 0.5f;
                        float dy = y - Height * 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        Color32 c = px[y * Width + x];

                        if (d >= r - 3f && d <= r)
                        {
                            rimB += c.b;
                            rimN++;
                        }
                        else if (d <= r * 0.25f)
                        {
                            coreB += c.b;
                            coreN++;
                        }
                    }
                }

                rimB /= Mathf.Max(1, rimN);
                coreB /= Mathf.Max(1, coreN);
                Debug.Log($"[Step8-2] 昼側 縁3px の青 {rimB:F1} (n={rimN}) / 中心の青 {coreB:F1} (n={coreN}) / 差 {rimB - coreB:+0.0;-0.0}");

                Assert.That(rimN, Is.GreaterThan(1000), "縁の画素が取れていない");
                Assert.That(rimB, Is.GreaterThan(coreB), "大気の縁が中心より青くない");
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }
        }

        [UnityTest]
        public IEnumerator 夜側に街灯りの画素がある()
        {
            yield return null;
            Texture2D shot = RenderScenario(ScenarioLibrary.EarthNightName);
            try
            {
                float r = DiscRadius();
                Color32[] px = shot.GetPixels32();

                int inside = 0;
                int lit = 0;
                for (int y = 0; y < Height - PanelTop; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        float dx = x - Width * 0.5f;
                        float dy = y - Height * 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);

                        // 中央はレンズフレアが乗るので除外する。
                        if (d > r * 0.97f || d < FlareGuardRadius)
                        {
                            continue;
                        }

                        inside++;
                        Color32 c = px[y * Width + x];
                        if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 16)
                        {
                            lit++;
                        }
                    }
                }

                double ratio = 100.0 * lit / Mathf.Max(1, inside);
                Debug.Log($"[Step8-2] 夜側 円盤内 {inside} 画素 / 輝度>16 が {lit} 画素 ({ratio:F3}%)");

                Assert.That(inside, Is.GreaterThan(10000), "円盤が取れていない");
                Assert.That(ratio, Is.GreaterThan(0.05), "街灯りが出ていない");
                Assert.That(ratio, Is.LessThan(20.0), "夜側が明るすぎる (昼側を見ている可能性)");
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }
        }

        [UnityTest]
        public IEnumerator 明暗境界線をまたいで輝度が段差なく変わる()
        {
            yield return null;
            Texture2D shot = RenderScenario(ScenarioLibrary.EarthTerminatorName);
            try
            {
                float r = DiscRadius();
                Color32[] px = shot.GetPixels32();

                // 円盤の中心を通る水平線をたどる。
                int row = Height / 2;
                var profile = new System.Collections.Generic.List<float>();
                for (int x = (int)(Width * 0.5f - r + 20); x < Width * 0.5f + r - 20; x += 8)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        Color32 c = px[(row + dy) * Width + x];
                        sum += Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        n++;
                    }

                    profile.Add(sum / n);
                }

                // 段差 = 隣り合う標本の差。街灯りが段で立ち上がると大きな跳ねが出る。
                float maxJump = 0f;
                for (int i = 1; i < profile.Count; i++)
                {
                    maxJump = Mathf.Max(maxJump, Mathf.Abs(profile[i] - profile[i - 1]));
                }

                float lit = profile[0];
                float dark = profile[profile.Count - 1];
                Debug.Log($"[Step8-2] 明暗境界 標本 {profile.Count} 点 / 明側の端 {lit:F1} / 暗側の端 {dark:F1} / 最大の段差 {maxJump:F1}");

                Assert.That(profile.Count, Is.GreaterThan(30), "標本が足りない");
                Assert.That(lit, Is.GreaterThan(dark + 20f), "明暗の差が出ていない");
                Assert.That(maxJump, Is.LessThan(120f), "境界で輝度が段差になっている");
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }
        }
    }
}
