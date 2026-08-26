using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// Unity 側アダプタのテスト。Transform への書き出しと更新順だけを見る。
    /// 数値の正しさは Core 側のテストで担保している。
    /// </summary>
    public sealed class UniverseRootTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        GameObject NewGameObject(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        (UniverseRoot root, Transform ship, ShiftableBody marker) BuildMinimalScene(Vec3d markerAbsolute)
        {
            GameObject rootGo = NewGameObject("UniverseRoot");
            var root = rootGo.AddComponent<UniverseRoot>();

            GameObject driverGo = NewGameObject("OriginShiftDriver");
            var driver = driverGo.AddComponent<OriginShiftDriver>();

            GameObject shipGo = NewGameObject("Ship");

            GameObject markerGo = NewGameObject("Marker");
            var marker = markerGo.AddComponent<ShiftableBody>();
            marker.AbsolutePosition = markerAbsolute;

            root.Configure(driver, shipGo.transform);
            driver.Register(marker);
            root.Initialize();

            return (root, shipGo.transform, marker);
        }

        [Test]
        public void 初期化直後にマーカーの原点相対座標が書かれている()
        {
            var markerAbsolute = new Vec3d(50.0, 0.0, 0.0);
            (UniverseRoot root, Transform ship, ShiftableBody marker) = BuildMinimalScene(markerAbsolute);

            Assert.That(ship.position, Is.EqualTo(Vector3.zero));
            Assert.That(marker.transform.position.x, Is.EqualTo(50.0f).Within(1e-4f));
            Assert.That(root.Origin.ShiftCount, Is.GreaterThan(0));
        }

        [Test]
        public void 進んだぶんだけマーカーが逆向きに動き船は原点に留まる()
        {
            var markerAbsolute = new Vec3d(0.0, 0.0, 0.0);
            (UniverseRoot root, Transform ship, ShiftableBody marker) = BuildMinimalScene(markerAbsolute);

            // 0.9c で 1 固定ステップぶん。既定の初期速度は +Z 向き 0.9c。
            root.Tick(UniverseConstants.FixedDeltaSeconds);

            double expected = UniverseConstants.DefaultCruiseBeta
                              * UniverseConstants.SpeedOfLightKmPerSec
                              * UniverseConstants.FixedDeltaSeconds;

            Debug.Log($"[Step1] 0.9c / 60fps の 1 フレーム移動量 = {expected:F2} units");

            Assert.That(ship.position, Is.EqualTo(Vector3.zero), "船は常に原点");
            Assert.That(marker.transform.position.z, Is.EqualTo((float)(-expected)).Within(1.0f),
                "原点が前進したぶん、マーカーは後ろへ下がる");
            Assert.That(root.Ship.Position.Z, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void シーンからShiftableBodyを収集できる()
        {
            GameObject driverGo = NewGameObject("OriginShiftDriver");
            var driver = driverGo.AddComponent<OriginShiftDriver>();

            GameObject a = NewGameObject("A");
            a.AddComponent<ShiftableBody>();
            GameObject b = NewGameObject("B");
            b.AddComponent<ShiftableBody>();

            driver.CollectFromScene();

            Assert.That(driver.Bodies.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void 同じShiftableBodyを二重登録しない()
        {
            GameObject driverGo = NewGameObject("OriginShiftDriver");
            var driver = driverGo.AddComponent<OriginShiftDriver>();

            GameObject a = NewGameObject("A");
            var body = a.AddComponent<ShiftableBody>();

            driver.Register(body);
            driver.Register(body);

            Assert.That(driver.Bodies.Count, Is.EqualTo(1), "二重登録すると 2 倍シフトされる");
        }
    }
}
