using System.IO;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 単色マテリアルを Assets/Materials/ に作る (決定 D-22: テクスチャは Step 6 まで入れない)。
    ///
    /// シーンと同じくコードから生成する。手で作ると差し替え時に取り違える。
    /// </summary>
    public static class MaterialLibrary
    {
        const string Folder = "Assets/Materials";

        public static Material PointMaterial(CelestialBody body)
        {
            // 光点は常に自己発光。距離が離れても暗くならないよう Unlit にする。
            return GetOrCreate(
                $"{body.Name}_Point",
                "Universal Render Pipeline/Unlit",
                ToColor(body.Color),
                transparent: true,
                emissive: false);
        }

        public static Material MeshMaterial(CelestialBody body)
        {
            Texture2D albedo = AlbedoFor(body);

            if (body.Kind == CelestialBodyKind.Star)
            {
                // 太陽は自ら光る。Lit にすると自分の光が当たらず真っ黒になる。
                return GetOrCreate(
                    $"{body.Name}_Mesh",
                    "Universal Render Pipeline/Unlit",
                    ToColor(body.Color),
                    transparent: true,
                    emissive: false,
                    albedo: albedo);
            }

            return GetOrCreate(
                $"{body.Name}_Mesh",
                "Universal Render Pipeline/Lit",
                ToColor(body.Color),
                transparent: true,
                emissive: false,
                albedo: albedo);
        }

        /// <summary>不透明な単色マテリアル (コックピットの枠など)。</summary>
        public static Material SolidMaterial(string name, Color color)
            => GetOrCreate(name, "Universal Render Pipeline/Lit", color, transparent: false, emissive: false);

        /// <summary>天体ごとのアルベドテクスチャ。無い天体は null。</summary>
        public static Texture2D AlbedoFor(CelestialBody body)
        {
            string file = null;
            if (body.Name == "Earth") file = TextureSetup.EarthAlbedo;
            else if (body.Name == "Mars") file = TextureSetup.MarsAlbedo;
            else if (body.Name == "Sun") file = TextureSetup.SunAlbedo;

            return file == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureSetup.AssetPath(file));
        }

        /// <summary>星空スカイボックス (Step 6)。Deep 段だけが描く。</summary>
        public static Material Skybox()
        {
            const string path = Folder + "/Starfield.mat";
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(TextureSetup.AssetPath(TextureSetup.StarMap));
            if (cubemap == null)
            {
                throw new System.InvalidOperationException(
                    $"星図が Cubemap として読めない: {TextureSetup.AssetPath(TextureSetup.StarMap)}");
            }

            Shader shader = Shader.Find("Skybox/Cubemap");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;
            material.SetTexture("_Tex", cubemap);
            // EXR はリニア HDR で、星の値は極端に小さい。露出 1.0 では真っ黒に見える。
            // ACES トーンマップの後でも星が残るところまで上げる。
            material.SetFloat("_Exposure", 1.0f);
            material.SetColor("_Tint", Color.white);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>発光体 (太陽) の HDR 強度。Bloom のしきい値 1.30 を確実に超える値。</summary>
        public const float EmissionIntensity = 4.0f;

        static Color ToColor(Rgb rgb) => new Color((float)rgb.R, (float)rgb.G, (float)rgb.B, 1f);

        static Material GetOrCreate(string name, string shaderName, Color color, bool transparent,
                                   bool emissive, Texture2D albedo = null)
        {
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"シェーダが見つからない: {shaderName}");
            }

            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;
            material.SetColor("_BaseColor", albedo != null ? Color.white : color);
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
            }

            if (transparent)
            {
                // クロスフェード (§3-3) のためにアルファを効かせる。
                material.SetFloat("_Surface", 1f); // 1 = Transparent
                material.SetFloat("_Blend", 0f);   // 0 = Alpha
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (emissive)
            {
                // Bloom のしきい値は 0.85〜1.30 なので、発光は HDR 域 (>1) まで上げないと光らない。
                // 4.0 なら 3 段階すべてでしきい値を超え、段階差が「にじみの広さ」として出る。
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * EmissionIntensity);
                if (albedo != null)
                {
                    material.SetTexture("_EmissionMap", albedo);
                }
            }

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }
    }
}
