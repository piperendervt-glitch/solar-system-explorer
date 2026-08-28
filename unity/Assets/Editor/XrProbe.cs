using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **MockHMD がどこで初期化できるかを確かめる (Step 12-0c)。**
    ///
    /// batchmode の Editor で立ち上がるなら、当初の計画どおり CI 相当の検証が
    /// batchmode で回る。立ち上がらずスタンドアロン exe が要るなら、
    /// **計画書 §0-B に足すホップが 1 つ増え、撮影経路の設計も変わる。**
    ///
    /// **成否の判定はしない。** 立ち上がったかどうかと、そのときの立体視の実態を
    /// ログに残すだけ。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ProbeXrMock -ExtraArgs '-xrMock'
    /// </summary>
    public static class XrProbe
    {
        public static void Run()
        {
            Debug.Log("[XrProbe] 開始前: " + XrBoot.ReadStereoFacts());

            XrBoot.Result result = XrBoot.Initialize(XrBoot.Mode.Mock);
            Debug.Log("[XrProbe] 結果: " + result);
            Debug.Log("[XrProbe] 開始後: " + XrBoot.ReadStereoFacts());

            XrBoot.Shutdown();
            Debug.Log("[XrProbe] 終了後: " + XrBoot.ReadStereoFacts());
        }
    }
}
