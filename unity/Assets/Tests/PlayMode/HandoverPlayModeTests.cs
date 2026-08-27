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
    /// 引き渡し帯の整合 (Step 8-5)。
    ///
    /// **非活性のオブジェクトの値は判定に使わない。**
    /// 引き渡し率 0 の距離では RealMesh / RealCloudMesh が非活性で、
    /// スケール未設定 (初期値 1) や前フレームの残値が読める。
    /// 一致判定は必ず「両方が活性」の距離で行うこと。
    /// </summary>
    public sealed class HandoverPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;
        /// <summary>
        /// 計器パネルが占める行数。**GetPixels32 は下から上に並ぶ。**
        /// パネルは画面下端にあるので、除外するのは y の小さい側。
        /// (以前は y < Height - PanelTop と書いており、画面下 1/3 だけを
        ///  走査していた。円盤が小さい距離では標本が 0 個になっていた)
        /// </summary>
        const int PanelRows = 380;

        UniverseRoot _root;
        CameraStackController _stack;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-handover.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            Assert.That(_root, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        CelestialBodyView Earth()
        {
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body.Name == "Earth") { return v; }
            }

            return null;
        }

        void PlaceAt(double distance)
        {
            // **昼側にする。** 何もしないと観測者 (+X 側) から見て太陽は地球の裏で、
            // 夜側を見ることになり大気の縁が暗くて測れない。
            _root.SetSunDirectionOverride(new Vec3d(-1.0, 0.0, 0.0));
            _root.PlaceObserver(_root.Model.Earth.AbsolutePosition + new Vec3d(1.0, 0.0, 0.0) * distance);

            // **機首を地球へ向ける。** PlaceObserver は位置だけを動かすので、
            // 向けないと円盤が画面外に出て、縁の測定が全部 0 になる。
            var rig = Object.FindAnyObjectByType<ShipRig>();
            if (rig != null && rig.ShipTransform != null)
            {
                rig.ShipTransform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0f), Vector3.up);
            }

            for (int i = 0; i < 4; i++)
            {
                _root.Tick(Dt);
            }
        }

        Transform RealMesh() => Earth().transform.Find("RealAnchor/RealSpin/RealMesh");
        Transform RealCloud() => Earth().transform.Find("RealAnchor/RealCloudSpin/RealCloudMesh");

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

        [UnityTest]
        public IEnumerator 帯の中央で殻と実スケールの世界回転が一致する()
        {
            yield return null;
            PlaceAt(4.0e4);

            CelestialBodyView earth = Earth();
            Transform realMesh = RealMesh();
            Transform realCloud = RealCloud();

            // **両方が活性であることを先に確かめる。** 非活性なら値に意味が無い。
            Assert.That(earth.Mesh.gameObject.activeInHierarchy, Is.True, "殻が非活性");
            Assert.That(realMesh.gameObject.activeInHierarchy, Is.True, "実スケールが非活性");
            Assert.That(earth.CloudMesh.gameObject.activeInHierarchy, Is.True, "殻の雲が非活性");
            Assert.That(realCloud.gameObject.activeInHierarchy, Is.True, "実スケールの雲が非活性");

            float surface = Quaternion.Angle(earth.Mesh.rotation, realMesh.rotation);
            float cloud = Quaternion.Angle(earth.CloudMesh.rotation, realCloud.rotation);

            Debug.Log($"[Step8-5] 引き渡し率 {earth.RealScaleBlend:F3} / 地表の世界回転のずれ {surface:F3} 度 / 雲のずれ {cloud:F3} 度");

            // 直す前は root の LookRotation(dir) が殻側だけに乗って 90.00 度ずれていた。
            Assert.That(surface, Is.LessThan(0.01f), "地表の位相がずれている");
            Assert.That(cloud, Is.LessThan(0.01f), "雲の位相がずれている");
        }

        [UnityTest]
        public IEnumerator 帯の中央で雲の半径比が殻と実スケールとも一致する()
        {
            yield return null;
            PlaceAt(4.0e4);

            CelestialBodyView earth = Earth();
            Transform realMesh = RealMesh();
            Transform realCloud = RealCloud();

            Assert.That(realMesh.gameObject.activeInHierarchy, Is.True, "非活性の値は使わない");
            Assert.That(realCloud.gameObject.activeInHierarchy, Is.True, "非活性の値は使わない");

            float proxyRatio = earth.CloudMesh.localScale.x / earth.Mesh.localScale.x;
            float realRatio = realCloud.localScale.x / realMesh.localScale.x;

            Debug.Log($"[Step8-5] 雲の半径比 殻 {proxyRatio:F4} / 実スケール {realRatio:F4} (定数 {CloudLayer.RadiusScale})");

            Assert.That(proxyRatio, Is.EqualTo(CloudLayer.RadiusScale).Within(1e-4f));
            Assert.That(realRatio, Is.EqualTo(CloudLayer.RadiusScale).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator 活性化した最初のフレームで雲の半径比が正しい()
        {
            yield return null;

            // 帯の外側 = 非活性から始める。
            PlaceAt(5.2e4);
            Assert.That(RealCloud().gameObject.activeInHierarchy, Is.False, "前提: まだ非活性");

            // 1 Tick だけ進めて帯の中へ入れる。**活性化した最初のフレーム。**
            _root.PlaceObserver(_root.Model.Earth.AbsolutePosition + new Vec3d(1.0, 0.0, 0.0) * 4.0e4);
            _root.Tick(Dt);

            Transform realMesh = RealMesh();
            Transform realCloud = RealCloud();
            Assert.That(realCloud.gameObject.activeInHierarchy, Is.True, "活性化していない");

            float ratio = realCloud.localScale.x / realMesh.localScale.x;
            Debug.Log($"[Step8-5] 活性化した最初のフレームの雲の半径比 {ratio:F4}");

            // スケールを入れてから活性化しないと、ここで 1.0000 になる。
            Assert.That(ratio, Is.EqualTo(CloudLayer.RadiusScale).Within(1e-4f),
                "活性化の順序が逆。スケールを入れてから SetActive すること");
        }

        [UnityTest]
        public IEnumerator 帯を通過しても大気の縁の色が跳ねない()
        {
            yield return null;

            var blues = new System.Collections.Generic.List<float>();
            double[] distances = { 5.4e4, 5.0e4, 4.6e4, 4.2e4, 3.8e4, 3.4e4, 3.0e4, 2.6e4 };

            foreach (double d in distances)
            {
                PlaceAt(d);
                float radius = (float)Earth().LastAngularPixels * 0.5f;
                Texture2D shot = Render();
                try
                {
                    Color32[] px = shot.GetPixels32();
                    double sum = 0;
                    int n = 0;
                    for (int y = PanelRows; y < Height; y++)
                    {
                        for (int x = 0; x < Width; x++)
                        {
                            float dx = x - Width * 0.5f;
                            float dy = y - Height * 0.5f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            if (r < radius - 3f || r > radius)
                            {
                                continue;
                            }

                            sum += px[y * Width + x].b;
                            n++;
                        }
                    }

                    blues.Add(n > 0 ? (float)(sum / n) : 0f);
                }
                finally
                {
                    Object.DestroyImmediate(shot);
                }
            }

            var text = new System.Text.StringBuilder("[Step8-5] 縁の青: ");
            for (int i = 0; i < distances.Length; i++)
            {
                text.Append(distances[i].ToString("E1")).Append("=").Append(blues[i].ToString("F1")).Append(" ");
            }

            Debug.Log(text.ToString());

            float worst = 0f;
            for (int i = 1; i < blues.Count; i++)
            {
                worst = Mathf.Max(worst, Mathf.Abs(blues[i] - blues[i - 1]));
            }

            Debug.Log($"[Step8-5] 隣り合う距離での縁の青の最大変化 {worst:F1}");
            Assert.That(worst, Is.LessThan(40f), "帯の通過で縁の色が跳ねている");
        }
    }
}
