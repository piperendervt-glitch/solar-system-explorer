using SolarSystem.Unity;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// コックピットと計器をコードから組む (Step 4)。
    ///
    /// 外部アセットは使わない (決定 D-22)。プリミティブで枠を作るだけ。
    /// 実寸は 2 m 前後 = 0.002 units。Cockpit カメラ (near 1e-4 / far 0.1) が描く。
    ///
    /// 船の子にするので、姿勢操作でコックピットごと回る。
    /// </summary>
    public static class CockpitBuilder
    {
        public const string LayerName = "Cockpit";

        /// <summary>
        /// コックピットの基準寸法。実寸 2 m = 0.002 units に
        /// CockpitRenderScale (1000) を掛けた値。
        /// Unity が near clip を 0.01 でクランプするので、実寸のままでは描けない
        /// (CameraStackController の注記を参照)。見かけの角度は実寸と同じ。
        /// </summary>
        const float Size = 0.002f * SolarSystem.Unity.CameraStackController.CockpitRenderScale;

        /// <summary>計器 RenderTexture の解像度 (docs/01-architecture.md §5-1)。</summary>
        const int PanelWidth = 512;
        const int PanelHeight = 256;

        const string RenderTexturePath = "Assets/Materials/InstrumentPanel.renderTexture";
        const string PanelMaterialPath = "Assets/Materials/InstrumentPanel.mat";

        public struct Result
        {
            public Camera CockpitCamera;
            public InstrumentPanel Panel;
        }

        public static Result Build(Transform shipTransform, int cockpitLayer)
        {
            var root = new GameObject("Cockpit");
            root.transform.SetParent(shipTransform, false);
            SetLayerRecursive(root, cockpitLayer);

            // ---- カメラ (視点は固定。船の姿勢に従う) ----
            var camGo = new GameObject("Cam_Cockpit");
            camGo.transform.SetParent(shipTransform, false);
            Camera cockpitCam = camGo.AddComponent<Camera>();

            // ---- 枠 ----
            // 前方に開口部、上下左右に厚みのある枠。プリミティブの箱だけで作る。
            Material frame = MaterialLibrary.SolidMaterial("CockpitFrame", new Color(0.16f, 0.17f, 0.19f));
            const float halfW = Size * 0.9f;
            const float halfH = Size * 0.5f;
            const float depth = Size * 1.2f;
            const float bar = Size * 0.12f;

            AddBox(root.transform, "Frame_Top", new Vector3(0f, halfH, depth * 0.5f),
                new Vector3(halfW * 2f, bar, depth), frame, cockpitLayer);
            AddBox(root.transform, "Frame_Bottom", new Vector3(0f, -halfH, depth * 0.5f),
                new Vector3(halfW * 2f, bar, depth), frame, cockpitLayer);
            AddBox(root.transform, "Frame_Left", new Vector3(-halfW, 0f, depth * 0.5f),
                new Vector3(bar, halfH * 2f, depth), frame, cockpitLayer);
            AddBox(root.transform, "Frame_Right", new Vector3(halfW, 0f, depth * 0.5f),
                new Vector3(bar, halfH * 2f, depth), frame, cockpitLayer);

            // 背面と床。振り返っても宇宙が見えないようにする。
            AddBox(root.transform, "Hull_Back", new Vector3(0f, 0f, -Size * 0.6f),
                new Vector3(halfW * 2f, halfH * 2f, bar), frame, cockpitLayer);
            AddBox(root.transform, "Hull_Floor", new Vector3(0f, -halfH * 0.55f, 0f),
                new Vector3(halfW * 1.6f, bar * 0.5f, Size), frame, cockpitLayer);

            // ---- 計器パネル ----
            RenderTexture rt = GetOrCreateRenderTexture();
            Material panelMaterial = GetOrCreatePanelMaterial(rt);

            GameObject panelQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelQuad.name = "InstrumentSurface";
            panelQuad.transform.SetParent(root.transform, false);
            panelQuad.layer = cockpitLayer;
            Object.DestroyImmediate(panelQuad.GetComponent<Collider>());
            // 視線のやや下、手前に傾けて置く。
            // 4 項目が全部入るよう、視界の下寄りに小さめに置く。
            // RT は 512x256 なのでアスペクトは 2:1 を保つ。
            panelQuad.transform.localPosition = new Vector3(0f, -halfH * 0.52f, Size * 0.95f);
            panelQuad.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            panelQuad.transform.localScale = new Vector3(Size * 0.80f, Size * 0.40f, 1f);
            panelQuad.GetComponent<Renderer>().sharedMaterial = panelMaterial;

            InstrumentPanel panel = BuildInstrumentSource(root.transform, rt);

            return new Result { CockpitCamera = cockpitCam, Panel = panel };
        }

        /// <summary>
        /// RenderTexture へ描く側。Canvas + TextMeshPro を専用の
        /// Orthographic カメラで撮る。カメラスタックには入れない。
        /// コックピットから遠く離れた場所に置いて、他のカメラに写らないようにする。
        /// </summary>
        static InstrumentPanel BuildInstrumentSource(Transform parent, RenderTexture rt)
        {
            var sourceRoot = new GameObject("InstrumentSource");
            sourceRoot.transform.SetParent(parent, false);
            // 計器用カメラの Culling Mask を UI だけにするので、位置は視界の外へ。
            sourceRoot.transform.localPosition = new Vector3(0f, 1.0e5f, 0f);

            var camGo = new GameObject("Cam_Instrument");
            camGo.transform.SetParent(sourceRoot.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 1f);
            cam.cullingMask = 1 << 5; // UI レイヤーのみ
            cam.targetTexture = rt;
            camGo.transform.localPosition = new Vector3(0f, 0f, -2f);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(sourceRoot.transform, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 4f;

            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            TMP_Text speed = AddLabel(canvasGo.transform, "Speed", new Vector2(0f, 0.34f), 46f, TextAlignmentOptions.Left);
            TMP_Text distance = AddLabel(canvasGo.transform, "Distance", new Vector2(0f, 0.09f), 40f, TextAlignmentOptions.Left);
            TMP_Text eta = AddLabel(canvasGo.transform, "Eta", new Vector2(0f, -0.16f), 40f, TextAlignmentOptions.Left);
            TMP_Text target = AddLabel(canvasGo.transform, "Target", new Vector2(0f, -0.40f), 34f, TextAlignmentOptions.Left);

            AddCaption(canvasGo.transform, "SpeedCaption", "SPD", new Vector2(-0.40f, 0.34f));
            AddCaption(canvasGo.transform, "DistanceCaption", "DST", new Vector2(-0.40f, 0.09f));
            AddCaption(canvasGo.transform, "EtaCaption", "ETA", new Vector2(-0.40f, -0.16f));
            AddCaption(canvasGo.transform, "TargetCaption", "TGT", new Vector2(-0.40f, -0.40f));

            var panel = sourceRoot.AddComponent<InstrumentPanel>();
            panel.Bind(speed, distance, eta, target);
            return panel;
        }

        static TMP_Text AddLabel(Transform parent, string name, Vector2 anchor, float fontSize,
                                 TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.55f, 0.95f, 0.75f, 1f);
            text.text = "---";

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.30f, 0.5f + anchor.y - 0.1f);
            rt.anchorMax = new Vector2(0.98f, 0.5f + anchor.y + 0.1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return text;
        }

        static void AddCaption(Transform parent, string name, string caption, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = 30f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.35f, 0.55f, 0.50f, 1f);
            text.text = caption;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.03f, 0.5f + anchor.y - 0.1f);
            rt.anchorMax = new Vector2(0.30f, 0.5f + anchor.y + 0.1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static RenderTexture GetOrCreateRenderTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (existing != null)
            {
                return existing;
            }

            var rt = new RenderTexture(PanelWidth, PanelHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "InstrumentPanel",
                antiAliasing = 1,
            };
            AssetDatabase.CreateAsset(rt, RenderTexturePath);
            return rt;
        }

        static Material GetOrCreatePanelMaterial(RenderTexture rt)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(PanelMaterialPath);
            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", rt);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, PanelMaterialPath);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale,
                           Material material, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
