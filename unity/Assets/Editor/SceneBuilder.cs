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
    /// コードなら SolarSystemModel.CreateOpposition() と書けて検算できる。
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        public const string DeepLayerName = "DeepSpace";

        public static void Build()
        {
            int deepLayer = LayerUtility.EnsureLayer(DeepLayerName);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rootGo = new GameObject("UniverseRoot");
            var universeRoot = rootGo.AddComponent<UniverseRoot>();

            var driverGo = new GameObject("OriginShiftDriver");
            driverGo.transform.SetParent(rootGo.transform, false);
            var shiftDriver = driverGo.AddComponent<OriginShiftDriver>();

            // ---- 船 (Step 2 では見た目を持たない。カメラの台座) ----
            var shipGo = new GameObject("Ship");
            shipGo.transform.SetParent(rootGo.transform, false);
            shipGo.transform.position = Vector3.zero;

            // ---- カメラ 2 段 (決定 D-6)。3 段目は作らない ----
            Camera deepCam = CreateCamera("Cam_Deep", shipGo.transform);
            Camera nearCam = CreateCamera("Cam_Near", shipGo.transform);
            nearCam.tag = "MainCamera";

            // Deep はプロキシ殻だけ、Near はそれ以外だけを描く。
            deepCam.cullingMask = 1 << deepLayer;
            nearCam.cullingMask = ~(1 << deepLayer);

            var stackGo = new GameObject("CameraStack");
            stackGo.transform.SetParent(rootGo.transform, false);
            var stack = stackGo.AddComponent<CameraStackController>();
            stack.Bind(deepCam, nearCam);
            stack.Configure();

            // ---- 太陽の Directional Light ----
            // 位置を持たないので ShiftableBody に登録しない (§3-5)。
            var lightGo = new GameObject("SunLight");
            lightGo.transform.SetParent(rootGo.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.90f);
            light.shadows = LightShadows.Soft;

            var aimerGo = new GameObject("SunLightAimer");
            aimerGo.transform.SetParent(rootGo.transform, false);
            var aimer = aimerGo.AddComponent<SunLightAimer>();
            aimer.Bind(light);

            // ---- 手動操作 (Step 3a) ----
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/Input/ShipControls.inputactions");
            if (inputAsset == null)
            {
                throw new FileNotFoundException("Assets/Input/ShipControls.inputactions が無い。");
            }

            var rigGo = new GameObject("ShipRig");
            rigGo.transform.SetParent(rootGo.transform, false);
            var rig = rigGo.AddComponent<ShipRig>();
            rig.Bind(inputAsset, shipGo.transform);

            var overlayGo = new GameObject("DebugOverlay");
            overlayGo.transform.SetParent(rootGo.transform, false);
            var overlay = overlayGo.AddComponent<DebugOverlay>();
            overlay.Bind(universeRoot, rig);

            // ---- 天体 (プロキシ殻。Deep レイヤー) ----
            // **船の子にしない。** 船は手動操作で回るので、子にすると天体が一緒に回ってしまう。
            var bodiesGo = new GameObject("Bodies");
            bodiesGo.transform.SetParent(rootGo.transform, false);
            var solarSystemView = bodiesGo.AddComponent<SolarSystemView>();

            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            foreach (CelestialBody body in model.Bodies)
            {
                solarSystemView.Register(CreateBodyView(body, bodiesGo.transform, deepLayer));
            }

            universeRoot.Configure(shiftDriver, shipGo.transform, solarSystemView, aimer, rig);

            // 登録漏れの検査 (docs/01-architecture.md §2-5)。
            shiftDriver.CollectFromScene();
            if (shiftDriver.Bodies.Count != 0)
            {
                throw new InvalidDataException(
                    $"ShiftableBody は 0 個のはず (天体はプロキシ殻で扱う): 実際 {shiftDriver.Bodies.Count}");
            }

            if (solarSystemView.Views.Count != 3)
            {
                throw new InvalidDataException(
                    $"天体は 3 個のはず: 実際 {solarSystemView.Views.Count}");
            }

            string dir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"シーンの保存に失敗した: {ScenePath}");
            }

            // PlayMode テストから SceneManager.LoadScene で開けるようにする。
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBuilder] OK: {ScenePath} / 天体 {solarSystemView.Views.Count} 個 / DeepSpace レイヤー = {deepLayer}");
        }

        static Camera CreateCamera(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<Camera>();
        }

        static CelestialBodyView CreateBodyView(CelestialBody body, Transform parent, int deepLayer)
        {
            var root = new GameObject(body.Name);
            root.transform.SetParent(parent, false);
            root.layer = deepLayer;

            // 光点: 球にする。数 px にしかならないので板と見分けがつかず、
            // 片面ポリゴンの裏表・巻き順の問題が消える (Quad では描画されなかった)。
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "Point";
            point.transform.SetParent(root.transform, false);
            point.layer = deepLayer;
            Object.DestroyImmediate(point.GetComponent<Collider>());
            point.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.PointMaterial(body);

            // メッシュ: 直径 1 の球。スケールは CelestialBodyView が毎フレーム決める。
            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
            mesh.layer = deepLayer;
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.MeshMaterial(body);

            // 実スケールメッシュ (Step 3b)。Near カメラが描くので Deep レイヤーに置かない。
            GameObject realMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            realMesh.name = "RealMesh";
            realMesh.transform.SetParent(root.transform, true);
            Object.DestroyImmediate(realMesh.GetComponent<Collider>());
            realMesh.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.MeshMaterial(body);
            realMesh.SetActive(false);

            var view = root.AddComponent<CelestialBodyView>();
            view.Bind(body, point.transform, mesh.transform, realMesh.transform);
            return view;
        }
    }
}
