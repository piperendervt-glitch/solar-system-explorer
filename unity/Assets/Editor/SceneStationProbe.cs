using System.Linq;
using System.Text;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **シーンに置かれたステーションの実体を数える (Step 13-3b の切り分け)。**
    /// 「置いたつもり」と「そこに在って描かれる」は別（§0-A の一般則）。
    /// </summary>
    public static class SceneStationProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);

            var sb = new StringBuilder();
            var identity = Object.FindAnyObjectByType<StationIdentity>();
            sb.AppendLine("[SceneStation] identity = "
                          + (identity != null ? identity.Describe() : "**無し**"));

            foreach (StationView view in Object.FindObjectsByType<StationView>(
                         FindObjectsSortMode.None))
            {
                Renderer[] rs = view.GetComponentsInChildren<Renderer>(true);
                sb.AppendLine("[SceneStation] " + view.name
                              + " / localScale " + view.transform.localScale
                              + " / 子レンダラー " + rs.Length
                              + " / enabled " + rs.Count(r => r.enabled)
                              + " / レイヤー " + string.Join(",",
                                  rs.Select(r => r.gameObject.layer).Distinct()
                                    .OrderBy(l => l).Select(l => l.ToString()).ToArray()));

                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) { b.Encapsulate(r.bounds); }
                    sb.AppendLine("[SceneStation]   ワールド bbox 中心 " + b.center
                                  + " / サイズ " + b.size);
                }
            }

            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                sb.AppendLine("[SceneStation] camera " + cam.name
                              + " / enabled " + cam.enabled
                              + " / mask " + cam.cullingMask
                              + " / layer10 " + ((cam.cullingMask & (1 << 10)) != 0)
                              + " / near " + cam.nearClipPlane
                              + " / far " + cam.farClipPlane
                              + " / depth " + cam.depth);
            }

            Debug.Log(sb.ToString());
        }
    }
}
