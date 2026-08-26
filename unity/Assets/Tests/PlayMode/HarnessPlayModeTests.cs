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
    /// <summary>検証ハーネス (Step 8-0) の PlayMode 検証。</summary>
    public sealed class HarnessPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        DebugOverlay _overlay;
        ScenarioRunner _runner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-harness.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _overlay = Object.FindAnyObjectByType<DebugOverlay>();
            _runner = Object.FindAnyObjectByType<ScenarioRunner>();

            Assert.That(_root, Is.Not.Null);
            Assert.That(_overlay, Is.Not.Null);
            Assert.That(_runner, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        static FlightInput Key(bool hud = false, bool next = false, bool prev = false)
            => new FlightInput { JumpIndex = -1, ToggleDebugHud = hud, ScenarioNext = next, ScenarioPrev = prev };

        void Press(FlightInput input)
        {
            _rig.InputOverride = input;
            _root.Tick(Dt);
            _rig.InputOverride = FlightInput.None;
            _root.Tick(Dt);
        }

        [UnityTest]
        public IEnumerator 引数なしではシナリオが動かず従来どおり開始する()
        {
            yield return null;

            Debug.Log($"[Step8-0] シナリオ活性 {_runner.IsActive} / 定義 {_runner.Count} 件 / 開始地点 {_root.StartStationName}");

            Assert.That(_runner.IsActive, Is.False, "-scenario 無しで動いてはいけない");
            Assert.That(_runner.Count, Is.GreaterThan(0), "定義自体は読めている");
            Assert.That(_root.StartStationName, Does.Contain("Earth"), "従来どおりセーブ既定から始まる");
            Assert.That(_overlay.Visible, Is.False, "HUD の既定は非表示");
            Assert.That(_overlay.BuildCheckText(), Is.Empty, "シナリオ非活性なら確認項目は出ない");
        }

        [UnityTest]
        public IEnumerator シナリオの初期状態が定義どおりに適用される()
        {
            Assert.That(_runner.Select(_root.Model, ScenarioLibrary.SelfTestName), Is.True);
            _runner.Apply(_root, _rig, _stack, _overlay);
            yield return null;

            ScenarioStart start = _runner.Current.Start;

            double dPos = Vec3d.Distance(_root.Ship.Position, start.Position);
            Vec3d want = (start.LookAt - start.Position).Normalized;
            Vector3 got = _rig.ShipTransform.forward;
            double dot = want.X * got.x + want.Y * got.y + want.Z * got.z;

            Debug.Log($"[Step8-0] 位置差 {dPos:E3} units / 機首と注視方向の内積 {dot:F6} / 目標 {_rig.TargetIndex} / 時刻 {_root.Clock.ElapsedSeconds:F3} 秒 / FOV {_stack.Deep.fieldOfView:F1} 度");

            Assert.That(dPos, Is.LessThan(1e-6), "位置");
            Assert.That(dot, Is.GreaterThan(0.9999), "姿勢");
            Assert.That(_rig.TargetIndex, Is.EqualTo(start.TargetStationIndex), "目標");
            Assert.That(_root.Clock.ElapsedSeconds, Is.EqualTo(start.ElapsedSeconds).Within(Dt), "時刻");
            Assert.That(_stack.Deep.fieldOfView, Is.EqualTo((float)start.VerticalFovDegrees).Within(0.01f), "画角");
            Assert.That(_overlay.Visible, Is.EqualTo(start.DebugHudVisible), "HUD の初期状態");
            Assert.That(_overlay.BuildCheckText(), Is.Not.Empty, "確認項目が組み立てられる");
        }

        [UnityTest]
        public IEnumerator F1でHUDの表示が反転する()
        {
            yield return null;
            Assert.That(_overlay.Visible, Is.False);

            Press(Key(hud: true));
            Assert.That(_overlay.Visible, Is.True, "1 回目で出る");

            Press(Key(hud: true));
            Assert.That(_overlay.Visible, Is.False, "2 回目で消える");

            // 押しっぱなしでは連射しない。
            _rig.InputOverride = Key(hud: true);
            _root.Tick(Dt);
            bool afterFirst = _overlay.Visible;
            _root.Tick(Dt);
            _root.Tick(Dt);
            _rig.InputOverride = null;

            Debug.Log($"[Step8-0] 押しっぱなし: 1 フレーム目 {afterFirst} / 3 フレーム目 {_overlay.Visible}");
            Assert.That(_overlay.Visible, Is.EqualTo(afterFirst), "押しっぱなしで連射しない");
        }

        [UnityTest]
        public IEnumerator F2とF3でシナリオ番号が循環する()
        {
            Assert.That(_runner.Select(_root.Model, ScenarioLibrary.SelfTestName), Is.True);
            _runner.Apply(_root, _rig, _stack, _overlay);
            yield return null;

            int start = _runner.Index;
            int count = _runner.Count;

            Press(Key(next: true));
            int afterNext = _runner.Index;

            Press(Key(prev: true));
            int afterPrev = _runner.Index;

            Debug.Log($"[Step8-0] {count} 件: 開始 {start} -> F2 {afterNext} -> F3 {afterPrev}");

            Assert.That(afterNext, Is.EqualTo((start + 1) % count));
            Assert.That(afterPrev, Is.EqualTo(start), "往復して戻る");
            Assert.That(_runner.Index, Is.InRange(0, count - 1), "範囲外に出ない");
        }

        [UnityTest]
        public IEnumerator 微振動はスラストに応じて回転しドッキング中は止まる()
        {
            CockpitShake shake = _root.Shake;
            Assert.That(shake, Is.Not.Null, "CockpitShake がシーンに無い");
            yield return null;

            _rig.InputOverride = FlightInput.None;
            _root.Tick(Dt);
            Debug.Log($"[Step8-0] スラスト 0: 振幅 {shake.LastAmplitudeRadians:E3} rad / 回転 {shake.LastEulerDegrees}");
            Assert.That(shake.LastAmplitudeRadians, Is.EqualTo(0f), "スラスト 0 では揺れない");
            Assert.That(shake.LastEulerDegrees, Is.EqualTo(Vector3.zero));

            float maxSeen = 0f;
            _rig.InputOverride = FlightInput.Forward(1f);
            for (int i = 0; i < 30; i++)
            {
                _root.Tick(Dt);
                maxSeen = Mathf.Max(maxSeen, shake.LastEulerDegrees.magnitude);
            }

            float limit = CockpitShake.MaxAmplitudeRadians * Mathf.Rad2Deg * Mathf.Sqrt(3f);
            Debug.Log($"[Step8-0] スラスト 1: 振幅 {shake.LastAmplitudeRadians:E3} rad / 回転の最大 {maxSeen:F5} 度 (上限 {limit:F5} 度)");

            Assert.That(shake.LastAmplitudeRadians, Is.EqualTo(CockpitShake.MaxAmplitudeRadians).Within(1e-9f));
            Assert.That(maxSeen, Is.GreaterThan(0f), "揺れが出ていない");
            Assert.That(maxSeen, Is.LessThanOrEqualTo(limit + 1e-4f), "上限を超えている");

            _rig.InputOverride = null;
        }

        [UnityTest]
        public IEnumerator HUDの状態はRTスクショに影響しない()
        {
            Assert.That(_runner.Select(_root.Model, ScenarioLibrary.SelfTestName), Is.True);
            _runner.Apply(_root, _rig, _stack, _overlay);
            for (int i = 0; i < 8; i++)
            {
                _root.Tick(Dt);
            }

            yield return null;

            _overlay.Visible = false;
            Texture2D off = Render();

            _overlay.Visible = true;
            Texture2D on = Render();

            try
            {
                Color32[] a = off.GetPixels32();
                Color32[] b = on.GetPixels32();
                int diff = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b)
                    {
                        diff++;
                    }
                }

                Debug.Log($"[Step8-0] HUD オフ/オンの画素差 {diff} 個 -> OnGUI は Camera.Render の RT 経路に写らない (CLAUDE.md 0-B)");
                Assert.That(diff, Is.EqualTo(0), "RT に OnGUI が写るようになったなら CLAUDE.md 0-B を更新すること");
            }
            finally
            {
                Object.DestroyImmediate(off);
                Object.DestroyImmediate(on);
                _overlay.Visible = false;
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
    }
}
