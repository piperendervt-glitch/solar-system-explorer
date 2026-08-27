using NUnit.Framework;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>惑星のテクスチャとシェーダ (Step 8-1 / 8-2)。</summary>
    public sealed class PlanetSurfaceTests
    {
        static Material EarthMaterial()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            return SolarSystem.Editor.MaterialLibrary.MeshMaterial(model.Earth);
        }

        static Material MarsMaterial()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            return SolarSystem.Editor.MaterialLibrary.MeshMaterial(model.Mars);
        }

        [Test]
        public void 追加マップが4kに縮小されている()
        {
            foreach (var map in SolarSystem.Editor.PlanetTextureSetup.Maps)
            {
                string path = SolarSystem.Editor.PlanetTextureSetup.AssetPath(map.Output);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(texture, Is.Not.Null, path + " が読めない");

                Debug.Log($"[Step8-1] {map.Output}: {texture.width} x {texture.height}");
                Assert.That(texture.width, Is.EqualTo(SolarSystem.Editor.PlanetTextureSetup.TargetWidth), map.Output);
                Assert.That(texture.height, Is.EqualTo(SolarSystem.Editor.PlanetTextureSetup.TargetHeight), map.Output);
            }
        }

        [Test]
        public void 色空間の設定が仕様表のとおり()
        {
            foreach (var map in SolarSystem.Editor.PlanetTextureSetup.Maps)
            {
                string path = SolarSystem.Editor.PlanetTextureSetup.AssetPath(map.Output);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);

                Debug.Log($"[Step8-1] {map.Output}: type={importer.textureType} sRGB={importer.sRGBTexture}");

                if (map.Kind == SolarSystem.Editor.PlanetTextureSetup.MapKind.Normal)
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap), map.Output);
                    Assert.That(path, Does.EndWith(".png"), "法線は PNG 必須");
                }
                else if (map.Kind == SolarSystem.Editor.PlanetTextureSetup.MapKind.LinearMask)
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), map.Output);
                    Assert.That(importer.sRGBTexture, Is.False, map.Output + " はマスクなので Linear");
                    Assert.That(path, Does.EndWith(".png"), "マスクは PNG");
                }
                else
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), map.Output);
                    Assert.That(importer.sRGBTexture, Is.True, map.Output + " は見た目の色なので sRGB");
                }
            }
        }

        [Test]
        public void 地球のマテリアルに4種のテクスチャが割り当たっている()
        {
            Material m = EarthMaterial();
            Assert.That(m.shader.name, Is.EqualTo(SolarSystem.Editor.MaterialLibrary.PlanetShaderName));

            foreach (string prop in new[] { "_BaseMap", "_NormalMap", "_SpecularMask", "_NightMap" })
            {
                Texture t = m.GetTexture(prop);
                string shown = t != null ? t.name : "(無し)";
                Debug.Log($"[Step8-2] 地球 {prop} = {shown}");
                Assert.That(t, Is.Not.Null, "地球の " + prop + " が空");
            }
        }

        [Test]
        public void 火星は鏡面と街灯りがblack既定に解決される()
        {
            Material m = MarsMaterial();

            // **既定を書いていないと Unity は "white" に解決する。**
            // その場合、火星が全面鏡面かつ全面街灯りになる。
            Texture spec = m.GetTexture("_SpecularMask");
            Texture night = m.GetTexture("_NightMap");
            Texture normal = m.GetTexture("_NormalMap");

            string s1 = spec != null ? spec.name : "(null)";
            string s2 = night != null ? night.name : "(null)";
            string s3 = normal != null ? normal.name : "(null)";
            Debug.Log($"[Step8-2] 火星 _SpecularMask={s1} _NightMap={s2} _NormalMap={s3}");

            Assert.That(spec == null || spec.name == "black", Is.True,
                "火星の _SpecularMask が black 既定でない: " + s1);
            Assert.That(night == null || night.name == "black", Is.True,
                "火星の _NightMap が black 既定でない: " + s2);
            Assert.That(normal == null || normal.name == "bump", Is.True,
                "火星の _NormalMap が bump 既定でない: " + s3);

            Assert.That(m.GetTexture("_BaseMap"), Is.Not.Null, "火星のアルベドが空");
        }

        [Test]
        public void 地球と火星のプロパティ値が仕様表と一致する()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();

            var earth = SolarSystem.Editor.MaterialLibrary.LookFor(model.Earth);
            var mars = SolarSystem.Editor.MaterialLibrary.LookFor(model.Mars);

            Debug.Log($"[Step8-2] 地球 色 {earth.AtmosphereColor} power {earth.AtmospherePower} 強さ {earth.AtmosphereStrength}");
            Debug.Log($"[Step8-2] 火星 色 {mars.AtmosphereColor} power {mars.AtmospherePower} 強さ {mars.AtmosphereStrength}");

            Assert.That(earth.AtmosphereColor, Is.EqualTo(new Color(0.35f, 0.55f, 1.0f, 1f)));
            Assert.That(earth.AtmospherePower, Is.EqualTo(3.5f));
            Assert.That(mars.AtmosphereColor, Is.EqualTo(new Color(1.0f, 0.6f, 0.35f, 1f)));
            Assert.That(mars.AtmospherePower, Is.EqualTo(5.0f));

            // 火星は地球の 1/4 (薄い大気)。
            Assert.That(mars.AtmosphereStrength, Is.EqualTo(earth.AtmosphereStrength * 0.25f).Within(1e-5f));

            Material m = EarthMaterial();
            Assert.That(m.GetFloat("_SmoothnessLand"), Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(m.GetFloat("_SmoothnessOcean"), Is.EqualTo(0.85f).Within(1e-5f));
        }

        [Test]
        public void シェーダが既存のクロスフェード契約を守っている()
        {
            Shader shader = Shader.Find(SolarSystem.Editor.MaterialLibrary.PlanetShaderName);
            Assert.That(shader, Is.Not.Null, "PlanetSurface が見つからない");

            Material m = EarthMaterial();
            Assert.That(m.HasProperty("_BaseColor"), Is.True, "_BaseColor が無いとクロスフェードが壊れる");
            Assert.That(m.HasProperty("_EmissionIntensity"), Is.True, "強度は別プロパティに逃がす");

            int queue = m.renderQueue;
            Debug.Log($"[Step8-2] renderQueue={queue}");
            Assert.That(queue, Is.GreaterThanOrEqualTo(3000), "Transparent キューであること");
        }

        [Test]
        public void 地球接近の3シナリオで太陽の向きが異なる()
        {
            var all = ScenarioLibrary.Create(SolarSystemModel.CreateOpposition());
            string[] names =
            {
                ScenarioLibrary.EarthDayName,
                ScenarioLibrary.EarthTerminatorName,
                ScenarioLibrary.EarthNightName,
            };

            var dirs = new System.Collections.Generic.List<Vec3d>();
            foreach (string name in names)
            {
                Scenario s = ScenarioLibrary.Find(all, name);
                Assert.That(s, Is.Not.Null, name + " が無い");
                Assert.That(s.Start.SunDirectionOverride.HasValue, Is.True, name + " に太陽の向きが無い");

                Vec3d d = s.Start.SunDirectionOverride.Value.Normalized;
                Debug.Log($"[Step8-2] {name}: 太陽の向き ({d.X:F2}, {d.Y:F2}, {d.Z:F2})");
                dirs.Add(d);
            }

            for (int i = 0; i < dirs.Count; i++)
            {
                for (int j = i + 1; j < dirs.Count; j++)
                {
                    // 昼と夜は正反対 (dot = -1)。これは別方向なので通す。
                    // 弾きたいのは「同じ向き」だけなので abs を取らない。
                    double dot = Vec3d.Dot(dirs[i], dirs[j]);
                    Assert.That(dot, Is.LessThan(0.99),
                        names[i] + " と " + names[j] + " の太陽の向きが同じ");
                }
            }
        }
    }
}
