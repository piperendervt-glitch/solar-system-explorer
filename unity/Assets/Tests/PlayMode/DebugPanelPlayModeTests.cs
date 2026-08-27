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
    /// <summary>デバッグパネル (Step 8-0b) の PlayMode 検証。</summary>
    public sealed class DebugPanelPlayModeTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        DebugPanel _panel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-panel.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _panel = Object.FindAnyObjectByType<DebugPanel>();
            Assert.That(_panel, Is.Not.Null, "DebugPanel がシーンに無い");
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        static FlightInput Key(bool panel = false, bool up = false, bool down = false,
                               bool left = false, bool right = false, bool select = false)
            => new FlightInput
            {
                JumpIndex = -1,
                DebugPanelToggle = panel,
                DebugUp = up,
                DebugDown = down,
                DebugLeft = left,
                DebugRight = right,
                DebugSelect = select,
            };

        void Press(FlightInput input)
        {
            _rig.InputOverride = input;
            _root.Tick(Dt);
            _rig.InputOverride = FlightInput.None;
            _root.Tick(Dt);
        }

        [UnityTest]
        public IEnumerator F4でパネルの表示が反転する()
        {
            yield return null;
            Assert.That(_panel.IsOpen, Is.False, "既定は閉じている");

            Press(Key(panel: true));
            Assert.That(_panel.IsOpen, Is.True, "1 回目で開く");

            Press(Key(panel: true));
            Assert.That(_panel.IsOpen, Is.False, "2 回目で閉じる");

            // 押しっぱなしで連射しない。
            _rig.InputOverride = Key(panel: true);
            _root.Tick(Dt);
            bool afterFirst = _panel.IsOpen;
            _root.Tick(Dt);
            _root.Tick(Dt);
            _rig.InputOverride = null;

            Debug.Log($"[Step8-0b] 押しっぱなし: 1 フレーム目 {afterFirst} / 3 フレーム目 {_panel.IsOpen}");
            Assert.That(_panel.IsOpen, Is.EqualTo(afterFirst), "連射している");
        }

        [UnityTest]
        public IEnumerator 閉じている間は既存動作と同じ()
        {
            yield return null;

            bool deepBefore = _stack.Deep.enabled;

            // パネルを開かずにトグルを直接切っても、適用は走らない。
            _panel.Model.Find("tier.deep").BoolValue = false;
            _root.Tick(Dt);

            Debug.Log($"[Step8-0b] 閉じたまま: Deep の enabled {_stack.Deep.enabled} (変更前 {deepBefore})");
            Assert.That(_stack.Deep.enabled, Is.EqualTo(deepBefore), "閉じているのに適用された");
            Assert.That(_rig.DebugPanelOpen, Is.False);

            _panel.Model.ResetAll();
        }

        [UnityTest]
        public IEnumerator 段をOFFにするとカメラのenabledがfalseになる()
        {
            yield return null;
            Press(Key(panel: true));
            Assert.That(_panel.IsOpen, Is.True);

            _panel.Model.Find("tier.nearfield").BoolValue = false;
            _root.Tick(Dt);

            Debug.Log($"[Step8-0b] Nearfield OFF -> enabled {_stack.Nearfield.enabled}");
            Assert.That(_stack.Nearfield.enabled, Is.False);

            _panel.Model.Find("tier.nearfield").BoolValue = true;
            _root.Tick(Dt);
            Assert.That(_stack.Nearfield.enabled, Is.True, "戻らない");
        }

        [UnityTest]
        public IEnumerator 数値を変えるとマテリアルが追従する()
        {
            yield return null;
            Press(Key(panel: true));

            Material earth = null;
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body.Name == "Earth")
                {
                    earth = v.MeshRenderer.sharedMaterial;
                }
            }

            Assert.That(earth, Is.Not.Null);

            DebugItem item = _panel.Model.Find(DebugPanelModel.AtmosphereId);
            item.Adjust(4); // 既定 5.00 -> 6.00
            _root.Tick(Dt);

            float applied = earth.GetFloat("_AtmosphereStrength");
            Debug.Log($"[Step8-0b] パネル {item.Value:F2} -> マテリアル {applied:F2}");
            Assert.That(applied, Is.EqualTo((float)item.Value).Within(1e-4f));

            _panel.Model.ResetAll();
            _root.Tick(Dt);
            Assert.That(earth.GetFloat("_AtmosphereStrength"),
                Is.EqualTo((float)PlanetAppearance.EarthAtmosphereStrength).Within(1e-4f), "戻らない");
        }

        [UnityTest]
        public IEnumerator パネルを開くと船の操作が止まる()
        {
            yield return null;

            Press(Key(panel: true));
            Assert.That(_rig.DebugPanelOpen, Is.True);

            // Space は本来「前進」。パネルが開いている間は船に届かない。
            double speedBefore = _root.Ship.SpeedKmPerSec;
            _rig.InputOverride = Key(select: true);
            for (int i = 0; i < 10; i++)
            {
                _root.Tick(Dt);
            }

            _rig.InputOverride = null;

            Debug.Log($"[Step8-0b] パネル中の Space: 速度 {speedBefore:F3} -> {_root.Ship.SpeedKmPerSec:F3} km/s");
            Assert.That(_root.Ship.SpeedKmPerSec, Is.EqualTo(speedBefore).Within(1e-9),
                "パネルが開いているのに船が動いた");
        }
    
        [UnityTest]
        public IEnumerator パネルを開くと左のHUDだけが隠れ確認項目は残る()
        {
            yield return null;
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            Assert.That(overlay, Is.Not.Null);
            overlay.Visible = true;

            Press(Key(panel: true));
            Assert.That(_panel.IsOpen, Is.True);
            Assert.That(overlay.SuppressMainHud, Is.True, "左の HUD が隠れていない");
            Assert.That(overlay.Visible, Is.True, "HUD ごと消してしまっている");
            Assert.That(overlay.BuildCheckText(), Is.Not.Null,
                        "確認項目まで巻き添えで消えている");

            Press(Key(panel: true));
            Assert.That(overlay.SuppressMainHud, Is.False, "閉じても隠れたまま");
        }

        [UnityTest]
        public IEnumerator 天体行は実際に描かれているものだけを測る()
        {
            yield return null;
            Press(Key(panel: true));

            var rows = _panel.BuildBodyRows();
            Assert.That(rows.Count, Is.GreaterThan(0), "天体行が空");

            foreach (var row in rows)
            {
                Assert.That(row.Length, Is.EqualTo(DebugPanel.BodyHeader.Length),
                            "列数が見出しと合っていない");

                string parts = row[5];
                // 見えている表現が 1 つも無い行は、測れないので --- のはず。
                bool anyVisible = parts.Contains("点") || parts.Contains("殻") || parts.Contains("実");
                if (!anyVisible)
                {
                    Assert.That(row[3], Is.EqualTo("---"),
                                "見えていないのに bbox を測っている: " + row[0]);
                }
            }
        }

        [UnityTest]
        public IEnumerator トグルでOFFにしたものはxで区別される()
        {
            yield return null;
            Press(Key(panel: true));

            // 地球の実スケールを OFF にする。
            string id = DebugPanelModel.BodyId("Earth", "real");
            int index = -1;
            for (int i = 0; i < _panel.Model.Items.Count; i++)
            {
                if (_panel.Model.Items[i].Id == id) { index = i; break; }
            }

            Assert.That(index, Is.GreaterThanOrEqualTo(0), "地球の実スケール項目が無い");
            _panel.Model.SetCursor(index);
            Press(Key(select: true));
            Assert.That(_panel.Model.BoolOf(id), Is.False, "OFF になっていない");

            var rows = _panel.BuildBodyRows();
            foreach (var row in rows)
            {
                if (row[0] != "Earth") { continue; }
                Assert.That(row[5][2], Is.EqualTo('x'),
                            "トグル OFF が - と区別されていない: " + row[5]);
            }
        }
    }
}
