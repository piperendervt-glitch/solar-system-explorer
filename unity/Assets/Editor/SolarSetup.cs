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
    /// <summary>
    /// 計器の RT を CPU へ読み戻して PNG と寸法を出す (Step 11-3c)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.DumpScreenTextures
    /// </summary>
    public static void DumpScreenTextures()
    {
        SolarSystem.Editor.ScreenTextureDump.Run();
        Debug.Log("[SolarSetup] DumpScreenTextures OK");
    }

    public static void ImportCockpit()
    {
        SolarSystem.Editor.CockpitImporter.Run();
        Debug.Log("[SolarSetup] ImportCockpit OK");
    }

    /// <summary>
    /// 取り込んだアセットを URP へ変換する (Step 11-1b)。取り込み直後にも走るので、
    /// **これを単独で呼ぶのは取り込み直さずに変換だけやり直したいとき。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.ConvertCockpitToUrp
    /// </summary>
    public static void ConvertCockpitToUrp()
    {
        SolarSystem.Editor.UrpConversion.Run();
        Debug.Log("[SolarSetup] ConvertCockpitToUrp OK");
    }

    /// <summary>
    /// **変換器が実際に効くことを確かめる（陽性対照。Step 11-1b）。**
    ///
    /// Hi-Rez は既に URP なので変換は 0 件で終わる。それだけでは「呼んだ」と
    /// 「効いた」の区別がつかないので、Standard のマテリアルを 1 枚わざと作って
    /// 変換されることを見る。**11-6 で有料アセットを入れる前にもう一度走らせる。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.VerifyUrpConversion
    /// </summary>
    public static void VerifyUrpConversion()
    {
        SolarSystem.Editor.UrpConversion.RunPositiveControl();
        Debug.Log("[SolarSetup] VerifyUrpConversion OK");
    }

    /// <summary>
    /// 取り込んだマテリアルの棚卸し (Step 11-1c)。**何も書き換えない。**
    ///
    ///   run_unity.ps1 -Method SolarSetup.InventoryCockpit
    /// </summary>
    public static void InventoryCockpit()
    {
        SolarSystem.Editor.CockpitInventory.Log();
        Debug.Log("[SolarSetup] InventoryCockpit OK");
    }

}
