using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>検証ハーネスのシナリオ定義 (Step 8-0)。</summary>
    public sealed class ScenarioTests
    {
        static IReadOnlyList<Scenario> All()
            => ScenarioLibrary.Create(SolarSystemModel.CreateOpposition());

        [Test]
        public void シナリオ名が一意()
        {
            var seen = new HashSet<string>();
            foreach (Scenario s in All())
            {
                Assert.That(s.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(seen.Add(s.Name), Is.True, $"名前が重複: {s.Name}");
            }

            Debug.Log($"[Step8-0] シナリオ {All().Count} 件: {string.Join(", ", seen)}");
        }

        [Test]
        public void 自己診断シナリオが存在する()
        {
            Scenario s = ScenarioLibrary.Find(All(), ScenarioLibrary.SelfTestName);
            Assert.That(s, Is.Not.Null, $"{ScenarioLibrary.SelfTestName} が無い");
            Debug.Log($"[Step8-0] {s.Name}: {s.Description}");
        }

        [Test]
        public void 確認項目は1から3行で空文字を含まない()
        {
            foreach (Scenario s in All())
            {
                Assert.That(s.CheckPoints, Is.Not.Null, s.Name);
                Assert.That(s.CheckPoints.Count, Is.InRange(1, Scenario.MaxCheckPoints), s.Name);
                foreach (string line in s.CheckPoints)
                {
                    Assert.That(line, Is.Not.Null.And.Not.Empty, s.Name);
                }

                Debug.Log($"[Step8-0] {s.Name} の確認項目 {s.CheckPoints.Count} 行: " +
                          string.Join(" / ", s.CheckPoints));
            }
        }

        [Test]
        public void 位置と注視点が異なる()
        {
            // 同一だと Quaternion.LookRotation が破綻する。
            foreach (Scenario s in All())
            {
                double d = Vec3d.Distance(s.Start.Position, s.Start.LookAt);
                Debug.Log($"[Step8-0] {s.Name} 位置-注視点 距離 {d:E3} units");
                Assert.That(d, Is.GreaterThan(1e-6), s.Name);
            }
        }

        [Test]
        public void Upが零ベクトルでない()
        {
            foreach (Scenario s in All())
            {
                Assert.That(Vec3d.Distance(s.Start.Up, Vec3d.Zero), Is.GreaterThan(1e-6), s.Name);
            }
        }

        [Test]
        public void 目標ステーション番号がモデルの範囲内()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            foreach (Scenario s in ScenarioLibrary.Create(model))
            {
                Assert.That(s.Start.TargetStationIndex,
                    Is.InRange(0, model.Stations.Count - 1), s.Name);
            }
        }

        [Test]
        public void 画角が妥当な範囲()
        {
            foreach (Scenario s in All())
            {
                Assert.That(s.Start.VerticalFovDegrees, Is.InRange(10.0, 120.0), s.Name);
            }
        }

        [Test]
        public void 開始時刻が非負()
        {
            foreach (Scenario s in All())
            {
                Assert.That(s.Start.ElapsedSeconds, Is.GreaterThanOrEqualTo(0.0), s.Name);
            }
        }

        [Test]
        public void 未知の名前ではnullを返し例外を投げない()
        {
            IReadOnlyList<Scenario> all = All();
            Assert.That(ScenarioLibrary.Find(all, "no-such-scenario"), Is.Null);
            Assert.That(ScenarioLibrary.Find(all, null), Is.Null);
            Assert.That(ScenarioLibrary.Find(all, string.Empty), Is.Null);
            Assert.That(ScenarioLibrary.Find(null, "x"), Is.Null);

            Assert.That(ScenarioLibrary.IndexOf(all, "no-such-scenario"), Is.EqualTo(-1));
        }

        [Test]
        public void ステーションが視線を塞がない()
        {
            // 実測でハマった穴: ポート正面に立つとステーション本体が
            // 地球の中央に 428x400 px の矩形として写り込んだ。
            // 視線から 45 度以内にステーションが来ないことを定義側で保証する。
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            foreach (Scenario s in ScenarioLibrary.Create(model))
            {
                Vec3d view = (s.Start.LookAt - s.Start.Position).Normalized;
                foreach (SpaceStation station in model.Stations)
                {
                    Vec3d toStation = station.AbsolutePosition - s.Start.Position;
                    double distance = toStation.Magnitude;
                    if (distance > 1.0e3)
                    {
                        continue; // 遠すぎて点にもならない
                    }

                    double cos = Vec3d.Dot(view, toStation.Normalized);
                    double angle = System.Math.Acos(System.Math.Min(1.0, System.Math.Max(-1.0, cos)))
                                   * 180.0 / System.Math.PI;
                    Debug.Log($"[Step8-0] {s.Name}: {station.Name} まで {distance:F2} units / 視線から {angle:F1} 度");
                    Assert.That(angle, Is.GreaterThan(45.0),
                        $"{s.Name} は {station.Name} が視界を塞ぐ");
                }
            }
        }

        // ---- 微振動 (Step 8-0) ----

        [Test]
        public void 微振動の振幅はスラストに比例しドッキング中は0()
        {
            const float max = CockpitShake.MaxAmplitudeRadians;

            Assert.That(CockpitShake.AmplitudeRadians(0f, false, max), Is.EqualTo(0f));
            Assert.That(CockpitShake.AmplitudeRadians(0.5f, false, max),
                Is.EqualTo(max * 0.5f).Within(1e-9f));
            Assert.That(CockpitShake.AmplitudeRadians(1f, false, max), Is.EqualTo(max));

            // 逆噴射でも揺れる (絶対値)。
            Assert.That(CockpitShake.AmplitudeRadians(-1f, false, max), Is.EqualTo(max));

            // ドッキング中は 0。
            Assert.That(CockpitShake.AmplitudeRadians(1f, true, max), Is.EqualTo(0f));

            // 1 px あたり 1.069e-3 rad なので、最大でも数 px に収まる。
            double px = max / AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees,
                UniverseConstants.ReferencePixelHeight);
            Debug.Log($"[Step8-0] 微振動 最大 {max:E3} rad = {max * Mathf.Rad2Deg:F3} 度 = {px:F2} px");
            Assert.That(px, Is.InRange(0.5, 5.0), "見えない / 揺れすぎ のどちらでもないこと");
        }

        [Test]
        public void 時刻の差し替えはステップ数に丸まる()
        {
            var clock = new UniverseClock();
            clock.SetElapsedSeconds(10.0);
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(10.0).Within(UniverseConstants.FixedDeltaSeconds));

            clock.SetElapsedSeconds(-5.0);
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(0.0), "負の時刻は 0 に倒す");
        }
    }
}
