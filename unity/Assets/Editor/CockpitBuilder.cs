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

        /// <summary>
        /// 計器 RenderTexture の解像度 (docs/01-architecture.md §5-1)。
        /// Step 7 で 512x320 の縦積み 5 行から 768x160 の横長 2 行へ変更した。
        /// 要件 §1「眺めの美しさを優先」に対して、5 行のパネルが視界中央を
        /// 塞いでいたため。アスペクトは 4.8:1。
        /// </summary>
        const int PanelWidth = 768;
        const int PanelHeight = 160;

        /// <summary>パネル背景の不透明度 (Step 7)。文字は不透明のまま。</summary>
        const float PanelBackgroundAlpha = 0.55f;

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
            // 画面の下端へ寄せる (Step 7)。高さは従来の半分 (0.40 -> 0.20)。
            // 中心は視線から約 20.9 度下、上下に約 5.4 度。垂直 FOV 60 度の
            // 下半分 (30 度) の内側に収まり、上 3/4 は完全に空く。
            // RT が 768x160 なのでアスペクトは 4.8 を保つ。
            // 下枠 (Frame_Bottom) の上面は y = -halfH + bar/2 = -0.44*Size。
            // パネルの下端 (中心 - 0.10*Size*cos18 = 0.095*Size) がそれより上に来る位置。
            panelQuad.transform.localPosition = new Vector3(0f, -halfH * 0.62f, Size * 1.05f);
            panelQuad.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            panelQuad.transform.localScale = new Vector3(Size * 0.96f, Size * 0.20f, 1f);
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
            // 背景だけ半透明にする。文字は不透明のまま残るので可読性は落ちない。
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f, PanelBackgroundAlpha);
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

            // 5 項目を 2 行に詰める (Step 7)。
            //   1 行目: SPD / DST / ETA
            //   2 行目: TGT / ALN
            const float row1 = 0.25f;
            const float row2 = -0.25f;

            TMP_Text speed = AddLabel(canvasGo.transform, "Speed", 0.10f, 0.33f, row1, 34f);
            TMP_Text distance = AddLabel(canvasGo.transform, "Distance", 0.43f, 0.66f, row1, 34f);
            TMP_Text eta = AddLabel(canvasGo.transform, "Eta", 0.76f, 0.99f, row1, 34f);
            TMP_Text target = AddLabel(canvasGo.transform, "Target", 0.10f, 0.49f, row2, 34f);
            TMP_Text alignment = AddLabel(canvasGo.transform, "Alignment", 0.59f, 0.99f, row2, 34f);

            AddCaption(canvasGo.transform, "SpeedCaption", "SPD", 0.01f, 0.10f, row1);
            AddCaption(canvasGo.transform, "DistanceCaption", "DST", 0.34f, 0.43f, row1);
            AddCaption(canvasGo.transform, "EtaCaption", "ETA", 0.67f, 0.76f, row1);
            AddCaption(canvasGo.transform, "TargetCaption", "TGT", 0.01f, 0.10f, row2);
            AddCaption(canvasGo.transform, "AlignCaption", "ALN", 0.50f, 0.59f, row2);

            var panel = sourceRoot.AddComponent<InstrumentPanel>();
            panel.Bind(speed, distance, eta, target, alignment);
            return panel;
        }

        /// <summary>行の高さ (正規化)。2 行なので 1 行あたり半分弱を使う。</summary>
        const float RowHalfHeight = 0.22f;

        static TMP_Text AddLabel(Transform parent, string name, float xMin, float xMax,
                                 float yCenter, float fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.55f, 0.95f, 0.75f, 1f);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = "---";

            SetAnchors(go, xMin, xMax, yCenter);
            return text;
        }

        static void AddCaption(Transform parent, string name, string caption,
                               float xMin, float xMax, float yCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = 26f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.35f, 0.55f, 0.50f, 1f);
            text.enableWordWrapping = false;
            text.text = caption;

            SetAnchors(go, xMin, xMax, yCenter);
        }

        static void SetAnchors(GameObject go, float xMin, float xMax, float yCenter)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0.5f + yCenter - RowHalfHeight);
            rt.anchorMax = new Vector2(xMax, 0.5f + yCenter + RowHalfHeight);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static RenderTexture GetOrCreateRenderTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (existing != null)
            {
                // 解像度を変えたら作り直す。既存の .asset をそのまま返すと
                // 古い 512x320 のままアスペクトが合わなくなる。
                if (existing.width != PanelWidth || existing.height != PanelHeight)
                {
                    existing.Release();
                    existing.width = PanelWidth;
                    existing.height = PanelHeight;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

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

            // 背景の半透明を活かすため、Unlit を Transparent にする (Step 7)。
            // アルファは RT 側が持つ (背景 0.55 / 文字 1.0)。
            material.SetFloat("_Surface", 1f); // 1 = Transparent
            material.SetFloat("_Blend", 0f);   // 0 = Alpha
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

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
