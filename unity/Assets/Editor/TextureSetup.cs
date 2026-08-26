using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// source_assets/ の原本を unity/Assets/Textures/ へ取り込む (Step 6)。
    ///
    /// source_assets/ は .gitignore で除外してあるので、リポジトリを clone した
    /// だけでは存在しない。無ければ**足りないファイル名を並べて止まる**
    /// (TmpSetup.RequireImported と同じ流儀)。
    ///
    /// 入手元とライセンスは docs/02-assets.md に記録してある。
    /// Solar System Scope のテクスチャは CC BY 4.0 でクレジット表記が必須。
    ///
    /// **取り込みは別 Run にする。** テクスチャのインポートは
    /// アセットのインポートを跨ぐので、同じ Run でシーン生成まで進めると
    /// マテリアルが古い参照のまま保存される (CLAUDE.md §5)。
    /// </summary>
    public static class TextureSetup
    {
        public const string TextureFolder = "Assets/Textures";

        public const string StarMap = "starmap_2020_4k.exr";
        public const string EarthAlbedo = "8k_earth_daymap.jpg";
        public const string MarsAlbedo = "8k_mars.jpg";
        public const string SunAlbedo = "8k_sun.jpg";

        static readonly string[] Required = { StarMap, EarthAlbedo, MarsAlbedo, SunAlbedo };

        static string SourceFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../source_assets"));

        public static string AssetPath(string fileName) => $"{TextureFolder}/{fileName}";

        public static bool IsImported =>
            Required.All(f => File.Exists(AssetPath(f)));

        /// <summary>未取り込みなら、足りないものを並べて例外で止める。</summary>
        public static void RequireImported()
        {
            if (IsImported)
            {
                return;
            }

            string[] missing = Required.Where(f => !File.Exists(AssetPath(f))).ToArray();
            throw new FileNotFoundException(
                "テクスチャが未取り込みです。\n" +
                $"  足りないもの: {string.Join(", ", missing)}\n" +
                $"  原本の置き場: {SourceFolder}\n" +
                "  先に .\\tools\\run_unity.ps1 -Method SolarSetup.ImportTextures を実行してください。\n" +
                "  入手元とライセンスは docs/02-assets.md を参照。");
        }

        public static void Import()
        {
            if (!Directory.Exists(SourceFolder))
            {
                throw new DirectoryNotFoundException(
                    $"原本のフォルダがありません: {SourceFolder}\n" +
                    "  docs/02-assets.md の表にある入手元から、同じファイル名で置いてください。");
            }

            var missing = new List<string>();
            foreach (string file in Required)
            {
                if (!File.Exists(Path.Combine(SourceFolder, file)))
                {
                    missing.Add(file);
                }
            }

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    $"原本が足りません ({SourceFolder}):\n" +
                    string.Join("\n", missing.Select(m => "  - " + m)) +
                    "\n  docs/02-assets.md の表にある入手元から、同じファイル名で置いてください。");
            }

            Directory.CreateDirectory(TextureFolder);

            foreach (string file in Required)
            {
                string source = Path.Combine(SourceFolder, file);
                string destination = AssetPath(file);
                File.Copy(source, destination, overwrite: true);
                Debug.Log($"[TextureSetup] 複製: {file} ({new FileInfo(source).Length / 1024 / 1024} MB)");
            }

            AssetDatabase.Refresh();

            ConfigureSkybox(AssetPath(StarMap));
            ConfigureAlbedo(AssetPath(EarthAlbedo));
            ConfigureAlbedo(AssetPath(MarsAlbedo));
            ConfigureAlbedo(AssetPath(SunAlbedo));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TextureSetup] OK: {Required.Length} 件を {TextureFolder} へ取り込みました。");
        }

        /// <summary>星図は緯度経度パノラマ。Cubemap として読ませる。</summary>
        static void ConfigureSkybox(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[TextureSetup] インポータが取れない: {path}");
                return;
            }

            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = false; // EXR はリニア
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        static void ConfigureAlbedo(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[TextureSetup] インポータが取れない: {path}");
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 8192;
            importer.SaveAndReimport();
        }
    }
}
