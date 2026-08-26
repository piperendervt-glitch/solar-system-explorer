using UnityEngine;

/// <summary>
/// batchmode のエントリポイント。
///
///   .\tools\run_unity.ps1 -Method SolarSetup.Run
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
    /// スクショ検証。シーン生成とは別 Run にする
    /// (CLAUDE.md §5「コンパイルやドメインリロードを跨ぐ処理は Run を分ける」)。
    ///
    ///   .\tools\run_unity.ps1 -Method SolarSetup.Capture
    /// </summary>
    public static void Capture()
    {
        SolarSystem.Editor.VerifyCapture.Run();
        Debug.Log("[SolarSetup] Capture OK");
    }
}
