using System.IO;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SolarSystem.Editor
{
    /// <summary>
    /// Main.unity を毎回まっさらに生成する (決定 D-20)。
    ///
    /// GUI で手置きしない (docs/01-architecture.md §7-2)。
    /// 座標が 1e8 のオーダーなので Inspector への手入力は桁を間違える。
    /// コードなら UniverseConstants.AstronomicalUnitKm と書けて検算できる。
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>Step 1 の目視用参照マーカーの距離 [units = km]。</summary>
        const double MarkerDistance = 50.0;

        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- UniverseRoot ----
            var rootGo = new GameObject("UniverseRoot");
            var universeRoot = rootGo.AddComponent<UniverseRoot>();

            var driverGo = new GameObject("OriginShiftDriver");
            driverGo.transform.SetParent(rootGo.transform, false);
            var shiftDriver = driverGo.AddComponent<OriginShiftDriver>();

            // ---- 船 (Step 1 では見た目を持たない。カメラの台座) ----
            var shipGo = new GameObject("Ship");
            shipGo.transform.SetParent(rootGo.transform, false);
            shipGo.transform.position = Vector3.zero;

            var cameraGo = new GameObject("Cam_Near");
            cameraGo.transform.SetParent(shipGo.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            // near/far とカメラ段の構成は Step 2 の作業 (決定 D-6: 2 段で開始)。
            // ここでは既定値のまま 1 台だけ置く。

            // ---- 参照マーカー ----
            // 天体ではない。浮動原点が効いていることを目で見るための素の立方体。
            // 天体・テクスチャ・光点/メッシュ切替は Step 2 の作業。
            CreateMarker("ReferenceMarker_X", new Vec3d(MarkerDistance, 0.0, 0.0), rootGo.transform);
            CreateMarker("ReferenceMarker_Y", new Vec3d(0.0, MarkerDistance, 0.0), rootGo.transform);
            CreateMarker("ReferenceMarker_Z", new Vec3d(0.0, 0.0, MarkerDistance), rootGo.transform);

            universeRoot.Configure(shiftDriver, shipGo.transform);

            // 登録漏れの検査 (docs/01-architecture.md §2-5)。
            // 実行時は Awake でも集め直すが、生成時点で数が合わないなら生成側のバグ。
            shiftDriver.CollectFromScene();
            const int expectedShiftableCount = 3; // 参照マーカー 3 個
            if (shiftDriver.Bodies.Count != expectedShiftableCount)
            {
                throw new InvalidDataException(
                    $"ShiftableBody の数が合わない: 期待 {expectedShiftableCount} / 実際 {shiftDriver.Bodies.Count}");
            }

            // ---- 保存 ----
            string dir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved)
            {
                throw new IOException($"シーンの保存に失敗した: {ScenePath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBuilder] OK: {ScenePath} / ShiftableBody {shiftDriver.Bodies.Count} 個");
        }

        static void CreateMarker(string name, Vec3d absolutePosition, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            // 1 unit = 1 km なので、既定の 1 unit 立方体は一辺 1 km。10 m に縮める。
            go.transform.localScale = Vector3.one * 0.01f;

            var body = go.AddComponent<ShiftableBody>();
            body.AbsolutePosition = absolutePosition;
        }
    }
}
