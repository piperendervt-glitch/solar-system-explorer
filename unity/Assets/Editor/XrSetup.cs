using System.IO;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **XR の設定アセットを CLI から作る (Step 12-0)。**
    ///
    /// XR Plugin Management は本来 Project Settings の GUI で構成するが、
    /// **GUI を要する手順を持ち込まない**（CLAUDE.md §0-B）。ここで同じことを
    /// スクリプトから行い、生成物 (`Assets/XR/`) を追跡する。
    ///
    /// ■ **InitManagerOnStart は false にする。**
    /// これが true だと、ローダを登録した時点で**起動と同時に XR が立ち上がる。**
    /// このスパイクでは `-xr` / `-xrMock` を付けたときだけ `XrBoot` が立ち上げる。
    /// 無指定の平面の絵と測定を、XR の導入で動かさないため。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ConfigureXr
    /// </summary>
    public static class XrSetup
    {
        /// <summary>ローダの型名。**アセット名ではなく型名で登録する。**</summary>
        public const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        public const string MockLoaderType = "Unity.XR.MockHMD.MockHMDLoader";

        public static void Run()
        {
            XRGeneralSettingsPerBuildTarget perTarget = EnsurePerBuildTarget();
            XRGeneralSettings standalone = EnsureStandalone(perTarget);

            // **自動起動を切る。** 起動するかどうかは XrBoot が引数で決める。
            standalone.InitManagerOnStart = false;

            XRManagerSettings manager = standalone.Manager;
            foreach (string type in new[] { OpenXrLoaderType, MockLoaderType })
            {
                if (XRPackageMetadataStore.IsLoaderAssigned(type, BuildTargetGroup.Standalone))
                {
                    continue;
                }

                bool ok = XRPackageMetadataStore.AssignLoader(manager, type, BuildTargetGroup.Standalone);
                Debug.Log($"[XrSetup] ローダ登録 {type}: {(ok ? "OK" : "**失敗**")}");
            }

            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(standalone);
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[XrSetup] InitManagerOnStart={standalone.InitManagerOnStart}"
                      + $" / ローダ {manager.activeLoaders.Count} 件");

            foreach (XRLoader loader in manager.activeLoaders)
            {
                Debug.Log("[XrSetup]   " + (loader == null ? "(null)" : loader.name));
            }
        }

        static XRGeneralSettingsPerBuildTarget EnsurePerBuildTarget()
        {
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);

            if (perTarget != null)
            {
                return perTarget;
            }

            const string directory = "Assets/XR";
            const string path = directory + "/XRGeneralSettingsPerBuildTarget.asset";

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, path);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            return perTarget;
        }

        static XRGeneralSettings EnsureStandalone(XRGeneralSettingsPerBuildTarget perTarget)
        {
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            }

            return perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        }
    }
}
