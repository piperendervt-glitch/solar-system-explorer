using System.Collections.Generic;
using System.Text;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **発光しているメッシュが、座席から実際に見えるか (Step 11-4a の調査)。**
    ///
    /// 棚卸し (`CockpitInventory`) は「発光しているマテリアルはどれか」までしか
    /// 分からない。補助光の対象に入れるかどうかは**目から見えるか**で決まるので、
    /// 目のローカル座標と、基準 1920x1080 での投影サイズを出す。
    ///
    /// **数えるだけで、何も書き換えない。**
    /// </summary>
    public static class CockpitVisibilityProbe
    {
        /// <summary>投影を測る条件。`CockpitMetrics` と同じ（解像度に依存させない）。</summary>
        const int ReferenceWidth = 1920;
        const int ReferenceHeight = 1080;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

            var stack = Object.FindAnyObjectByType<CameraStackController>();
            Camera eye = stack != null ? stack.Cockpit : null;
            if (eye == null)
            {
                Debug.LogWarning("[VisibilityProbe] コックピットのカメラが無い");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("マテリアル\tレンダラー\t目からの位置 (右, 上, 前)\t"
                              + "投影 (1920x1080)\t判定");

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // **サブメッシュも見る。** 1 つのレンダラーが複数のマテリアルを
                // 持つことがあり、`sharedMaterial` だけでは取りこぼす。
                foreach (Material material in r.sharedMaterials)
                {
                    if (material == null || !IsEmissive(material))
                    {
                        continue;
                    }

                    Vector3 center = eye.transform.InverseTransformPoint(r.bounds.center);
                    Vector2Int projected = Project(r, eye, out bool anyInFront);

                    string verdict = !anyInFront
                        ? "**目の後ろ**"
                        : projected.x <= 0 || projected.y <= 0
                            ? "画角の外"
                            : $"見える ({projected.x}x{projected.y} px)";

                    report.AppendLine($"{material.name}\t{r.name}\t"
                                      + $"({center.x:F2}, {center.y:F2}, {center.z:F2}) m\t"
                                      + $"{projected.x}x{projected.y}\t{verdict}");
                }
            }

            Debug.Log("[VisibilityProbe] 発光しているメッシュ\n" + report);
        }

        static bool IsEmissive(Material material)
        {
            if (!material.IsKeywordEnabled("_EMISSION"))
            {
                return false;
            }

            bool hasMap = material.HasProperty("_EmissionMap")
                          && material.GetTexture("_EmissionMap") != null;
            bool hasColor = material.HasProperty("_EmissionColor")
                            && material.GetColor("_EmissionColor").maxColorComponent > 0.001f;
            return hasMap || hasColor;
        }

        /// <summary>
        /// 目から見た投影サイズ [px]。**基準 1920x1080 / 実際の画角**で測る。
        /// 隅が目の後ろにあるものは除外する（投影が破綻するので）。
        /// </summary>
        static Vector2Int Project(Renderer renderer, Camera eye, out bool anyInFront)
        {
            Bounds b = renderer.bounds;
            var corners = new List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                corners.Add(b.center + Vector3.Scale(
                    b.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f,
                                (i & 2) == 0 ? -1f : 1f,
                                (i & 4) == 0 ? -1f : 1f)));
            }

            float halfHeight = Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pixelsPerUnit = ReferenceHeight * 0.5f / halfHeight;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            anyInFront = false;

            foreach (Vector3 world in corners)
            {
                Vector3 view = eye.transform.InverseTransformPoint(world);
                if (view.z <= 1e-4f)
                {
                    continue;
                }

                anyInFront = true;
                float x = (view.x / view.z) * pixelsPerUnit;
                float y = (view.y / view.z) * pixelsPerUnit;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }

            if (!anyInFront)
            {
                return Vector2Int.zero;
            }

            // 画角の外にはみ出したぶんは切る。
            float halfW = ReferenceWidth * 0.5f;
            float halfH = ReferenceHeight * 0.5f;
            minX = Mathf.Max(minX, -halfW); maxX = Mathf.Min(maxX, halfW);
            minY = Mathf.Max(minY, -halfH); maxY = Mathf.Min(maxY, halfH);

            return new Vector2Int(Mathf.Max(0, Mathf.RoundToInt(maxX - minX)),
                                  Mathf.Max(0, Mathf.RoundToInt(maxY - minY)));
        }
    }
}
