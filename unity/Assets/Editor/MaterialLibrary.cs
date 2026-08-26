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
            if (body.Kind == CelestialBodyKind.Star)
            {
                // 太陽は自ら光る。Lit にすると自分の光が当たらず真っ黒になる。
                return GetOrCreate(
                    $"{body.Name}_Mesh",
                    "Universal Render Pipeline/Unlit",
                    ToColor(body.Color),
                    transparent: true,
                    emissive: false);
            }

            return GetOrCreate(
                $"{body.Name}_Mesh",
                "Universal Render Pipeline/Lit",
                ToColor(body.Color),
                transparent: true,
                emissive: false);
        }

        static Color ToColor(Rgb rgb) => new Color((float)rgb.R, (float)rgb.G, (float)rgb.B, 1f);

        static Material GetOrCreate(string name, string shaderName, Color color, bool transparent, bool emissive)
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
            material.SetColor("_BaseColor", color);

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
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color);
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
