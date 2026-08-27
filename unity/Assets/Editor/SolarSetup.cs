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
    /// source_assets/ のテクスチャを取り込む (Step 6)。
    /// アセットのインポートを跨ぐので別 Run にする。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportTextures
    /// </summary>
    public static void ImportTextures()
    {
        SolarSystem.Editor.TextureSetup.Import();
        Debug.Log("[SolarSetup] ImportTextures OK");
    }

    /// <summary>
    /// 惑星の追加マップ (雲 / 夜景 / 法線 / 鏡面) を 4k に縮小して取り込む (Step 8-1)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportPlanetTextures
    ///
    /// アセットのインポートを跨ぐので別 Run にする。
    /// </summary>
    public static void ImportPlanetTextures()
    {
        SolarSystem.Editor.PlanetTextureSetup.Import();
        Debug.Log("[SolarSetup] ImportPlanetTextures OK");
    }

    /// <summary>
    /// シナリオの初期状態を撮る (Step 8-0)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.CaptureScenario
    ///   run_unity.ps1 -Method SolarSetup.CaptureScenario -ExtraArgs "-scenario","harness-selftest"
    ///   run_unity.ps1 -Method SolarSetup.CaptureScenario -ExtraArgs "-hero"
    ///
    /// 既定は verify/shots/ へフル解像度 PNG。-hero で docs/screenshots/demo2/ へ 1280x720 JPEG。
    /// </summary>
    public static void CaptureScenario()
    {
        SolarSystem.Editor.ScenarioCapture.Run();
        Debug.Log("[SolarSetup] CaptureScenario OK");
    }

    /// <summary>
    /// Windows 向けスタンドアロンビルド (Step 7)。出力は build/。
    ///
    ///   run_unity.ps1 -Method SolarSetup.Build -TimeoutMinutes 30
    /// </summary>
    public static void Build()
    {
        SolarSystem.Editor.PlayerBuilder.Build();
        Debug.Log("[SolarSetup] Build OK");
    }

    /// <summary>
    /// コックピットの .unitypackage を取り込む (Step 11-1a)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportCockpit
    ///   run_unity.ps1 -Method SolarSetup.ImportCockpit -ExtraArgs "-package","&lt;path&gt;"
    ///
    /// **取り込み先を書き換えてから取り込む。** ImportPackageImmediately は
    /// 宛先の引数を持たず、素のままだと .gitignore の外へ展開されるため。
    /// </summary>
    public static void ImportCockpit()
    {
        SolarSystem.Editor.CockpitImporter.Run();
        Debug.Log("[SolarSetup] ImportCockpit OK");
    }

}
