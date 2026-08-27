using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>デバッグパネルのモデル (Step 8-0b)。純粋な状態機械なので素で叩く。</summary>
    public sealed class DebugPanelTests
    {
        static DebugPanelModel Make()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            var names = new System.Collections.Generic.List<string>();
            foreach (CelestialBody b in model.Bodies)
            {
                names.Add(b.Name);
            }

            return DebugPanelModel.Create(
                names,
                PlanetAppearance.EarthAtmosphereStrength,
                PlanetAppearance.CloudOpacity,
                SunFlareController.BaseIntensity,
                CockpitShake.MaxAmplitudeRadians,
                PlanetAppearance.SunEmissionIntensity,
                PlanetAppearance.CoronaRadiusScale);
        }

        [Test]
        public void 既定値がコードの定数と一致する()
        {
            DebugPanelModel m = Make();

            double atmosphere = m.Find(DebugPanelModel.AtmosphereId).DefaultValue;
            double cloud = m.Find(DebugPanelModel.CloudId).DefaultValue;
            double flare = m.Find(DebugPanelModel.FlareId).DefaultValue;
            double shake = m.Find(DebugPanelModel.ShakeId).DefaultValue;

            Debug.Log($"[Step8-0b] 既定 大気 {atmosphere} / 雲 {cloud} / フレア {flare} / 微振動 {shake:E3}");

            Assert.That(atmosphere, Is.EqualTo(PlanetAppearance.EarthAtmosphereStrength));
            Assert.That(cloud, Is.EqualTo(PlanetAppearance.CloudOpacity));
            Assert.That(flare, Is.EqualTo(SunFlareController.BaseIntensity).Within(1e-6));
            Assert.That(shake, Is.EqualTo(CockpitShake.MaxAmplitudeRadians).Within(1e-9));
        }

        [Test]
        public void 数値が範囲でクランプされる()
        {
            DebugPanelModel m = Make();
            DebugItem atmosphere = m.Find(DebugPanelModel.AtmosphereId);

            for (int i = 0; i < 200; i++)
            {
                atmosphere.Adjust(1);
            }

            Assert.That(atmosphere.Value, Is.EqualTo(atmosphere.Max), "上限を超えた");

            for (int i = 0; i < 200; i++)
            {
                atmosphere.Adjust(-1);
            }

            Assert.That(atmosphere.Value, Is.EqualTo(atmosphere.Min), "下限を割った");
            Debug.Log($"[Step8-0b] 大気の範囲 {atmosphere.Min} 〜 {atmosphere.Max}");
        }

        [Test]
        public void 刻みどおりに増減する()
        {
            DebugPanelModel m = Make();

            var cases = new[]
            {
                (DebugPanelModel.AtmosphereId, 0.25),
                (DebugPanelModel.CloudId, 0.05),
                (DebugPanelModel.FlareId, 0.05),
                (DebugPanelModel.ShakeId, 2.5e-4),
            };

            foreach ((string id, double step) in cases)
            {
                DebugItem item = m.Find(id);
                Assert.That(item.Step, Is.EqualTo(step).Within(1e-12), id);

                double before = item.Value;
                item.Adjust(1);
                Debug.Log($"[Step8-0b] {item.Label}: {before} -> {item.Value} (刻み {step})");
                Assert.That(item.Value - before, Is.EqualTo(step).Within(step * 1e-6), id);
            }
        }

        [Test]
        public void 単一段表示が排他になる()
        {
            DebugPanelModel m = Make();

            // 既定 (なし) では 4 段とも見える。
            Assert.That(m.TierVisible(1, "tier.deep"), Is.True);
            Assert.That(m.TierVisible(2, "tier.near"), Is.True);

            DebugItem solo = m.Find(DebugPanelModel.SoloId);
            solo.Index = 2; // Near

            Debug.Log($"[Step8-0b] 1 段だけ表示 = {DebugPanelModel.SoloOptions[solo.Index]}");
            Assert.That(m.TierVisible(1, "tier.deep"), Is.False);
            Assert.That(m.TierVisible(2, "tier.near"), Is.True);
            Assert.That(m.TierVisible(3, "tier.nearfield"), Is.False);
            Assert.That(m.TierVisible(4, "tier.cockpit"), Is.False);

            // 個別トグルを切っても、solo が優先される。
            m.Find("tier.near").BoolValue = false;
            Assert.That(m.TierVisible(2, "tier.near"), Is.True, "solo が優先されていない");
        }

        [Test]
        public void Rで全項目が既定に戻る()
        {
            DebugPanelModel m = Make();
            m.Find(DebugPanelModel.AtmosphereId).Adjust(4);
            m.Find("tier.deep").BoolValue = false;
            m.Find(DebugPanelModel.SoloId).Index = 3;

            Assert.That(m.ChangedItems().Count, Is.EqualTo(3));

            m.ResetAll();
            Debug.Log($"[Step8-0b] ResetAll 後の変更点 {m.ChangedItems().Count} 件");
            Assert.That(m.ChangedItems().Count, Is.EqualTo(0));
        }

        [Test]
        public void シナリオ切替ではトグルだけ戻り数値は残る()
        {
            DebugPanelModel m = Make();
            DebugItem atmosphere = m.Find(DebugPanelModel.AtmosphereId);

            atmosphere.Adjust(4);            // 5.00 -> 6.00
            m.Find("tier.deep").BoolValue = false;
            m.Find(DebugPanelModel.SoloId).Index = 1;

            double kept = atmosphere.Value;
            m.ResetToggles();

            Debug.Log($"[Step8-0b] ResetToggles 後: 大気 {atmosphere.Value} (保持) / " +
                      $"段 Deep {m.BoolOf("tier.deep")} / solo {m.SoloIndex}");

            Assert.That(atmosphere.Value, Is.EqualTo(kept), "数値が戻ってしまった");
            Assert.That(m.BoolOf("tier.deep"), Is.True, "トグルが戻っていない");
            Assert.That(m.SoloIndex, Is.EqualTo(0), "選択が戻っていない");
        }

        [Test]
        public void 変更点だけがログに出る()
        {
            DebugPanelModel m = Make();
            Assert.That(m.BuildChangeLog(), Does.Contain("変更された項目はありません"));

            m.Find(DebugPanelModel.CloudId).Adjust(2);
            string log = m.BuildChangeLog();
            Debug.Log("[Step8-0b] " + log);

            Assert.That(log, Does.Contain("_CloudOpacity"));
            Assert.That(log, Does.Not.Contain("_AtmosphereStrength"), "変えていない項目が出ている");
        }
    }
}
