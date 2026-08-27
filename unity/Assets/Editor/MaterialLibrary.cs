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

            // 惑星は手書きの PlanetSurface (Step 8-2)。
            Material material = GetOrCreate(
                $"{body.Name}_Mesh",
                PlanetShaderName,
                ToColor(body.Color),
                transparent: true,
                emissive: false,
                albedo: albedo);

            ApplyPlanetProperties(material, body);
            return material;
        }

        /// <summary>雲シェーダの名前 (Step 8-3)。</summary>
        public const string CloudShaderName = "SolarSystem/PlanetClouds";

        /// <summary>雲球の半径倍率。実寸 +8km では 1 画素にならないので見た目優先。</summary>
        public const float CloudRadiusScale = 1.006f;

        /// <summary>
        /// 雲の不透明度のゲイン。**1.15 で確定** (earth-close-day を目視して決定)。
        ///
        /// 素材 (earth_clouds) の最大値は 227/255 = 0.89 しかない。
        /// これを 1.0 まで持ち上げる係数は 255/227 = 1.123 で、1.15 はその少し上。
        ///
        /// **EditMode テストは下限 1.12 しか縛っていない。**
        /// 「素材の最大 0.89 のままでは薄い」ことしか自動では言えないため。
        /// 濃さそのものは目視で決めた値なので、変えるならまた目で見ること。
        /// </summary>
        public const float CloudOpacity = 1.15f;

        /// <summary>
        /// 雲球のマテリアル (Step 8-3)。地球のみ。
        /// **renderQueue は地表 +1。** 同心球なので距離ソートが同値になり順序が不定になる。
        /// </summary>
        public static Material CloudMaterial(CelestialBody body)
        {
            Shader shader = Shader.Find(CloudShaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException("シェーダが見つからない: " + CloudShaderName);
            }

            string path = $"{Folder}/{body.Name}_Clouds.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    PlanetTextureSetup.AssetPath("4k_earth_clouds.jpg")));
            material.SetFloat("_CloudOpacity", CloudOpacity);

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = SurfaceRenderQueue + 1;

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

        /// <summary>地表の描画キュー。雲はこれの +1。</summary>
        public const int SurfaceRenderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        /// <summary>惑星シェーダの名前 (Step 8-2)。</summary>
        public const string PlanetShaderName = "SolarSystem/PlanetSurface";

        /// <summary>大気の縁の仕様 (docs/02-demo2-plan.md 8-2)。</summary>
        public struct PlanetLook
        {
            public Color AtmosphereColor;
            public float AtmospherePower;
            public float AtmosphereStrength;
            public bool UsesEarthMaps;
        }

        /// <summary>天体ごとの見た目。テストはここを仕様表として読む。</summary>
        public static PlanetLook LookFor(CelestialBody body)
        {
            if (body != null && body.Name == "Mars")
            {
                return new PlanetLook
                {
                    AtmosphereColor = new Color(1.0f, 0.6f, 0.35f, 1f),
                    AtmospherePower = 5.0f,
                    AtmosphereStrength = EarthAtmosphereStrength * 0.25f, // 地球の 1/4 (薄い大気)
                    UsesEarthMaps = false,
                };
            }

            return new PlanetLook
            {
                AtmosphereColor = new Color(0.35f, 0.55f, 1.0f, 1f),
                AtmospherePower = 3.5f,
                AtmosphereStrength = EarthAtmosphereStrength,
                UsesEarthMaps = true,
            };
        }

        /// <summary>
        /// 地球の大気の強さ。**5.0 で確定** (earth-close-day を目視して決定)。
        ///
        /// **画素テストは相対成分 B/(R+G+B) で判定している。**
        /// 絶対値の B で比べると、中心が明るい海のとき大気の強さと無関係に
        /// 中心が勝ちうるため。5.0 での実測: 縁 0.5145 / 中心 0.4975 (差 +0.0170)。
        ///
        /// **注意: 以前この欄に書いていた「縁 B=132.0 / 中心 B=117.2 (差 +14.8)」は
        /// 誤った走査範囲で得た値だった。** PlayMode テストの画素走査が
        /// `y < Height - PanelTop` となっており、計器パネルを除くつもりで
        /// 画面下 1/3 だけを見ていた (GetPixels32 は下から上に並ぶ)。
        /// 修正後の絶対値は 縁 132.4 / 中心 130.6 で、差は +1.8 しかない。
        /// 値そのものの妥当性は目視で決めた。
        /// </summary>
        public const float EarthAtmosphereStrength = 5.0f;

        public const float SmoothnessLand = 0.1f;
        public const float SmoothnessOcean = 0.85f;
        public const float NightIntensity = 1.0f;

        static void ApplyPlanetProperties(Material material, CelestialBody body)
        {
            PlanetLook look = LookFor(body);

            material.SetColor("_AtmosphereColor", look.AtmosphereColor);
            material.SetFloat("_AtmospherePower", look.AtmospherePower);
            material.SetFloat("_AtmosphereStrength", look.AtmosphereStrength);
            material.SetFloat("_SmoothnessLand", SmoothnessLand);
            material.SetFloat("_SmoothnessOcean", SmoothnessOcean);
            material.SetFloat("_NightIntensity", NightIntensity);
            material.SetFloat("_EmissionIntensity", 1.0f);

            // **地球以外は法線 / 鏡面 / 街灯りを割り当てない。**
            // シェーダ側の既定 (bump / black / black) に解決される。
            if (!look.UsesEarthMaps)
            {
                return;
            }

            material.SetTexture("_NormalMap", LoadPlanetMap("4k_earth_normal.png"));
            material.SetTexture("_SpecularMask", LoadPlanetMap("4k_earth_specular.png"));
            material.SetTexture("_NightMap", LoadPlanetMap("4k_earth_nightmap.jpg"));
        }

        static Texture2D LoadPlanetMap(string fileName)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                PlanetTextureSetup.AssetPath(fileName));
            if (texture == null)
            {
                PlanetTextureSetup.RequireImported();
            }

            return texture;
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
