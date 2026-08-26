using System.IO;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// TextMeshPro の Essential Resources を CLI から導入する (Step 4)。
    ///
    /// 未導入だと TMP のテキストがフォント無しで真っ白になる。
    /// GUI の "Window > TextMeshPro > Import TMP Essential Resources" と同じ処理を
    /// TMP_PackageResourceImporter.ImportResources で呼ぶ。
    ///
    /// **導入は別 Run にする。** .unitypackage の取り込みは
    /// アセットのインポートとドメインリロードを跨ぐので、同じ Run で
    /// シーン生成まで進めると途中で中断される (CLAUDE.md §5)。
    /// </summary>
    public static class TmpSetup
    {
        public const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static bool IsImported => File.Exists(SettingsPath);

        public static void ImportEssentials()
        {
            if (IsImported)
            {
                Debug.Log($"[TmpSetup] 導入済み: {SettingsPath}");
                return;
            }

            Debug.Log("[TmpSetup] TMP Essential Resources を取り込みます...");

            // TMP_PackageResourceImporter.ImportResources は内部で
            // AssetDatabase.ImportPackage を呼ぶが、これは**非同期**で
            // 次のエディタ tick まで完了しない。batchmode + -quit では
            // その tick が来ないので何も起きなかった (実測)。
            // 同期版の ImportPackageImmediately を使う。公開 API ではないので
            // リフレクションで引き、無ければ従来経路にフォールバックする。
            string packagePath = FindEssentialPackage();
            System.Reflection.MethodInfo immediate = typeof(AssetDatabase).GetMethod(
                "ImportPackageImmediately",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

            if (packagePath != null && immediate != null)
            {
                Debug.Log($"[TmpSetup] ImportPackageImmediately: {packagePath}");
                immediate.Invoke(null, new object[] { packagePath });
            }
            else
            {
                Debug.LogWarning("[TmpSetup] 同期取り込みが使えないので ImportResources にフォールバックします。");
                TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            }

            AssetDatabase.Refresh();

            Debug.Log(IsImported
                ? $"[TmpSetup] OK: {SettingsPath}"
                : "[TmpSetup] まだ反映されていません。次の Run で確認します。");
        }

        /// <summary>TMP Essential Resources.unitypackage の絶対パスを探す。</summary>
        static string FindEssentialPackage()
        {
            UnityEditor.PackageManager.PackageInfo info =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_Settings).Assembly);

            if (info == null)
            {
                Debug.LogWarning("[TmpSetup] TMP のパッケージ情報が取れない。");
                return null;
            }

            string path = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[TmpSetup] .unitypackage が見つからない: {path}");
                return null;
            }

            return path;
        }

        /// <summary>未導入なら例外で止める。シーン生成の前提条件。</summary>
        public static void RequireImported()
        {
            if (IsImported)
            {
                return;
            }

            throw new FileNotFoundException(
                "TMP Essential Resources が未導入です。先に " +
                ".\\tools\\run_unity.ps1 -Method SolarSetup.ImportTmp を実行してください。",
                SettingsPath);
        }
    }
}
