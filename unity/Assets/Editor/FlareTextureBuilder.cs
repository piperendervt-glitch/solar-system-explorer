using System.IO;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 光条（スパイク）の筋テクスチャを生成する (Step 9-3b)。
    ///
    /// **1 枚の筋を N 個の要素として角度をずらして使う。**
    /// スターバーストを 1 枚に焼き込むと本数がテクスチャに固定され、
    /// 実機で振れなくなる。筋なら要素の visible を切り替えるだけで本数を変えられる。
    ///
    /// **中心を通る両側の帯なので、要素 1 個で光条は 2 本に見える。**
    /// パネルの「光条の本数」は画面上の本数なので、要素数はその半分。
    ///
    /// コロナと同じ扱い: リニア取り込み (sRGB オフ)、Assets/Textures/ に置く。
    /// </summary>
    public static class FlareTextureBuilder
    {
        public const string FileName = "flare_streak.png";
        public const int Size = 256;

        /// <summary>筋の長さ方向の減衰。小さいほど遠くまで伸びる。</summary>
        public const float LengthFalloff = 1.5f;

        /// <summary>筋の太さ方向の減衰。大きいほど細くなる。</summary>
        public const float WidthFalloff = 6.0f;

        /// <summary>筋の色。太陽に合わせて少し暖色。</summary>
        public static readonly Color Tint = new Color(1.0f, 0.93f, 0.80f, 1f);

        public static string AssetPath => PlanetTextureSetup.TextureFolder + "/" + FileName;

        /// <summary>
        /// 中心を原点とする正規化座標 (u, v) ∈ [-1, 1]^2 での明るさ。
        ///
        /// **x 軸・y 軸それぞれで中心から単調に減り、四隅で 0 になる。**
        /// 斜め方向は筋の外なので早く 0 に落ちる。
        /// </summary>
        public static float Profile(float u, float v)
        {
            float au = Mathf.Abs(u);
            float av = Mathf.Abs(v);
            if (au >= 1f || av >= 1f)
            {
                return 0f;
            }

            return Mathf.Pow(1f - au, LengthFalloff) * Mathf.Pow(1f - av, WidthFalloff);
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
                    float u = (x + 0.5f - half) / half;
                    float v = (y + 0.5f - half) / half;
                    float p = Profile(u, v);

                    pixels[y * Size + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(Tint.r * p) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(Tint.g * p) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(Tint.b * p) * 255f),
                        255);
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
                throw new System.InvalidOperationException("光条のテクスチャを作れなかった: " + AssetPath);
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
