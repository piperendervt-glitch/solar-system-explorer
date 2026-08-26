using System.IO;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
        public const string NearfieldLayerName = "Nearfield";

        public static void Build()
        {
            TmpSetup.RequireImported();
            TextureSetup.RequireImported();

            int deepLayer = LayerUtility.EnsureLayer(DeepLayerName);
            int cockpitLayer = LayerUtility.EnsureLayer(CockpitBuilder.LayerName);
            int nearfieldLayer = LayerUtility.EnsureLayer(NearfieldLayerName);

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

            // ---- カメラ 3 段 (Step 4 で Cockpit を追加) ----
            Camera deepCam = CreateCamera("Cam_Deep", shipGo.transform);
            Camera nearCam = CreateCamera("Cam_Near", shipGo.transform);
            nearCam.tag = "MainCamera";
            Camera nearfieldCam = CreateCamera("Cam_Nearfield", shipGo.transform);

            // ---- コックピットと計器 (Step 4) ----
            CockpitBuilder.Result cockpit = CockpitBuilder.Build(shipGo.transform, cockpitLayer);

            // **カリングマスクは排他にする。** 同じオブジェクトが 2 つの段に
            // 描かれないので二重描画が起きない。
            deepCam.cullingMask = 1 << deepLayer;
            nearCam.cullingMask = ~((1 << deepLayer) | (1 << cockpitLayer) | (1 << nearfieldLayer) | (1 << 5));
            nearfieldCam.cullingMask = 1 << nearfieldLayer;
            cockpit.CockpitCamera.cullingMask = 1 << cockpitLayer;

            // ---- 星空スカイボックス (Step 6) ----
            // **描くのは Deep 段だけ。** Base カメラだけが色をクリアし、
            // Overlay (Near / Nearfield / Cockpit) は深度しかクリアしないので、
            // スカイボックスは Deep の背景として 1 回だけ描かれる。
            // 星は無限遠なので、浮動原点のシフトでは動かない
            // (スカイボックスはカメラの回転だけを見る)。
            Material skybox = MaterialLibrary.Skybox();
            RenderSettings.skybox = skybox;
            deepCam.clearFlags = CameraClearFlags.Skybox;

            var stackGo = new GameObject("CameraStack");
            stackGo.transform.SetParent(rootGo.transform, false);
            var stack = stackGo.AddComponent<CameraStackController>();
            stack.Bind(deepCam, nearCam, nearfieldCam, cockpit.CockpitCamera);
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

            // ---- ステーション (Step 5) ----
            var stationsGo = new GameObject("Stations");
            stationsGo.transform.SetParent(rootGo.transform, false);
            var stationSet = stationsGo.AddComponent<StationViewSet>();

            foreach (SpaceStation station in model.Stations)
            {
                stationSet.Register(CreateStationView(station, stationsGo.transform, nearfieldLayer));
            }

            // ---- ポストプロセス (Step 6) ----
            // 3 段階を比較のうえ Step 7 で Medium に確定 (人間が選択)。
            var volumeGo = new GameObject("PostProcess");
            volumeGo.transform.SetParent(rootGo.transform, false);
            var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            // profile ではなく sharedProfile。profile は実行時コピーを作るプロパティで、
            // シリアライズされないためシーン再読み込み後に空のプロファイルになる。
            volume.sharedProfile = PostProcessProfileBuilder.GetOrCreate();

            var preset = volumeGo.AddComponent<PostProcessPreset>();
            preset.Bind(volume);
            preset.Apply(PostProcessStrength.Medium);

            foreach (Camera cam in new[] { deepCam, nearCam, nearfieldCam, cockpit.CockpitCamera })
            {
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }

            // ---- 太陽のフレア (Step 6) ----
            var flare = light.gameObject.AddComponent<UnityEngine.Rendering.LensFlareComponentSRP>();
            flare.lensFlareData = LensFlareBuilder.GetOrCreate();
            flare.intensity = 0.6f;
            flare.scale = 1.0f;
            flare.attenuationByLightShape = false;

            // ---- exe からのスクショ用 (Step 7) ----
            // 引数が無ければ何もしない。見た目には影響しない。
            rootGo.AddComponent<StandaloneCapture>();

            // ---- エンジン音 (Step 6) ----
            var audioGo = new GameObject("EngineAudio");
            audioGo.transform.SetParent(shipGo.transform, false);
            var audioSource = audioGo.AddComponent<AudioSource>();
            audioSource.clip = EngineAudioClipBuilder.GetOrCreate();
            audioSource.loop = true;
            audioSource.playOnAwake = true;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.06f;
            var lowPass = audioGo.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 220f;
            var engineAudio = audioGo.AddComponent<EngineAudio>();
            engineAudio.Bind(audioSource, lowPass);

            // リスナーはコックピット (視点) に置く。
            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                nearCam.gameObject.AddComponent<AudioListener>();
            }

            universeRoot.Configure(shiftDriver, shipGo.transform, solarSystemView, aimer, rig,
                                   cockpit.Panel, stationSet, preset, engineAudio);

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

            if (stationSet.Views.Count != model.Stations.Count)
            {
                throw new InvalidDataException(
                    $"ステーションは {model.Stations.Count} 基のはず: 実際 {stationSet.Views.Count}");
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

            Debug.Log($"[SceneBuilder] OK: {ScenePath} / 天体 {solarSystemView.Views.Count} 個 / " +
                      $"ステーション {stationSet.Views.Count} 基 / " +
                      $"レイヤー DeepSpace={deepLayer} Cockpit={cockpitLayer} Nearfield={nearfieldLayer} / カメラ 4 段 / " +
                      $"スカイボックス={skybox.name} (Deep 段のみ) / ポスト={preset.Strength}");
        }

        /// <summary>
        /// ステーション 1 基。外部アセットは使わず (決定 D-22) プリミティブで組む。
        /// リング状の外形＋ポートの目印。実寸 0.5 units (500 m)。
        /// </summary>
        static StationView CreateStationView(SpaceStation station, Transform parent, int layer)
        {
            var root = new GameObject(station.Name);
            root.transform.SetParent(parent, false);
            root.layer = layer;

            float r = (float)station.RadiusKm;
            Material hull = MaterialLibrary.SolidMaterial("StationHull", new Color(0.62f, 0.64f, 0.68f));
            Material port = MaterialLibrary.SolidMaterial("StationPort", new Color(0.20f, 0.70f, 0.45f));

            // 中央のコア
            AddPart(root.transform, "Core", PrimitiveType.Cylinder, Vector3.zero,
                Quaternion.Euler(90f, 0f, 0f), new Vector3(r * 0.45f, r * 0.5f, r * 0.45f), hull, layer);

            // 四方に張り出したアーム
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var rot = Quaternion.Euler(0f, 0f, a);
                Vector3 dir = rot * Vector3.up;
                AddPart(root.transform, $"Arm{i}", PrimitiveType.Cube, dir * (r * 0.6f),
                    rot, new Vector3(r * 0.16f, r * 1.2f, r * 0.16f), hull, layer);
                AddPart(root.transform, $"Pod{i}", PrimitiveType.Cube, dir * (r * 1.05f),
                    rot, new Vector3(r * 0.4f, r * 0.35f, r * 0.4f), hull, layer);
            }

            // ポート (深宇宙側 = ローカル +Z)。船はここへ着く。
            AddPart(root.transform, "Port", PrimitiveType.Cylinder,
                new Vector3(0f, 0f, r * 0.55f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(r * 0.3f, r * 0.12f, r * 0.3f), port, layer);

            var view = root.AddComponent<StationView>();
            view.Bind(station);
            return view;
        }

        static void AddPart(Transform parent, string name, PrimitiveType type, Vector3 position,
                            Quaternion rotation, Vector3 scale, Material material, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
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
