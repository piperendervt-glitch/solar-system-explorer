using System.IO;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// コロナの放射状グラデーションを生成する (Step 9-2)。
    ///
    /// **外部素材を使わない。** 中心 白（少し橙） → 縁 透明の単純な減衰なので、
    /// 素材を持ち込むより生成したほうが出典管理も要らず、値も追える。
    ///
    /// **リニアで取り込む (sRGB オフ)。** 減衰の式をそのまま画素値にしたいので、
    /// sRGB エンコードを挟ませない。挟むと (1-r)^3 が画素で別の曲線になる。
    ///
    /// **アルファは使わない。** シェーダが Additive なので減衰は RGB に入れる。
    /// </summary>
    public static class CoronaTextureBuilder
    {
        public const string FileName = "sun_corona.png";
        public const int Size = 256;

        /// <summary>中心の色。少し橙に寄せる。**シェーダの _CoronaColor の既定と揃える。**</summary>
        public static readonly Color Core = new Color(1.0f, 0.82f, 0.55f, 1f);

        public static string AssetPath => PlanetTextureSetup.TextureFolder + "/" + FileName;

        /// <summary>
        /// テクスチャに入れる値。**中心からの正規化距離だけ (1 - r)。**
        /// r = 0 で 1.0、r = 1 で厳密に 0。**縁で切れないことが要件。**
        ///
        /// **減衰の指数はここでは掛けない (Step 9-4)。** 焼き込むと実機で
        /// 振れないので、シェーダが pow(この値, _Falloff) を計算する。
        /// </summary>
        public static float Profile(float r)
        {
            if (r >= 1f)
            {
                return 0f;
            }

            if (r <= 0f)
            {
                return 1f;
            }

            return 1f - r;
        }

        /// <summary>
        /// シェーダが出す最終的な減衰。テストと計算の突き合わせ用。
        /// </summary>
        public static float Shaped(float r, double falloff)
        {
            float d = Profile(r);
            return d <= 0f ? 0f : Mathf.Pow(d, (float)falloff);
        }

        public static Texture2D GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory(PlanetTextureSetup.TextureFolder);

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[Size * Size];
            float half = Size * 0.5f;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // 画素の中心で測る。+0.5 を忘れると中心が 1 画素ずれる。
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float p = Profile(r);

                    // **グレースケール。** 色はシェーダの _CoronaColor が持つ。
                    var v = (byte)Mathf.RoundToInt(Mathf.Clamp01(p) * 255f);
                    pixels[y * Size + x] = new Color32(v, v, v, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(AssetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Configure();

            var created = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (created == null)
            {
                throw new System.InvalidOperationException("コロナのテクスチャを作れなかった: " + AssetPath);
            }

            return created;
        }

        static void Configure()
        {
            var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException("TextureImporter が取れない: " + AssetPath);
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
