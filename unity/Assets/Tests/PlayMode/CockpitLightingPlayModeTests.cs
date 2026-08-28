using System.Collections;
using System.Collections.Generic;
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
    /// 補助光が**実際に絵を変えているか** (Step 11-4)。
    ///
    /// **「効いていない」が失敗として出ることが目的。** 強度 0 や当て先 0 件でも
    /// 絵は出てしまうので、コンポーネントの状態だけを見るテストでは足りない。
    /// 画素で見る。
    /// </summary>
    public sealed class CockpitLightingPlayModeTests
    {
        const int Width = 960;
        const int Height = 540;
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        CameraStackController _stack;
        CockpitLights _lights;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath =
                Path.Combine(Path.GetTempPath(), "solar-system-explorer-lighting.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _lights = Object.FindAnyObjectByType<CockpitLights>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        void RequireLights()
        {
            if (_lights == null || _lights.Fill == null)
            {
                Assert.Inconclusive("補助光が組まれていない（箱コックピット）");
            }
        }

        [UnityTest]
        public IEnumerator 補助光を切ると内装の画素が変わる()
        {
            yield return null;
            RequireLights();

            Settle();

            _lights.SetFillEnabled(true);
            _lights.SetFillIntensity((float)CockpitDefinition.DefaultFillLightIntensity);
            Color32[] on = Render();

            _lights.SetFillEnabled(false);
            Color32[] off = Render();

            _lights.SetFillEnabled(true);

            int changed = 0;
            for (int i = 0; i < on.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(on[i].r - off[i].r),
                                  Mathf.Max(Mathf.Abs(on[i].g - off[i].g),
                                            Mathf.Abs(on[i].b - off[i].b)));
                if (d > 2)
                {
                    changed++;
                }
            }

            Debug.Log($"  [Step11-4] 補助光 ON/OFF で変わった画素 {changed} / {on.Length}");

            // **効いていなければここで落ちる。** 強度 0・当て先 0 件・Bind 漏れは
            // すべて「変わらない」として現れる。
            Assert.That(changed, Is.GreaterThan(on.Length / 100),
                        "補助光を切っても絵が変わらない（効いていない）");
        }

        [UnityTest]
        public IEnumerator 暗い場面で内装が真っ黒に潰れていない()
        {
            yield return null;
            RequireLights();

            var runner = Object.FindAnyObjectByType<ScenarioRunner>();
            var rig = Object.FindAnyObjectByType<ShipRig>();
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            Assert.That(runner, Is.Not.Null);

            foreach (string scenario in new[] { "earth-close-night", "sun-hidden" })
            {
                Assert.That(runner.Select(_root.Model, scenario), Is.True, scenario);
                runner.Apply(_root, rig, _stack, overlay);
                Settle();

                Color32[] lit = Render();
                Color32[] hidden = RenderWithoutCockpit();

                var values = new List<double>();
                for (int i = 0; i < lit.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(lit[i].r - hidden[i].r),
                                      Mathf.Max(Mathf.Abs(lit[i].g - hidden[i].g),
                                                Mathf.Abs(lit[i].b - hidden[i].b)));
                    if (d > 2)
                    {
                        values.Add(CockpitLighting.Luminance(lit[i].r, lit[i].g, lit[i].b));
                    }
                }

                CockpitLighting.Result result = CockpitLighting.Measure(values);
                Debug.Log($"  [Step11-4] {scenario}: {result}");

                Assert.That(CockpitLighting.WithinBudget(result), Is.True,
                            $"{scenario}: 内装が潰れている ({result})");
            }
        }

        void Settle(int frames = 12)
        {
            for (int i = 0; i < frames; i++)
            {
                _root.Tick(Dt);
            }
        }

        Color32[] RenderWithoutCockpit()
        {
            int previous = _stack.Cockpit.cullingMask;
            try
            {
                // **カメラは回したまま mask だけ空にする** (CLAUDE.md §0-B)。
                _stack.Cockpit.cullingMask = 0;
                return Render();
            }
            finally
            {
                _stack.Cockpit.cullingMask = previous;
            }
        }

        Color32[] Render()
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevDeep = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Deep.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();

                Color32[] pixels = shot.GetPixels32();
                Object.DestroyImmediate(shot);
                return pixels;
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
