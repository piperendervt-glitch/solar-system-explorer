using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>太陽の HDR 化 (Step 9-1)。</summary>
    public sealed class SunSurfaceTests
    {
        static SolarSystemModel Model() => SolarSystemModel.CreateOpposition();

        [Test]
        public void 太陽の殻と光点が手書きシェーダを使う()
        {
            CelestialBody sun = Model().Sun;
            Assert.That(MaterialLibrary.MeshMaterial(sun).shader.name,
                        Is.EqualTo(MaterialLibrary.SunShaderName));
            Assert.That(MaterialLibrary.PointMaterial(sun).shader.name,
                        Is.EqualTo(MaterialLibrary.SunShaderName),
                        "光点だけ Unlit のままだと LOD 帯で明るさが跳ぶ");
        }

        [Test]
        public void 発光強度が0でなく殻と光点で一致する()
        {
            CelestialBody sun = Model().Sun;
            float mesh = MaterialLibrary.MeshMaterial(sun).GetFloat("_EmissionIntensity");
            float point = MaterialLibrary.PointMaterial(sun).GetFloat("_EmissionIntensity");

            Assert.That(mesh, Is.GreaterThan(1.0f), "1.0 では bloom しきい値 1.05 を超えられない");
            Assert.That(point, Is.EqualTo(mesh).Within(1e-4f), "殻と光点で強度が違う");
            Assert.That(mesh, Is.EqualTo((float)PlanetAppearance.SunEmissionIntensity).Within(1e-4f),
                        "定数を二重定義している");
        }

        [Test]
        public void 周辺減光のプロパティが入っている()
        {
            Material m = MaterialLibrary.MeshMaterial(Model().Sun);
            Assert.That(m.HasProperty("_LimbDarkening"), Is.True);
            Assert.That(m.GetFloat("_LimbFloor"), Is.EqualTo(MaterialLibrary.SunLimbFloor).Within(1e-4f));
            Assert.That(m.GetFloat("_LimbFloor"), Is.LessThan(1.0f), "縁が中心と同じ明るさでは平坦なまま");
        }

        [Test]
        public void 強度をBaseColorに焼いていない()
        {
            // **MPB が _BaseColor の RGBA を毎フレーム上書きするので、
            // ここに焼いた HDR 値は初回フレームで消える。**
            foreach (Material m in new[]
                     {
                         MaterialLibrary.MeshMaterial(Model().Sun),
                         MaterialLibrary.PointMaterial(Model().Sun),
                     })
            {
                Color c = m.GetColor("_BaseColor");
                Assert.That(Mathf.Max(c.r, Mathf.Max(c.g, c.b)), Is.LessThanOrEqualTo(1.0f),
                            "_BaseColor に HDR 値が焼かれている");
            }
        }

        [Test]
        public void デッドコードが残っていない()
        {
            System.Type t = typeof(MaterialLibrary);

            Assert.That(t.GetField("EmissionIntensity",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic),
                        Is.Null, "使われない EmissionIntensity 定数が残っている");

            MethodInfo[] all = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Static | BindingFlags.Instance);
            bool hasEmissiveArg = all.Any(m => m.GetParameters().Any(pa => pa.Name == "emissive"));
            Assert.That(hasEmissiveArg, Is.False,
                        "どこからも true が渡らない emissive 引数が残っている");
        }
    }
}
