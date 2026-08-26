using UnityEngine;

/// <summary>
/// batchmode のエントリポイント。
///
    ///   run_unity.ps1 -Method SolarSetup.ImportTmp
///
/// CLAUDE.md §3 のとおり **名前空間を付けない**。-executeMethod には
/// クラス名とメソッド名だけを渡す。実体は SolarSystem.Editor.SceneBuilder。
/// </summary>
public static class SolarSetup
{
    public static void Run()
    {
        SolarSystem.Editor.SceneBuilder.Build();
        Debug.Log("[SolarSetup] OK");
    }

    /// <summary>
    /// TextMeshPro の Essential Resources を取り込む (Step 4)。
    /// .unitypackage の取り込みはドメインリロードを跨ぐので別 Run にする。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportTmp
    /// </summary>
    public static void ImportTmp()
    {
        SolarSystem.Editor.TmpSetup.ImportEssentials();
        Debug.Log("[SolarSetup] ImportTmp OK");
    }

    /// <summary>
    /// スクショ検証。シーン生成とは別 Run にする
    /// (CLAUDE.md §5「コンパイルやドメインリロードを跨ぐ処理は Run を分ける」)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportTmp
    /// </summary>
    public static void Capture()
    {
        SolarSystem.Editor.VerifyCapture.Run();
        Debug.Log("[SolarSetup] Capture OK");
    }
}
