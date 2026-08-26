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
    /// 手動操作 (Step 3a) の PlayMode 検証。実際に Play して描画まで通す。
    ///
    /// **キー入力そのものは batchmode では再現できなかった。**
    /// Input System のプレイヤー側状態が更新されず、QueueStateEvent も
    /// InputState.Change も効かない (どちらも rKey.isPressed が False のまま)。
    /// そこで ShipRig.Apply へ素の FlightInput を渡す — キーを押したときに
    /// ReadInput が作るのと同じ値。キー割り当てが正しいことは
    /// EditMode の ManualControlsTests が .inputactions を直接読んで担保している。
    ///
    /// スクショは Deep カメラを RenderTexture へ描いて保存する。
    /// OnGUI のデバッグ表示はカメラの RT には乗らないので、
    /// 数値は DebugOverlay.BuildText() をログへ出す。
    /// </summary>
    public sealed class ManualFlightPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = 1.0 / 60.0;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        DebugOverlay _overlay;

        static string ShotDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify/shots"));

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _overlay = Object.FindAnyObjectByType<DebugOverlay>();

            Assert.That(_root, Is.Not.Null, "UniverseRoot が無い");
            Assert.That(_rig, Is.Not.Null, "ShipRig が無い");
            Assert.That(_stack, Is.Not.Null, "CameraStackController が無い");
            Assert.That(_overlay, Is.Not.Null, "DebugOverlay が無い");
        }

        /// <summary>
        /// キーを 1 回叩いたのと同じ入力列 (押す -> 離す)。
        /// UniverseRoot.Tick 経由なので、本番とまったく同じ呼び出し順を通る。
        /// </summary>
        void Tap(FlightInput pressed) => Hold(pressed, 1);

        void Hold(FlightInput input, int frames)
        {
            _rig.InputOverride = input;
            for (int i = 0; i < frames; i++)
            {
                _root.Tick(Dt);
            }

            _rig.InputOverride = FlightInput.None;
            _root.Tick(Dt);
            _rig.InputOverride = null;
        }

        [UnityTest]
        public IEnumerator デバッグジャンプで各距離へ飛べてスクショが撮れる()
        {
            Directory.CreateDirectory(ShotDirectory);

            for (int i = 0; i < DebugJumpTable.Count; i++)
            {
                Tap(FlightInput.Jump(i));
                yield return null;

                double expected = DebugJumpTable.Distances[i];
                double actual = _root.Model.Mars.DistanceFrom(_root.Ship.Position);

                Assert.That(_rig.LastJumpIndex, Is.EqualTo(i), $"段 {i + 1} が拾われていない");
                Assert.That(actual, Is.EqualTo(expected).Within(expected * 1e-9),
                    $"段 {i + 1} の到達距離がずれている");
                Assert.That(_rig.Dial.Index, Is.EqualTo(0), "ジャンプ後は STOP に戻る");

                CelestialBodyView mars = _root.SolarSystem.Find("Mars");
                Capture($"3a_key{i + 1}_{expected:0.###e+0}");

                Debug.Log(
                    $"[Step3a] 段{i + 1} -> 火星まで {actual:E4} units / " +
                    $"角直径 {mars.LastAngularPixels:F3} px / " +
                    $"point {(mars.Lod.PointActive ? "ON " : "off")} mesh {(mars.Lod.MeshActive ? "ON " : "off")} " +
                    $"blend {mars.Lod.Blend:F3}\n" +
                    $"[Step3a] --- デバッグ表示 ---\n{_overlay.BuildText()}");
            }
        }

        [UnityTest]
        public IEnumerator ダイヤルで速度段が上下する()
        {
            Assert.That(_rig.Dial.Index, Is.EqualTo(0));

            for (int i = 0; i < 3; i++)
            {
                Tap(new FlightInput { DialUp = true, JumpIndex = -1 });
            }

            Assert.That(_rig.Dial.Index, Is.EqualTo(3));
            Assert.That(_rig.Dial.Current.Label, Is.EqualTo("1 km/s"));
            Assert.That(_rig.Dial.IsManualRegime, Is.True);

            Tap(new FlightInput { DialUp = true, JumpIndex = -1 });
            Assert.That(_rig.Dial.Current.ShowAsBeta, Is.True, "1 km/s を超えたら c 表記へ移る");
            Assert.That(_rig.Dial.IsManualRegime, Is.False);

            Tap(new FlightInput { DialDown = true, JumpIndex = -1 });
            Assert.That(_rig.Dial.Index, Is.EqualTo(3));

            Debug.Log($"[Step3a] ダイヤル操作後: {_rig.Dial.Current.Label} (段 {_rig.Dial.Index})");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 押しっぱなしでは段が1つしか動かない()
        {
            // 立ち上がりだけを拾うこと。押しっぱなしで段が滑ったら操作にならない。
            _rig.InputOverride = new FlightInput { DialUp = true, JumpIndex = -1 };
            for (int i = 0; i < 30; i++)
            {
                _root.Tick(Dt);
            }

            _rig.InputOverride = null;

            Assert.That(_rig.Dial.Index, Is.EqualTo(1), "押しっぱなしで段が滑っている");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 前進で火星に近づく()
        {
            Tap(FlightInput.Jump(5)); // 1e4 units
            double before = _root.Model.Mars.DistanceFrom(_root.Ship.Position);

            for (int i = 0; i < 3; i++)
            {
                Tap(new FlightInput { DialUp = true, JumpIndex = -1 }); // 1 km/s
            }

            Assert.That(_rig.Dial.Current.KmPerSec, Is.EqualTo(1.0));

            Hold(FlightInput.Forward(1f), 120);
            yield return null;

            double after = _root.Model.Mars.DistanceFrom(_root.Ship.Position);
            double expectedTravel = 1.0 * 120 * Dt; // 1 km/s x 2 秒 = 2 units

            Debug.Log($"[Step3a] 前進 120 フレーム (1 km/s): {before:F4} -> {after:F4} units " +
                      $"(進んだ {before - after:F4} / 理論値 {expectedTravel:F4})");

            Assert.That(before - after, Is.EqualTo(expectedTravel).Within(0.05));
            Assert.That(_rig.LastThrust, Is.EqualTo(0f), "離したらスラストが 0 に戻る");
        }

        [UnityTest]
        public IEnumerator 停止中は前進しても動かない()
        {
            Tap(FlightInput.Jump(4)); // 2e4 units、ダイヤルは STOP
            double before = _root.Model.Mars.DistanceFrom(_root.Ship.Position);

            Hold(FlightInput.Forward(1f), 30);
            yield return null;

            double after = _root.Model.Mars.DistanceFrom(_root.Ship.Position);
            Assert.That(after, Is.EqualTo(before).Within(1e-9), "STOP なら動かない");
        }

        [UnityTest]
        public IEnumerator ロールとピッチとヨーで姿勢が変わる()
        {
            Tap(FlightInput.Jump(4));

            Quaternion start = _stack.Deep.transform.rotation;

            Hold(new FlightInput { Roll = 1f, JumpIndex = -1 }, 30);
            Quaternion afterRoll = _stack.Deep.transform.rotation;

            Hold(new FlightInput { LookKeys = new Vector2(0f, 1f), JumpIndex = -1 }, 30);
            Quaternion afterPitch = _stack.Deep.transform.rotation;

            Hold(new FlightInput { LookKeys = new Vector2(1f, 0f), JumpIndex = -1 }, 30);
            Quaternion afterYaw = _stack.Deep.transform.rotation;

            float rollDeg = Quaternion.Angle(start, afterRoll);
            float pitchDeg = Quaternion.Angle(afterRoll, afterPitch);
            float yawDeg = Quaternion.Angle(afterPitch, afterYaw);

            // 30 フレーム x 1/60 秒 = 0.5 秒。ロール 60 度/秒 -> 30 度、旋回 45 度/秒 -> 22.5 度。
            Debug.Log($"[Step3a] 0.5 秒ぶん: ロール {rollDeg:F2} 度 / ピッチ {pitchDeg:F2} 度 / ヨー {yawDeg:F2} 度");

            Assert.That(rollDeg, Is.EqualTo(30f).Within(1f));
            Assert.That(pitchDeg, Is.EqualTo(22.5f).Within(1f));
            Assert.That(yawDeg, Is.EqualTo(22.5f).Within(1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator マウスでも旋回できる()
        {
            Tap(FlightInput.Jump(4));
            Quaternion start = _stack.Deep.transform.rotation;

            _rig.InputOverride = new FlightInput { LookMouse = new Vector2(100f, 0f), JumpIndex = -1 };
            _root.Tick(Dt);
            _rig.InputOverride = null;

            float deg = Quaternion.Angle(start, _stack.Deep.transform.rotation);
            Debug.Log($"[Step3a] マウス 100 px で {deg:F2} 度 (感度 {ShipRig.MouseDegreesPerPixel} 度/px)");

            Assert.That(deg, Is.EqualTo(100f * ShipRig.MouseDegreesPerPixel).Within(0.1f));
            yield return null;
        }

        // ================= Step 3b =================

        [UnityTest]
        public IEnumerator 実スケール引き渡しで近距離の破綻が直る()
        {
            Directory.CreateDirectory(ShotDirectory);
            CelestialBodyView mars = _root.SolarSystem.Find("Mars");

            // 段6(1e4) / 段7(5e3) / 段8(3e3) — 3a で破綻していた距離。
            foreach (int index in new[] { 5, 6, 7 })
            {
                Tap(FlightInput.Jump(index));
                yield return null;

                double d = DebugJumpTable.Distances[index];

                // 引き渡し前 = 引き渡しを切って Step 3a と同じ「プロキシ殻だけ」に戻した絵。
                _root.SolarSystem.HandoffEnabled = false;
                _root.Tick(Dt);
                Capture($"3b_key{index + 1}_{d:0.###e+0}_before");

                _root.SolarSystem.HandoffEnabled = true;
                _root.Tick(Dt);
                Capture($"3b_key{index + 1}_{d:0.###e+0}_after");

                Debug.Log(
                    $"[Step3b] 段{index + 1} 火星まで {d:E1} units / " +
                    $"引き渡し率 {mars.RealScaleBlend:F2} / " +
                    $"殻メッシュ {(mars.Lod.MeshActive ? "ON " : "off")} / " +
                    $"実スケール {(mars.RealScaleBlend > 0.0 ? "ON " : "off")}");
                Debug.Log($"[Step3b] --- デバッグ表示 ---\n{_overlay.BuildText()}");
            }

            // 3e4 units 以下では完全に実スケールへ渡っている。
            Assert.That(mars.RealScaleBlend, Is.EqualTo(1.0));
        }

        [UnityTest]
        public IEnumerator 引き渡し帯の途中では両方が出ている()
        {
            CelestialBodyView mars = _root.SolarSystem.Find("Mars");

            // 4e4 units は帯のちょうど真ん中。
            _rig.JumpTo(_root, 4);            // まず 2e4 へ飛んでから
            _root.PlaceObserver(DebugJumpTable.PositionAt(_root.Model, 4.0e4));
            yield return null;

            Debug.Log($"[Step3b] 4e4 units: 引き渡し率 {mars.RealScaleBlend:F3} / " +
                      $"殻メッシュ {(mars.Lod.MeshActive ? "ON" : "off")}");

            Assert.That(mars.RealScaleBlend, Is.EqualTo(0.5).Within(1e-6));
        }

        [UnityTest]
        public IEnumerator 太陽と地球はプロキシ殻のまま()
        {
            Tap(FlightInput.Jump(7)); // 火星まで 3e3 units
            yield return null;

            CelestialBodyView sun = _root.SolarSystem.Find("Sun");
            CelestialBodyView earth = _root.SolarSystem.Find("Earth");
            CelestialBodyView mars = _root.SolarSystem.Find("Mars");

            Debug.Log($"[Step3b] 引き渡し対象 = {_root.SolarSystem.HandoffTarget?.BodyName ?? "なし"} / " +
                      $"太陽 {sun.RealScaleBlend:F2} / 地球 {earth.RealScaleBlend:F2} / 火星 {mars.RealScaleBlend:F2}");

            Assert.That(sun.RealScaleBlend, Is.EqualTo(0.0));
            Assert.That(earth.RealScaleBlend, Is.EqualTo(0.0));
            Assert.That(mars.RealScaleBlend, Is.EqualTo(1.0));
            Assert.That(_root.SolarSystem.HandoffTarget.BodyName, Is.EqualTo("Mars"));
        }

        [UnityTest]
        public IEnumerator オートパイロットで仮目標へ到着する()
        {
            // 1/1000 スケールの仮目標 7.8e4 units (決定 D-8)。
            _root.PlaceObserver(DebugJumpTable.PositionAt(_root.Model, 7.8e4));
            _rig.LookAtMars(_root);
            _rig.EngageAutopilot(_root, _root.Model.Mars.AbsolutePosition);

            // 起動直後は必ず Align。LookAtMars で既に機首が合っているので、
            // 次の Tick で Cruise へ移る。
            Assert.That(_rig.Autopilot.State, Is.EqualTo(AutopilotState.Align));
            yield return null;
            Assert.That(_rig.Autopilot.IsEngaged, Is.True);

            int steps = 0;
            const int maxSteps = 60 * 60; // 60 秒ぶん
            while (_rig.Autopilot.State != AutopilotState.Arrived && steps < maxSteps)
            {
                _root.Tick(Dt);
                steps++;
                if (steps % 120 == 0)
                {
                    yield return null;
                }
            }

            double finalDistance = _root.Model.Mars.DistanceFrom(_root.Ship.Position);
            Debug.Log(
                $"[Step3b] AP 到着: {steps * Dt:F3} 秒 ({steps} ステップ) / " +
                $"最終距離 {finalDistance:F4} units / 最終速度 {_root.Ship.SpeedKmPerSec:F4} km/s");
            Debug.Log($"[Step3b] --- デバッグ表示 ---\n{_overlay.BuildText()}");

            Assert.That(_rig.Autopilot.State, Is.EqualTo(AutopilotState.Arrived));
            Assert.That(finalDistance, Is.InRange(0.0, UniverseConstants.ArrivalRadiusUnits));
            Assert.That(_root.Ship.SpeedKmPerSec, Is.LessThanOrEqualTo(UniverseConstants.ArrivalMaxSpeedKmPerSec));
        }

        [UnityTest]
        public IEnumerator 到着後は手動操作に戻せる()
        {
            _root.PlaceObserver(DebugJumpTable.PositionAt(_root.Model, 7.8e4));
            _rig.LookAtMars(_root);
            _rig.EngageAutopilot(_root, _root.Model.Mars.AbsolutePosition);

            int steps = 0;
            while (_rig.Autopilot.State != AutopilotState.Arrived && steps < 60 * 60)
            {
                _root.Tick(Dt);
                steps++;
            }

            Assert.That(_rig.Autopilot.State, Is.EqualTo(AutopilotState.Arrived));

            // 手動入力を入れると解除され、以後は手動で動く。
            Tap(new FlightInput { Roll = 1f, JumpIndex = -1 });
            Assert.That(_rig.Autopilot.State, Is.EqualTo(AutopilotState.Idle));

            for (int i = 0; i < 3; i++)
            {
                Tap(new FlightInput { DialUp = true, JumpIndex = -1 });
            }

            double before = _root.Model.Mars.DistanceFrom(_root.Ship.Position);
            _rig.LookAtMars(_root);
            Hold(FlightInput.Forward(1f), 60);
            double after = _root.Model.Mars.DistanceFrom(_root.Ship.Position);

            Debug.Log($"[Step3b] 到着後の手動前進: {before:F4} -> {after:F4} units");
            Assert.That(after, Is.LessThan(before), "到着後に手動で動かせない");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 手動操作中にオートパイロットを起動できる()
        {
            Tap(FlightInput.Jump(2)); // 1e5 units
            for (int i = 0; i < 3; i++)
            {
                Tap(new FlightInput { DialUp = true, JumpIndex = -1 });
            }

            Hold(FlightInput.Forward(1f), 10);
            Assert.That(_rig.Autopilot.State, Is.EqualTo(AutopilotState.Idle));

            _rig.LookAtMars(_root);
            Tap(new FlightInput { AutopilotEngage = true, JumpIndex = -1 });

            Debug.Log($"[Step3b] 手動中に AP 起動 -> {_rig.Autopilot.State} / " +
                      $"実効巡航 {_rig.Autopilot.EffectiveCruiseKmPerSec:E3} km/s");

            Assert.That(_rig.Autopilot.IsEngaged, Is.True);
            yield return null;
        }

        void Capture(string name)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevDeep = _stack.Deep.targetTexture;
            RenderTexture prevNear = _stack.Near.targetTexture;
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
                File.WriteAllBytes(Path.Combine(ShotDirectory, $"{name}.png"), png.EncodeToPNG());
                Object.DestroyImmediate(png);
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                _stack.Near.targetTexture = prevNear;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
