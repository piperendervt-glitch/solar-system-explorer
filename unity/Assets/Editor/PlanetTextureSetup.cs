using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 惑星の追加マップを 4k へ縮小して取り込む (Step 8-1)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.ImportPlanetTextures
    ///
    /// 原本は source_assets/textures/ (gitignore の内側)。8k のまま追跡すると
    /// public リポジトリが肥大するので、4096x2048 に落としてから Assets/ へ置く。
    ///
    /// **.tif は Texture2D.LoadImage が読めない。** 一度 Unity にインポートさせて
    /// から画素を読む。そのための一時フォルダを使い、終わったら消す。
    ///
    /// **normal / specular はガンマ変換を挟まない。** 生の線形データとして縮小する。
    /// albedo 系 (clouds / nightmap) は sRGB 符号化のまま箱型フィルタを掛ける
    /// (近似。docs/02-demo2-plan.md 8-1 に記載)。
    /// </summary>
    public static class PlanetTextureSetup
    {
        public const string TextureFolder = "Assets/Textures";
        public const string TempFolder = "Assets/Textures/_import_tmp";

        public const int TargetWidth = 4096;
        public const int TargetHeight = 2048;
        public const int JpegQuality = 92;

        /// <summary>出力の種類。色空間と形式がここで決まる。</summary>
        public enum MapKind
        {
            /// <summary>見た目の色。sRGB / JPEG 可。</summary>
            ColorSrgb,

            /// <summary>接空間法線。NormalMap / PNG 必須。</summary>
            Normal,

            /// <summary>マスク。Linear / PNG。</summary>
            LinearMask,
        }

        public struct MapSpec
        {
            public string Source;
            public string Output;
            public MapKind Kind;
        }

        public static readonly MapSpec[] Maps =
        {
            new MapSpec { Source = "8k_earth_clouds.jpg",       Output = "4k_earth_clouds.jpg",   Kind = MapKind.ColorSrgb },
            new MapSpec { Source = "8k_earth_nightmap.jpg",     Output = "4k_earth_nightmap.jpg", Kind = MapKind.ColorSrgb },
            new MapSpec { Source = "8k_earth_normal_map.tif",   Output = "4k_earth_normal.png",   Kind = MapKind.Normal },
            new MapSpec { Source = "8k_earth_specular_map.tif", Output = "4k_earth_specular.png", Kind = MapKind.LinearMask },
        };

        public static string SourceDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../source_assets/textures"));

        public static string AssetPath(string fileName) => TextureFolder + "/" + fileName;

        public static bool IsImported
        {
            get
            {
                foreach (MapSpec m in Maps)
                {
                    if (!File.Exists(AssetPath(m.Output)))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>取り込み済みでなければ、足りないものを並べて止まる。</summary>
        public static void RequireImported()
        {
            if (IsImported)
            {
                return;
            }

            var missing = new List<string>();
            foreach (MapSpec m in Maps)
            {
                if (!File.Exists(AssetPath(m.Output)))
                {
                    missing.Add(m.Output);
                }
            }

            throw new FileNotFoundException(
                "惑星の追加マップが取り込まれていない: " + string.Join(", ", missing) + "\n" +
                "  run_unity.ps1 -Method SolarSetup.ImportPlanetTextures を実行すること。\n" +
                "  原本の入手元は docs/asset-sources.md。");
        }

        public static void Import()
        {
            if (!Directory.Exists(SourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    "原本のフォルダが無い: " + SourceDirectory + "\n" +
                    "  docs/asset-sources.md のとおりに配置すること。");
            }

            var missing = new List<string>();
            foreach (MapSpec m in Maps)
            {
                if (!File.Exists(Path.Combine(SourceDirectory, m.Source)))
                {
                    missing.Add(m.Source);
                }
            }

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    "原本が足りない: " + string.Join(", ", missing) + "\n  場所: " + SourceDirectory);
            }

            Directory.CreateDirectory(TextureFolder);
            PrepareTempFolder();

            try
            {
                foreach (MapSpec map in Maps)
                {
                    Convert(map);
                }
            }
            finally
            {
                DeleteTempFolder();
            }

            AssetDatabase.Refresh();
            LogSizes();
        }

        static void Convert(MapSpec map)
        {
            string temp = TempFolder + "/" + map.Source;
            File.Copy(Path.Combine(SourceDirectory, map.Source), temp, overwrite: true);
            AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);

            // **生のバイト値で読む。** sRGB 変換もミップも圧縮も挟まない。
            var importer = (TextureImporter)AssetImporter.GetAtPath(temp);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(temp);
            if (source == null)
            {
                throw new InvalidDataException("読み込めなかった: " + temp);
            }

            Texture2D scaled = BoxDownscale(source, TargetWidth, TargetHeight);
            try
            {
                bool jpeg = map.Output.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase);
                byte[] bytes = jpeg ? scaled.EncodeToJPG(JpegQuality) : scaled.EncodeToPNG();
                File.WriteAllBytes(AssetPath(map.Output), bytes);

                Debug.Log($"[PlanetTextureSetup] {map.Source} ({source.width}x{source.height}) -> " +
                          $"{map.Output} ({scaled.width}x{scaled.height} / {bytes.Length / 1024} KB)");
            }
            finally
            {
                Object.DestroyImmediate(scaled);
            }

            AssetDatabase.ImportAsset(AssetPath(map.Output), ImportAssetOptions.ForceSynchronousImport);
            ConfigureOutput(AssetPath(map.Output), map.Kind);
        }

        /// <summary>
        /// 2x2 の箱型フィルタ。**ガンマ変換を挟まない。**
        /// 法線マップはこれでベクトルが非正規化されるが、シェーダ側で normalize する。
        /// </summary>
        public static Texture2D BoxDownscale(Texture2D source, int width, int height)
        {
            int sx = source.width / width;
            int sy = source.height / height;
            if (sx < 1 || sy < 1)
            {
                throw new InvalidDataException(
                    $"原本が小さすぎる: {source.width}x{source.height} -> {width}x{height}");
            }

            Color32[] src = source.GetPixels32();
            var dst = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < sy; j++)
                    {
                        int row = (y * sy + j) * source.width;
                        for (int i = 0; i < sx; i++)
                        {
                            Color32 c = src[row + x * sx + i];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }

                    int n = sx * sy;
                    dst[y * width + x] = new Color32(
                        (byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
            result.SetPixels32(dst);
            result.Apply();
            return result;
        }

        static void ConfigureOutput(string path, MapKind kind)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException("TextureImporter が取れない: " + path);
            }

            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = TargetWidth;
            importer.isReadable = false;

            switch (kind)
            {
                case MapKind.Normal:
                    importer.textureType = TextureImporterType.NormalMap;
                    break;

                case MapKind.LinearMask:
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false; // 輝度ではなくマスク
                    break;

                default:
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    break;
            }

            importer.SaveAndReimport();
        }

        static void PrepareTempFolder()
        {
            DeleteTempFolder();
            Directory.CreateDirectory(TempFolder);
            AssetDatabase.Refresh();
        }

        static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }

            if (Directory.Exists(TempFolder))
            {
                Directory.Delete(TempFolder, recursive: true);
            }

            string meta = TempFolder + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }

        static void LogSizes()
        {
            long added = 0;
            foreach (MapSpec m in Maps)
            {
                added += new FileInfo(AssetPath(m.Output)).Length;
            }

            long total = 0;
            foreach (string f in Directory.GetFiles(TextureFolder))
            {
                if (!f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                {
                    total += new FileInfo(f).Length;
                }
            }

            Debug.Log($"[PlanetTextureSetup] 追加分 {added / 1048576.0:F2} MB (目安 30 MB 以内) / " +
                      $"Assets/Textures 合計 {total / 1048576.0:F2} MB");
        }
    }
}
