using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

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
            PrimeSettings();
            Debug.Log("[XrProbe] 開始前: " + XrBoot.ReadStereoFacts());

            XrBoot.Result result = XrBoot.Initialize(XrBoot.Mode.Mock);
            Debug.Log("[XrProbe] 結果: " + result);
            Debug.Log("[XrProbe] 開始後: " + XrBoot.ReadStereoFacts());

            XrBoot.Shutdown();
            Debug.Log("[XrProbe] 終了後: " + XrBoot.ReadStereoFacts());
        }

        /// <summary>
        /// **Editor でだけ要る前準備 (Step 12-0d)。**
        ///
        /// `XRGeneralSettings.Instance` は `XRGeneralSettingsPerBuildTarget.OnEnable()`
        /// が入れる。batchmode の `-executeMethod` では**そのアセットが読み込まれるとは
        /// 限らない**ので、コンパイルが走った回だけ Instance が入り、走らない回は
        /// null になる（12-0d で実測。同じコードで初期化が成功したり失敗したりする）。
        ///
        /// ここで先に読ませて、**測定の当たり外れを潰す。**
        /// プレイヤー（exe）では preloaded asset として必ず読まれるので、これは
        /// **Editor だけの事情。`XrBoot` 側には持ち込まない。**
        /// </summary>
        static void PrimeSettings()
        {
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out ScriptableObject perTarget);

            Debug.Log($"[XrProbe] 設定の読み込み: perTarget={(perTarget == null ? "(無し)" : perTarget.name)}"
                      + $" / Instance={(XRGeneralSettings.Instance == null ? "null" : "有り")}");
        }
    }
}
