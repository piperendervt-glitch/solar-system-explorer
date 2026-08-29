using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **判定リグをシーンに組む (Step 13-3b)。**
    ///
    /// ■ 既定は非アクティブ
    /// `-stationJudge` が無ければ `SetActive(false)` のまま。**平面の 36 枚は動かない。**
    /// 実行時に有効化するのは `StationJudgeRig.Requested()` を見る `DebugPanel` 側。
    ///
    /// ■ モデルが無いクローンでも通る
    /// プレハブの GUID が解決できなければリグを組まない（`HasModel` が false になる）。
    /// 箱ステーションのフォールバックと同じ考え方 (§0-A)。
    ///
    /// ■ マテリアル
    /// **`new Material()` を書かない。** `MaterialLibrary.SolidMaterial` が
    /// プロジェクトのアセットを作る／使い回す。発光は実行時に載せる。
    /// </summary>
    public static class StationJudgeBuilder
    {
        /// <summary>ステーションのプレハブ GUID（13-2 の実測）。</summary>
        public const string StationPrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        public const string RootName = "StationJudge";

        /// <summary>輪の分割数。</summary>
        const int RingSegments = 96;

        public static StationJudgeRig Build(Transform parent, Camera nearfieldCamera, int layer)
        {
            var rootGo = new GameObject(RootName);
            rootGo.transform.SetParent(parent, false);

            StationJudgeRig rig = rootGo.AddComponent<StationJudgeRig>();
            rig.Bind(nearfieldCamera);

            GameObject model = InstantiateModel(rootGo.transform, layer);
            if (model == null)
            {
                Debug.LogWarning("[StationJudge] モデルが解決できない（取り込まれていない）。"
                                 + "リグは空のまま組む: GUID " + StationPrefabGuid);
            }

            Transform shipFrame = BuildFrame(rootGo.transform, layer);
            Transform grid = BuildGrid(rootGo.transform, layer);
            Transform ringGold = BuildRing(rootGo.transform, layer, "Ring_GoldDisc",
                                           new Color(1.00f, 0.85f, 0.25f));
            Transform ringPlate = BuildRing(rootGo.transform, layer, "Ring_ProtrudingPlate",
                                            new Color(0.30f, 0.85f, 1.00f));
            Transform ringBody = BuildRing(rootGo.transform, layer, "Ring_ModuleBody",
                                           new Color(1.00f, 0.35f, 0.85f));

            var so = new SerializedObject(rig);
            so.FindProperty("_model").objectReferenceValue = model != null ? model.transform : null;
            so.FindProperty("_camera").objectReferenceValue = nearfieldCamera;
            so.FindProperty("_shipFrame").objectReferenceValue = shipFrame;
            so.FindProperty("_grid").objectReferenceValue = grid;
            so.FindProperty("_ringGold").objectReferenceValue = ringGold;
            so.FindProperty("_ringPlate").objectReferenceValue = ringPlate;
            so.FindProperty("_ringBody").objectReferenceValue = ringBody;
            so.ApplyModifiedPropertiesWithoutUndo();

            // **既定は OFF。** 起動引数が無ければ絵に一切出ない。
            rootGo.SetActive(false);

            Debug.Log("[StationJudge] 組んだ: モデル "
                      + (model != null ? "あり" : "**無し**")
                      + " / 目印 5 件 / 既定は非アクティブ");

            return rig;
        }

        static GameObject InstantiateModel(Transform parent, int layer)
        {
            string path = AssetDatabase.GUIDToAssetPath(StationPrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = "Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            SetLayerRecursively(instance, layer);

            // **当たり判定は要らない。** 判定ビューは見るだけ。
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(c);
            }

            return instance;
        }

        static Transform BuildFrame(Transform parent, int layer)
        {
            Mesh mesh = StationJudgeRig.BuildFrame(
                (float)StationJudge.ShipWidthMeters,
                (float)StationJudge.ShipHeightMeters,
                StationJudgeRig.DefaultLineWidth);

            return BuildMarker(parent, layer, "Marker_ShipFrame", mesh,
                               new Color(0.30f, 1.00f, 0.45f));
        }

        static Transform BuildGrid(Transform parent, int layer)
        {
            Mesh mesh = StationJudgeRig.BuildGrid(
                StationJudgeRig.DefaultGridHalfExtent, StationJudgeRig.DefaultLineWidth * 0.5f);

            return BuildMarker(parent, layer, "Marker_Grid", mesh,
                               new Color(0.55f, 0.60f, 0.70f));
        }

        static Transform BuildRing(Transform parent, int layer, string name, Color color)
            => BuildMarker(parent, layer, name, StationJudgeRig.BuildRing(RingSegments), color);

        static Transform BuildMarker(Transform parent, int layer, string name,
                                     Mesh mesh, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;

            // メッシュはプロシージャル。シーンに焼くのでアセットとして保存する。
            AssetDatabase.CreateAsset(mesh, MeshPath(name));

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialLibrary.SolidMaterial("Judge_" + name, color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go.transform;
        }

        const string MeshFolder = "Assets/Materials/JudgeMeshes";

        static string MeshPath(string name)
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "JudgeMeshes");
            }

            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            return path;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
