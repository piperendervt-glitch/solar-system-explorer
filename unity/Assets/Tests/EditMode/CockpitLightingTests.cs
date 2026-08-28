using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 補助光と発光 (Step 11-4)。
    ///
    /// **「設定し忘れても絵は出る」を作らない。** 11-3c で、未設定の行列が単位行列
    /// として返り、blit が素通しになっていたのに気づけなかった。同じ型を避けるため、
    /// **無害な既定値（強度 0 / 対象 0 件 / 未 Bind）が必ず失敗になる**ように縛る。
    /// </summary>
    public sealed class CockpitLightingTests
    {
        Transform _ship;

        [SetUp]
        public void SetUp() => _ship = new GameObject("TestShip").transform;

        [TearDown]
        public void TearDown()
        {
            if (_ship != null)
            {
                UnityEngine.Object.DestroyImmediate(_ship.gameObject);
            }
        }

        static void RequireImported()
        {
            if (!CockpitCatalog.IsAvailable(CockpitDefinition.HiRezSample))
            {
                Assert.Inconclusive("コックピットのアセットが取り込まれていない");
            }
        }

        // ---- 内装の発光の撤去 (11-4) ----

        [Test]
        public void 内装の発光の経路が残っていない()
        {
            // **効かない摘みは残さない。**
            // 発光マテリアル Cockpit3Grey の強さを 0.0 と 1.0 で振っても、
            // **内装の画素が 1 つも変わらなかった**（4 場面とも最大差 0 / 実測）。
            // 見える面に発光テクセルが無いものと判断し、経路ごと外した。
            // 形は Demo 2 の EngineAudio 撤去と同じ（リフレクションで再発を縛る）。
            Assert.That(typeof(CockpitDefinition).GetProperty("Emissives"), Is.Null,
                        "Definition.Emissives が残っている");
            Assert.That(typeof(CockpitDefinition).GetField("DefaultEmissiveStrength"), Is.Null,
                        "発光の強さの定数が残っている");

            Assert.That(typeof(CockpitLights).GetProperty("Emissives"), Is.Null,
                        "CockpitLights.Emissives が残っている");
            Assert.That(typeof(CockpitLights).GetMethod("SetEmissiveStrength"), Is.Null,
                        "発光の摘みが残っている");
            Assert.That(typeof(CockpitLights).GetField("EmissionProperty"), Is.Null,
                        "発光のプロパティ名が残っている");

            Assembly editor = typeof(CockpitBuilder).Assembly;
            MethodInfo collect = typeof(CockpitBuilder).GetMethod(
                "CollectEmissives", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(collect, Is.Null, "CollectEmissives が残っている");
            Assert.That(editor, Is.Not.Null);
        }

        [Test]
        public void 未Bindのまま当てると例外で止まる()
        {
            var go = new GameObject("Probe");
            go.transform.SetParent(_ship, false);

            var lights = go.AddComponent<CockpitLights>();
            Assert.That(() => lights.Apply(), Throws.InstanceOf<InvalidOperationException>());
        }
        [Test]
        public void 箱にも補助光がある()
        {
            // 箱コックピットでも内装は暗くなる。**フォールバックでも光は要る。**
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);

            Assert.That(box.Lights.Fill, Is.Not.Null, "箱に補助光が無い");
            Assert.That(box.Lights.Fill.intensity,
                        Is.EqualTo((float)CockpitDefinition.DefaultFillLightIntensity)
                            .Within(1e-4f));
        }

        // ---- 既定値が「何もしない値」でないこと ----

        [Test]
        public void 補助光の強さは0でない()
        {
            Assert.That(CockpitDefinition.DefaultFillLightIntensity, Is.GreaterThan(0.0),
                        "強度 0 は「消えている」と「設定し忘れた」を見分けられない");
        }

        [Test]
        public void 補助光の設定がコードの定数と一致する()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);
            Light fill = hirez.Lights.Fill;

            Assert.That(fill, Is.Not.Null);
            Assert.That(fill.type, Is.EqualTo(LightType.Point));
            Assert.That(fill.intensity,
                        Is.EqualTo((float)CockpitDefinition.DefaultFillLightIntensity).Within(1e-4f));
            Assert.That(fill.range,
                        Is.EqualTo((float)CockpitDefinition.FillLightRangeMeters).Within(1e-4f));
            Assert.That(fill.shadows, Is.EqualTo(LightShadows.None), "補助光が影を落としている");
        }

        [Test]
        public void 補助光はコックピット段だけを向いている()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            Assert.That(hirez.Lights.Fill.cullingMask, Is.EqualTo(1 << 9),
                        "補助光の culling mask がコックピット層だけになっていない");

            // **内装のレンダラーは既定のビットも持つ。** これが無いと太陽光が
            // 当たらなくなる（太陽の Directional は既定のビットしか持たない）。
            foreach (Renderer r in hirez.Identity.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(r.renderingLayerMask & 1u, Is.Not.Zero,
                            $"{r.name} が既定のレンダリングレイヤーを失っている（太陽光が当たらない）");
                Assert.That(r.renderingLayerMask & CockpitDefinition.CockpitRenderingLayer,
                            Is.Not.Zero, $"{r.name} に内装のレンダリングレイヤーが無い");
            }
        }

        // ---- 潰れの判定式 ----

        [Test]
        public void 画素が1つも無ければ例外()
        {
            // 「マスクが 0 画素なので潰れていない」という通り方をさせない。
            Assert.That(() => CockpitLighting.Measure(new double[0]),
                        Throws.InstanceOf<ArgumentException>());
            Assert.That(() => CockpitLighting.Measure(null),
                        Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void 真っ黒の割合を数える()
        {
            CockpitLighting.Result allBlack = CockpitLighting.Measure(new[] { 0.0, 1.0, 2.0 });
            Assert.That(allBlack.BlackRatio, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(CockpitLighting.WithinBudget(allBlack), Is.False);

            CockpitLighting.Result mixed =
                CockpitLighting.Measure(new[] { 0.0, 10.0, 20.0, 30.0 });
            Assert.That(mixed.BlackRatio, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(mixed.Mean, Is.EqualTo(15.0).Within(1e-9));
            Assert.That(mixed.Median, Is.EqualTo(15.0).Within(1e-9));
            Assert.That(mixed.Min, Is.EqualTo(0.0));
            Assert.That(mixed.Max, Is.EqualTo(30.0));
        }

        [Test]
        public void 輝度はBT709()
        {
            Assert.That(CockpitLighting.Luminance(255, 255, 255), Is.EqualTo(255.0).Within(1e-6));
            Assert.That(CockpitLighting.Luminance(0, 0, 0), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(CockpitLighting.Luminance(0, 255, 0), Is.EqualTo(0.7152 * 255).Within(1e-6));
        }
    }
}
