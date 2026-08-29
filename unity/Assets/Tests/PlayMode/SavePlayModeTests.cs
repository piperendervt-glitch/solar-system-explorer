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
    /// セーブと復元 (Step 7)。
    ///
    /// セーブ先はテスト用の一時ファイルへ差し替える。
    /// 本物の persistentDataPath を汚すと、他のテストの開始位置が変わってしまう。
    /// </summary>
    public sealed class SavePlayModeTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        static string TempSavePath =>
            Path.Combine(Path.GetTempPath(), "solar-system-explorer-test.save.json");

        [SetUp]
        public void SetUp()
        {
            SaveFile.OverridePath = TempSavePath;
            SaveFile.Delete();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        static IEnumerator Restart()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        /// <summary>目標のポートへ着けてドッキングを完了させる。</summary>
        static void DockAt(UniverseRoot root, ShipRig rig, int stationIndex)
        {
            SpaceStation station = root.Model.Stations[stationIndex];
            rig.SetTargetIndex(stationIndex);

            // 到着圏の内側・停止・ポート正面。
            // **要求の判定はポート位置から測る (Step 13-3b)。**
            // 以前は中心から PortStandoff + 5.0 に置いていたが、
            // 要求可能距離が 20 -> 2.0 になったのでポート基準で 1.0 units に置く。
            root.PlaceObserver(station.PortPosition + station.PortDirection * 1.0);
            Vec3d port = station.PortDirection;
            rig.ShipTransform.rotation = Quaternion.LookRotation(
                new Vector3((float)-port.X, (float)-port.Y, (float)-port.Z), Vector3.up);

            rig.InputOverride = new FlightInput { JumpIndex = -1, DockRequest = true };
            root.Tick(Dt);
            rig.InputOverride = null;

            // 補間 5 秒ぶん回す。
            for (int i = 0; i < 400 && rig.Docking.State != DockingState.Docked; i++)
            {
                root.Tick(Dt);
            }
        }

        [UnityTest]
        public IEnumerator ファイルが無ければ地球ステーションから始まる()
        {
            Assert.That(SaveFile.Exists(), Is.False, "前提: セーブが無い");

            yield return Restart();
            var root = Object.FindAnyObjectByType<UniverseRoot>();

            Debug.Log($"[Step7] セーブ無しの開始地点: {root.StartStationName}");
            Assert.That(root.StartStationName, Is.EqualTo(root.Model.Stations[0].Name));
            Assert.That(root.StartStationName, Does.Contain("Earth"));
        }

        [UnityTest]
        public IEnumerator 壊れたセーブでも落ちず地球から始まる()
        {
            File.WriteAllText(TempSavePath, "{これはJSONではない");

            yield return Restart();
            var root = Object.FindAnyObjectByType<UniverseRoot>();

            Debug.Log($"[Step7] 壊れたセーブの開始地点: {root.StartStationName}");
            Assert.That(root.StartStationName, Does.Contain("Earth"));
        }

        [UnityTest]
        public IEnumerator 未知のステーション名でも落ちず地球から始まる()
        {
            File.WriteAllText(TempSavePath, SaveCodec.Serialize("PLUTO STATION"));

            yield return Restart();
            var root = Object.FindAnyObjectByType<UniverseRoot>();

            Debug.Log($"[Step7] 未知名のセーブの開始地点: {root.StartStationName}");
            Assert.That(root.StartStationName, Does.Contain("Earth"));
        }

        [UnityTest]
        public IEnumerator 地球と火星を往復して都度保存と復元がされる()
        {
            // ---- 1 回目の起動: セーブ無し -> 地球 ----
            yield return Restart();
            var root = Object.FindAnyObjectByType<UniverseRoot>();
            var rig = Object.FindAnyObjectByType<ShipRig>();
            Debug.Log($"[Step7] 起動 1: {root.StartStationName} (セーブ無し)");
            Assert.That(root.StartStationName, Does.Contain("Earth"));

            // ---- 火星ステーションへドッキング -> 保存 ----
            DockAt(root, rig, 1);
            Assert.That(rig.Docking.State, Is.EqualTo(DockingState.Docked), "火星へ着けていない");

            string afterMars = SaveFile.ReadRaw();
            Debug.Log($"[Step7] 火星ドッキング後のファイル: {afterMars}");
            Assert.That(SaveCodec.TryParse(afterMars, out string savedMars), Is.True);
            Assert.That(savedMars, Is.EqualTo(root.Model.Stations[1].Name));

            // ---- 再起動 -> 火星から始まる ----
            yield return Restart();
            root = Object.FindAnyObjectByType<UniverseRoot>();
            rig = Object.FindAnyObjectByType<ShipRig>();
            Debug.Log($"[Step7] 起動 2: {root.StartStationName}");
            Assert.That(root.StartStationName, Is.EqualTo(root.Model.Stations[1].Name));
            Assert.That(rig.TargetIndex, Is.EqualTo(1), "目標も火星に追従する");

            double toMarsPort = Vec3d.Distance(root.Ship.Position, root.Model.Stations[1].PortPosition);
            Debug.Log($"[Step7] 起動 2 の位置と火星ポートの差: {toMarsPort:E3} units");
            Assert.That(toMarsPort, Is.LessThan(1e-6), "火星ポートに着いた状態で始まる");

            // ---- 地球ステーションへ戻る -> 保存 ----
            DockAt(root, rig, 0);
            Assert.That(rig.Docking.State, Is.EqualTo(DockingState.Docked), "地球へ戻れていない");

            string afterEarth = SaveFile.ReadRaw();
            Debug.Log($"[Step7] 地球ドッキング後のファイル: {afterEarth}");
            Assert.That(SaveCodec.TryParse(afterEarth, out string savedEarth), Is.True);
            Assert.That(savedEarth, Is.EqualTo(root.Model.Stations[0].Name));

            // ---- 再起動 -> 地球から始まる ----
            yield return Restart();
            root = Object.FindAnyObjectByType<UniverseRoot>();
            Debug.Log($"[Step7] 起動 3: {root.StartStationName}");
            Assert.That(root.StartStationName, Is.EqualTo(root.Model.Stations[0].Name));
        }
    }
}
