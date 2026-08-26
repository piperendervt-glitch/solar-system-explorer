using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>手動操作 (Step 3a) の Core ロジックと入力定義。</summary>
    public sealed class ManualControlsTests
    {
        // ---- 速度ダイヤル ----

        [Test]
        public void ダイヤルはSTOPから始まる()
        {
            var dial = new SpeedDial();
            Assert.That(dial.Index, Is.EqualTo(0));
            Assert.That(dial.Current.KmPerSec, Is.EqualTo(0.0));
            Assert.That(dial.Current.Label, Is.EqualTo("STOP"));
        }

        [Test]
        public void ダイヤルは端で止まり巡回しない()
        {
            var dial = new SpeedDial();
            dial.Shift(-5);
            Assert.That(dial.Index, Is.EqualTo(0), "STOP より下へ回らない");

            dial.Shift(+100);
            Assert.That(dial.Index, Is.EqualTo(SpeedDial.Steps.Count - 1), "上端を越えない");
            Assert.That(dial.Current.Beta, Is.EqualTo(UniverseConstants.DefaultCruiseBeta).Within(1e-12));
        }

        [Test]
        public void 低速側はkm毎秒表記で上限1km毎秒()
        {
            var kmSteps = SpeedDial.Steps.Where(s => !s.ShowAsBeta).ToArray();
            double maxKmPerSec = kmSteps.Max(s => s.KmPerSec);

            Debug.Log("[Step3a] ダイヤル: " + string.Join(" | ", SpeedDial.Steps.Select(s => s.Label)));

            // 決定 D-11: 手動操作の速度は km/s 表記、上限 1 km/s。
            Assert.That(maxKmPerSec, Is.EqualTo(UniverseConstants.ManualMaxSpeedKmPerSec));
            Assert.That(kmSteps.Last().Label, Is.EqualTo("1 km/s"));
        }

        [Test]
        public void 高速側はc表記()
        {
            var betaSteps = SpeedDial.Steps.Where(s => s.ShowAsBeta).ToArray();
            Assert.That(betaSteps.Length, Is.GreaterThan(0));
            Assert.That(betaSteps.Last().Label, Is.EqualTo("0.9 c"));

            // c 表記の下端でも 1 km/s を大きく超える。表記を分ける根拠。
            Assert.That(betaSteps.First().KmPerSec, Is.GreaterThan(UniverseConstants.ManualMaxSpeedKmPerSec));
        }

        [Test]
        public void 手動域の判定が1km毎秒で切り替わる()
        {
            var dial = new SpeedDial();
            dial.SetIndex(3); // 1 km/s
            Assert.That(dial.IsManualRegime, Is.True);

            dial.Shift(+1); // 0.001c
            Assert.That(dial.IsManualRegime, Is.False);
        }

        [Test]
        public void 上限1km毎秒なら1フレームの移動量が目で追える()
        {
            double perFrame = UniverseConstants.ManualMaxSpeedKmPerSec * UniverseConstants.FixedDeltaSeconds;
            Debug.Log($"[Step3a] 1 km/s の 1 フレーム移動量 = {perFrame:F5} units = {perFrame * 1000:F1} m");
            Assert.That(perFrame * 1000.0, Is.EqualTo(16.667).Within(0.01));
        }

        // ---- デバッグジャンプ ----

        [Test]
        public void ジャンプ先の距離が指定どおり()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();

            for (int i = 0; i < DebugJumpTable.Count; i++)
            {
                Vec3d pos = DebugJumpTable.PositionForIndex(model, i);
                double actual = model.Mars.DistanceFrom(pos);
                Assert.That(actual, Is.EqualTo(DebugJumpTable.Distances[i]).Within(1e-6),
                    $"キー {i + 1} の距離がずれている");
            }
        }

        [Test]
        public void ジャンプ先はどれも太陽と火星を結ぶ線上にある()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            Vec3d toward = (model.Mars.AbsolutePosition - model.Earth.AbsolutePosition).Normalized;

            for (int i = 0; i < DebugJumpTable.Count; i++)
            {
                Vec3d pos = DebugJumpTable.PositionForIndex(model, i);
                Vec3d dirToMars = model.Mars.DirectionFrom(pos);
                Assert.That(Vec3d.Dot(dirToMars, toward), Is.EqualTo(1.0).Within(1e-9));
            }
        }

        [Test]
        public void プロキシ殻を壊す段が7番と8番だけ()
        {
            const double nearClip = 500.0;
            var clipping = new System.Collections.Generic.List<int>();

            for (int i = 0; i < DebugJumpTable.Count; i++)
            {
                double d = DebugJumpTable.Distances[i];
                bool clips = DebugJumpTable.ClipsNearPlane(SolarSystemModel.MarsRadiusKm, d, nearClip);
                double shell = DeepProxyProjection.ShellRadius(d);
                double front = shell - SolarSystemModel.MarsRadiusKm * DeepProxyProjection.ScaleFactor(d);

                Debug.Log($"[Step3a] キー{i + 1} d={d:E1} units / 殻の手前 {front:F1} / near=500 を切る: {clips}");

                if (clips)
                {
                    clipping.Add(i + 1);
                }
            }

            // 火星の下限は 6777 units。1e4 までは安全、5e3 と 3e3 が内側。
            Assert.That(clipping, Is.EqualTo(new[] { 7, 8 }));
        }

        // ---- .inputactions ----

        [Test]
        public void 入力アセットに必要なアクションが全部ある()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/ShipControls.inputactions");
            Assert.That(asset, Is.Not.Null, ".inputactions が読めない");

            InputActionMap map = asset.FindActionMap("Flight", throwIfNotFound: false);
            Assert.That(map, Is.Not.Null, "Flight アクションマップが無い");

            string[] required =
            {
                "LookMouse", "LookKeys", "Roll", "Thrust", "DialUp", "DialDown",
                "Jump1", "Jump2", "Jump3", "Jump4", "Jump5", "Jump6", "Jump7", "Jump8",
                "AutopilotEngage", "AutopilotCancel",
                "CycleTarget", "DockRequest", "Undock",
                // 検証ハーネス (Step 8-0)
                "DebugHudToggle", "ScenarioNext", "ScenarioPrev",
            };

            foreach (string name in required)
            {
                Assert.That(map.FindAction(name), Is.Not.Null, $"アクション {name} が無い");
            }

            Debug.Log($"[Step3a] Flight アクション {map.actions.Count} 個 / バインディング {map.bindings.Count} 個");
            Assert.That(map.actions.Count, Is.EqualTo(required.Length));
        }

        [Test]
        public void ジャンプキーが1から8の数字に割り当たっている()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/ShipControls.inputactions");
            InputActionMap map = asset.FindActionMap("Flight");

            for (int i = 1; i <= DebugJumpTable.Count; i++)
            {
                InputAction action = map.FindAction($"Jump{i}");
                string[] paths = action.bindings.Select(b => b.path).ToArray();
                Assert.That(paths, Does.Contain($"<Keyboard>/{i}"), $"Jump{i} に数字キー {i} が無い");
            }
        }

        [Test]
        public void 姿勢と前進の入力が割り当たっている()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/ShipControls.inputactions");
            InputActionMap map = asset.FindActionMap("Flight");

            string[] AllPaths(string action) =>
                map.FindAction(action).bindings.Select(b => b.path).ToArray();

            Assert.That(AllPaths("LookMouse"), Does.Contain("<Mouse>/delta"));
            Assert.That(AllPaths("LookKeys"), Does.Contain("<Keyboard>/w"));
            Assert.That(AllPaths("LookKeys"), Does.Contain("<Keyboard>/a"));
            Assert.That(AllPaths("Roll"), Does.Contain("<Keyboard>/q"));
            Assert.That(AllPaths("Roll"), Does.Contain("<Keyboard>/e"));
            Assert.That(AllPaths("Thrust"), Does.Contain("<Keyboard>/space"));
            Assert.That(AllPaths("DialUp"), Does.Contain("<Keyboard>/r"));
            Assert.That(AllPaths("DialDown"), Does.Contain("<Keyboard>/f"));
            Assert.That(AllPaths("AutopilotEngage"), Does.Contain("<Keyboard>/t"));
            Assert.That(AllPaths("AutopilotCancel"), Does.Contain("<Keyboard>/g"));
            Assert.That(AllPaths("CycleTarget"), Does.Contain("<Keyboard>/tab"));
            Assert.That(AllPaths("DockRequest"), Does.Contain("<Keyboard>/enter"));
            Assert.That(AllPaths("Undock"), Does.Contain("<Keyboard>/backspace"));

            // 検証ハーネス (Step 8-0)。
            Assert.That(AllPaths("DebugHudToggle"), Does.Contain("<Keyboard>/f1"));
            Assert.That(AllPaths("ScenarioNext"), Does.Contain("<Keyboard>/f2"));
            Assert.That(AllPaths("ScenarioPrev"), Does.Contain("<Keyboard>/f3"));
        }
    }
}
