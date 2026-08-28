using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// 計器が 5 面に映っていること (Step 11-3)。
    ///
    /// **アセットが無いクローンでは Inconclusive**（画面はアセット側のメッシュなので、
    /// 箱で組まれた環境には存在しない）。
    /// </summary>
    public sealed class CockpitScreenPlayModeTests
    {
        CockpitScreens _screens;
        CockpitIdentity _identity;
        UniverseRoot _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(),
                                                 "solar-system-explorer-screens.save.json");
            SaveFile.Delete();
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _screens = Object.FindAnyObjectByType<CockpitScreens>();
            _identity = Object.FindAnyObjectByType<CockpitIdentity>();
            _root = Object.FindAnyObjectByType<UniverseRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        void RequireScreens()
        {
            Assert.That(_screens, Is.Not.Null, "CockpitScreens がシーンに無い");

            if (_screens.Screens.Count == 0)
            {
                Assert.Inconclusive(
                    $"画面が組まれていない（コックピットは {_identity?.DefinitionId}）。"
                    + "取り込み済みのマシンで SolarSetup.Run を回すこと。");
            }
        }

        [UnityTest]
        public IEnumerator 五面のMPBにRTが入っている()
        {
            yield return null;
            RequireScreens();

            var block = new MaterialPropertyBlock();
            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                // **既定は逆歪ませ (11-3c)。** 面が読むのは歪ませたほうの RT。
                RenderTexture expected = _screens.Mode == ScreenMode.Prewarp
                    ? screen.Warped
                    : screen.Texture;

                screen.Target.GetPropertyBlock(block);

                Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(expected),
                            screen.RendererName + ": _BaseMap に RT が入っていない");

                // **_BaseMap と _EmissionMap の両方に差す。** 片方だけだと
                // ベンダーの画面絵が発光側に残って二重写しになる（11-1c の実測）。
                Assert.That(block.GetTexture("_EmissionMap"), Is.EqualTo(expected),
                            screen.RendererName + ": _EmissionMap に RT が入っていない");
            }
        }

        [UnityTest]
        public IEnumerator 面ごとにSTが違いマテリアルは共有のまま()
        {
            yield return null;
            RequireScreens();

            var block = new MaterialPropertyBlock();
            var seen = new System.Collections.Generic.List<Vector4>();

            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                screen.Target.GetPropertyBlock(block);
                seen.Add(block.GetVector("_BaseMap_ST"));

                // **共有マテリアルのまま。** 複製していたら name に " (Instance)" が付く。
                Assert.That(screen.Target.sharedMaterial.name, Does.Not.Contain("Instance"),
                            screen.RendererName + ": マテリアルが複製されている");
            }

            Assert.That(seen.Distinct().Count(), Is.EqualTo(seen.Count),
                        "ST が面ごとに違わない（別の UV 矩形を指せていない）");
        }

        [UnityTest]
        public IEnumerator テスト柄に切り替えると描画元が入れ替わる()
        {
            yield return null;
            RequireScreens();

            // **割り当ては案 A で確定 (11-3c)。** 入れ替わるのは計器とテスト柄だけ。
            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                Assert.That(Enabled(screen.CameraA), Is.True, "計器の Canvas が止まっている");
                Assert.That(Enabled(screen.CameraPattern), Is.False,
                            "テスト柄の Canvas が動いている");
            }

            _screens.SetPattern(true);
            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                Assert.That(Enabled(screen.CameraA), Is.False, "計器の Canvas が動いている");
                Assert.That(Enabled(screen.CameraPattern), Is.True,
                            "テスト柄の Canvas が止まっている");
            }

            _screens.SetPattern(false);
        }

        static bool Enabled(Camera cam)
            => cam != null && cam.GetComponentInChildren<Canvas>(true).enabled;

        [UnityTest]
        public IEnumerator 計器カメラは常時有効でなく10Hzで描かれる()
        {
            yield return null;
            RequireScreens();

            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                Assert.That(screen.CameraA.enabled, Is.False, "計器のカメラが常時有効");
                Assert.That(screen.CameraPattern.enabled, Is.False,
                            "テスト柄のカメラが常時有効");
            }

            int before = _screens.RenderCount;

            // 0.25 秒ぶん進める。10 Hz なら 5 面 x 2〜3 回。
            for (int i = 0; i < 15; i++)
            {
                _root.Tick(1.0 / 60.0);
            }

            int drawn = _screens.RenderCount - before;
            Assert.That(drawn, Is.GreaterThan(0), "1 度も描かれていない");
            Assert.That(drawn, Is.LessThanOrEqualTo(_screens.Screens.Count * 4),
                        $"毎フレーム描いている疑い ({drawn} 回)");

            Debug.Log($"  [Step11-3] 0.25 秒で描画 {drawn} 回 / 面 {_screens.Screens.Count}");
        }

        [UnityTest]
        public IEnumerator 逆歪ませのblitも10Hzに乗る()
        {
            yield return null;
            RequireScreens();

            // **毎フレーム走らせない。** 計器を描き直した回にだけ blit する。
            _screens.SetMode(ScreenMode.Prewarp);
            int beforeWarp = _screens.WarpCount;
            int beforeRender = _screens.RenderCount;

            for (int i = 0; i < 15; i++)
            {
                _root.Tick(1.0 / 60.0);
            }

            int warped = _screens.WarpCount - beforeWarp;
            int drawn = _screens.RenderCount - beforeRender;

            Assert.That(warped, Is.GreaterThan(0), "1 度も歪ませていない");
            Assert.That(warped, Is.EqualTo(drawn),
                        $"描いた回数 {drawn} と blit の回数 {warped} が合わない");

            Debug.Log($"  [Step11-3c] 0.25 秒で blit {warped} 回 / 描画 {drawn} 回");
        }

        [UnityTest]
        public IEnumerator 逆歪ませでは面が歪ませたRTを読む()
        {
            yield return null;
            RequireScreens();

            var block = new MaterialPropertyBlock();
            CockpitScreens.Screen screen = _screens.Screens[0];

            _screens.SetMode(ScreenMode.OnFace);
            screen.Target.GetPropertyBlock(block);
            Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(screen.Texture),
                        "面に貼るのに歪ませたほうを読んでいる");

            _screens.SetMode(ScreenMode.Prewarp);
            screen.Target.GetPropertyBlock(block);
            Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(screen.Warped),
                        "逆歪ませなのに元の RT を読んでいる");

            // **行列は .mat に保存されない。** しかも未設定の行列を読むと
            // **単位行列**が返るので、入れ忘れると blit が素通し（1:1 コピー）に
            // なり、「逆歪ませが効いていないのに絵は出ている」形で黙って失敗する
            // （EditMode の撮影経路で実際に踏んだ / 11-3c）。
            // 出所は `Screen.Warp` ただ 1 つで、blit のたびに入れ直す。
            Assert.That(screen.Warp, Is.Not.EqualTo(Matrix4x4.identity),
                        "行列が単位行列（未設定と見分けが付かない）");

            _root.Tick(1.0);

            Assert.That(screen.WarpMaterial.GetMatrix(CockpitScreens.WarpProperty),
                        Is.EqualTo(screen.Warp), "blit の行列が入っていない");
        }

        [UnityTest]
        public IEnumerator 発光強度がMPBに反映される()
        {
            yield return null;
            RequireScreens();

            _screens.SetEmission(1.25f);

            var block = new MaterialPropertyBlock();
            _screens.Screens[0].Target.GetPropertyBlock(block);

            Assert.That(block.GetColor("_EmissionColor").r, Is.EqualTo(1.25f).Within(1e-4f));

            _screens.SetEmission((float)CockpitDefinition.DefaultScreenEmission);
        }

        [UnityTest]
        public IEnumerator 視線を動かしても画面が視界に固定されている()
        {
            yield return null;
            RequireScreens();

            // **実機の「文字が波打つ」の観測経路 (Step 11-3b)。**
            // 計器の面はコックピットの一部なので、視線を動かしても**画面上の位置は
            // 動かないはず。** 1 フレームでもずれていれば、縮小されたテクスチャの
            // 標本点が毎フレーム入れ替わり、静止画では出ない揺れになる。
            var rig = Object.FindAnyObjectByType<ShipRig>();
            var stack = Object.FindAnyObjectByType<CameraStackController>();
            Camera cam = stack.Cockpit;

            Renderer target = _screens.Screens[0].Target;
            Vector3 corner = target.GetComponent<MeshFilter>().sharedMesh.vertices[0];

            var samples = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < 10; i++)
            {
                rig.InputOverride = new FlightInput { LookMouse = new Vector2(2f, 0f) };
                yield return null;

                Vector3 world = target.transform.TransformPoint(corner);
                samples.Add(cam.transform.InverseTransformPoint(world));
            }

            rig.InputOverride = null;

            float maxShift = 0f;
            for (int i = 1; i < samples.Count; i++)
            {
                maxShift = Mathf.Max(maxShift, (samples[i] - samples[0]).magnitude);
            }

            // カメラ座標での 1 m は、この距離で画面上の何 px か。
            float pxPerMeter = Screen.height
                               / (2f * samples[0].z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

            Debug.Log($"  [Step11-3b] 視線を回したときの面のずれ {maxShift * 1000f:F4} mm"
                      + $" = 画面上 {maxShift * pxPerMeter:F4} px（10 フレーム）");

            Assert.That(maxShift * pxPerMeter, Is.LessThan(0.01f),
                        "面が視界に対して動いている（縮小テクスチャの揺れの原因になる）");
        }

        [UnityTest]
        public IEnumerator 音量が定数どおり()
        {
            yield return null;

            // **F4 から音量の項目を外した (11-3)。** 外しても値が変わらないこと。
            var routing = Object.FindAnyObjectByType<AudioRouting>();
            Assert.That(routing, Is.Not.Null);

            Assert.That(routing.VolumeOf(AudioGroup.Master),
                        Is.EqualTo(AudioMix.MasterVolume).Within(1e-6));
            Assert.That(routing.VolumeOf(AudioGroup.Engine),
                        Is.EqualTo(AudioMix.EngineVolume).Within(1e-6));
            Assert.That(routing.VolumeOf(AudioGroup.Cockpit),
                        Is.EqualTo(AudioMix.CockpitVolume).Within(1e-6));
            Assert.That(routing.VolumeOf(AudioGroup.Sfx),
                        Is.EqualTo(AudioMix.SfxVolume).Within(1e-6));
        }
    }
}
