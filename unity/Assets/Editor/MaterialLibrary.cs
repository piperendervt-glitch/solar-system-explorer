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
            if (body.Kind == CelestialBodyKind.Star)
            {
                // **光点も殻と同じ強度に乗せる (Step 9-1)。**
                // 航行範囲で太陽は LOD 帯 (4〜8px) を横切る
                // (地球近傍 8.70px / 火星近傍 5.71px)。殻だけ HDR にすると
                // 火星側で光点が優勢になり、太陽が航行中に暗くなっていく。
                Material star = GetOrCreate(
                    $"{body.Name}_Point",
                    SunShaderName,
                    ToColor(body.Color),
                    transparent: true);

                ApplySunProperties(star);
                return star;
            }

            // 光点は常に自己発光。距離が離れても暗くならないよう Unlit にする。
            return GetOrCreate(
                $"{body.Name}_Point",
                "Universal Render Pipeline/Unlit",
                ToColor(body.Color),
                transparent: true);
        }

        public static Material MeshMaterial(CelestialBody body)
        {
            Texture2D albedo = AlbedoFor(body);

            if (body.Kind == CelestialBodyKind.Star)
            {
                // 太陽は自ら光る。Lit にすると自分の光が当たらず真っ黒になる。
                // **手書きの SunSurface (Step 9-1)。** Unlit には強度を持たせる
                // 空きプロパティが無く、周辺減光も書けない。
                Material star = GetOrCreate(
                    $"{body.Name}_Mesh",
                    SunShaderName,
                    ToColor(body.Color),
                    transparent: true,
                    albedo: albedo);

                ApplySunProperties(star);
                return star;
            }

            // 惑星は手書きの PlanetSurface (Step 8-2)。
            Material material = GetOrCreate(
                $"{body.Name}_Mesh",
                PlanetShaderName,
                ToColor(body.Color),
                transparent: true,
                albedo: albedo);

            ApplyPlanetProperties(material, body);
            return material;
        }

        /// <summary>雲シェーダの名前 (Step 8-3)。</summary>
        public const string CloudShaderName = "SolarSystem/PlanetClouds";

        /// <summary>雲球の半径倍率。実寸 +8km では 1 画素にならないので見た目優先。</summary>
        public const float CloudRadiusScale = 1.006f;

        /// <summary>雲の不透明度のゲイン。実体は Core (Step 8-0b で移した)。</summary>
        public const float CloudOpacity = (float)PlanetAppearance.CloudOpacity;

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

        /// <summary>太陽シェーダの名前 (Step 9-1)。光点と殻の両方で使う。</summary>
        public const string SunShaderName = "SolarSystem/SunSurface";

        /// <summary>太陽の HDR 強度。実体は Core (パネルが実行時に読む)。</summary>
        public static float SunEmissionIntensity => (float)PlanetAppearance.SunEmissionIntensity;

        /// <summary>縁の明るさ。中心 1.0 に対する比 (計画書 9-1: 中心 1.0 -> 縁 0.6)。</summary>
        public const float SunLimbFloor = 0.6f;

        /// <summary>
        /// 太陽マテリアルの見た目。**光点と殻の両方に同じ値を掛ける。**
        /// 強度を _BaseColor に焼かない: CelestialBodyView が MPB で RGBA を
        /// 毎フレーム上書きするので、焼いた値は初回フレームで消える。
        /// </summary>
        static void ApplySunProperties(Material material)
        {
            material.SetFloat("_EmissionIntensity", SunEmissionIntensity);
            material.SetFloat("_LimbDarkening", 1.0f);
            material.SetFloat("_LimbFloor", SunLimbFloor);
        }

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
                    AtmosphereStrength = EarthAtmosphereStrength * (float)PlanetAppearance.MarsAtmosphereRatio,
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

        /// <summary>地球の大気の強さ。実体は Core (Step 8-0b で移した)。</summary>
        public const float EarthAtmosphereStrength = (float)PlanetAppearance.EarthAtmosphereStrength;

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
            => GetOrCreate(name, "Universal Render Pipeline/Lit", color, transparent: false);

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

        /// <summary>コロナシェーダの名前 (Step 9-2)。</summary>
        public const string CoronaShaderName = "SolarSystem/SunCorona";

        /// <summary>
        /// コロナのマテリアル (Step 9-2)。太陽のみ。
        ///
        /// **Additive なので GetOrCreate は使わない。** あちらは Transparent の
        /// アルファブレンドを組む前提で、_Surface / _SrcBlend などを書きにいく。
        /// このシェーダはブレンドをパスに固定で書いているので、
        /// マテリアル側から触られると噛み合わない。
        /// </summary>
        public static Material CoronaMaterial(CelestialBody body)
        {
            string path = $"{Folder}/{body.Name}_Corona.mat";
            Shader shader = Shader.Find(CoronaShaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"シェーダが見つからない: {CoronaShaderName}");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", CoronaTextureBuilder.GetOrCreate());

            // **本体と同じ強度。** コロナだけ取り残されると縁で段差になる。
            material.SetFloat("_EmissionIntensity", SunEmissionIntensity);

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

        static Color ToColor(Rgb rgb) => new Color((float)rgb.R, (float)rgb.G, (float)rgb.B, 1f);

        static Material GetOrCreate(string name, string shaderName, Color color, bool transparent,
                                   Texture2D albedo = null)
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
