using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// 箱コックピットの内寸の半分（高さ）。
        /// **計器パネルの位置がこれを基準にしている**ので、箱の枠を組む側と
        /// パネル側で共有する。パネルを実機の画面へ移すのは 11-3。
        /// </summary>
        const float HalfHeight = Size * 0.5f;

        /// <summary>パネル背景の不透明度 (Step 7)。文字は不透明のまま。</summary>
        const float PanelBackgroundAlpha = 0.55f;

        const string RenderTexturePath = "Assets/Materials/InstrumentPanel.renderTexture";
        const string PanelMaterialPath = "Assets/Materials/InstrumentPanel.mat";

        public struct Result
        {
            public Camera CockpitCamera;
            public InstrumentPanel Panel;

            /// <summary>微振動で揺らす対象 (Step 8-0)。カメラと枠の共通の親。</summary>
            public Transform ShakeRig;

            /// <summary>どの定義で組んだか (Step 11-0c)。HUD とテストが読む。</summary>
            public CockpitIdentity Identity;

            /// <summary>窓の投影面積比の計測器 (Step 11-2b)。F4 とテストが読む。</summary>
            public CockpitMetrics Metrics;

            /// <summary>計器を映す 5 面 (Step 11-3)。箱では空。</summary>
            public CockpitScreens Screens;

            /// <summary>補助光と発光 (Step 11-4)。F4 とテストが読む。</summary>
            public CockpitLights Lights;
        }

        /// <summary>既定の定義（`CockpitCatalog.Requested`）で組む。</summary>
        public static Result Build(Transform shipTransform, int cockpitLayer)
            => Build(shipTransform, cockpitLayer, CockpitCatalog.Requested);

        /// <summary>
        /// 定義を指定して組む (Step 11-2a)。
        /// **差し替えの継ぎ目はここ。** 11-6 で有料アセットを足すときも、
        /// 定義を 1 つ増やしてここへ渡すだけで済むはず、というのが 11-2a の主張。
        /// </summary>
        public static Result Build(Transform shipTransform, int cockpitLayer,
                                   SolarSystem.Core.CockpitDefinition requested)
        {
            // 微振動 (Step 8-0) はカメラと枠を**一緒に**揺らす必要がある。
            // カメラだけ揺らすと枠が泳いで見える。実機ではカメラは枠に固定されていて、
            // 枠は静止したまま外の景色が揺れるのが正しい。
            var rig = new GameObject("CockpitRig");
            rig.transform.SetParent(shipTransform, false);

            var root = new GameObject("Cockpit");
            root.transform.SetParent(rig.transform, false);
            SetLayerRecursive(root, cockpitLayer);

            // **どの定義で組んだかをシーンに残す (Step 11-0c)。**
            // 「レンダラー数が箱より多い」のような間接的な判定ではなく、
            // Id を直接読めるようにする。HUD にも出るので実機で取り違えに気づける。
            SolarSystem.Core.CockpitDefinition built =
                CockpitCatalog.Resolve(requested, out bool fellBackToBox);
            CockpitIdentity identity = root.AddComponent<CockpitIdentity>();
            identity.Bind(built.Id, requested.Id, fellBackToBox);

            // ---- カメラ (視点は固定。船の姿勢に従う) ----
            var camGo = new GameObject("Cam_Cockpit");
            camGo.transform.SetParent(rig.transform, false);
            Camera cockpitCam = camGo.AddComponent<Camera>();

            // ---- 機体 ----
            // **箱と実アセットで同じ手順を通す。** 置く物が違うだけで、
            // 目の位置の決め方も、レイヤの与え方も、あとの計器も同じ。
            SolarSystem.Core.Vec3d eye = built.NeedsPrefab
                ? PlacePrefab(root.transform, built, cockpitLayer)
                : BuildBoxHull(root.transform, cockpitLayer);

            // 視点はプレハブ原点基準のメートル。コックピット空間は 1 m = 1 unit。
            //
            // **機首の向きはプレハブ側を回して合わせる**ので、目の位置にも同じ回転を掛ける。
            // カメラ自身は回さない。**カメラを回すと船の後ろを向いてしまう**
            // （船の前方は Unity の Z+ で、外の景色はそちらにある）。
            Quaternion align = AlignToShipForward(built);
            camGo.transform.localPosition =
                align * new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
            camGo.transform.localRotation = Quaternion.identity;

            // ---- 計器 ----
            // **下端の帯は撤去した (11-3c)。** 計器はコックピットの画面に載る。
            //
            // **箱コックピットのときだけ帯を残す。** 箱には画面が無いので、
            // 帯まで消すと計器が 1 つも出ない。判定は**フォルダの有無ではなく
            // `Definition.Screens` が空かどうか**（アセットを持たないクローンで
            // シーンを組み直すと箱に落ちる → そこでは帯が出る）。
            bool hasScreens = built.Screens.Count > 0;
            InstrumentPanel panel = hasScreens
                ? BuildPanelOnly(root.transform)
                : BuildInstrumentStrip(root.transform, cockpitLayer);

            // ---- 計器の画面 (Step 11-3) ----
            var screens = root.AddComponent<CockpitScreens>();
            screens.Bind(BuildScreens(root.transform, built, panel, camGo.transform));

            // **テスト柄の Canvas はシーンに止めた状態で焼く (11-3b)。**
            // Start() が走らない EditMode の撮影経路（ScenarioCapture）でも、
            // 止めた側が同じ RT へ描き込まないようにするため。
            foreach (CockpitScreens.Screen screen in screens.Screens)
            {
                if (screen.CameraPattern == null)
                {
                    continue;
                }

                foreach (Canvas canvas in
                         screen.CameraPattern.GetComponentsInChildren<Canvas>(true))
                {
                    canvas.enabled = false;
                }
            }
            // ---- 補助光 (Step 11-4) ----
            ApplyRenderingLayers(root.transform, cockpitLayer);

            var lights = root.AddComponent<CockpitLights>();
            lights.Bind(BuildFillLight(camGo.transform, cockpitLayer));
            lights.Apply();

            // ---- 窓の物差し (Step 11-2b) ----
            // **箱には窓が無い**ので、そのときは空のまま。計測器は「測れない」を返す。
            var metrics = root.AddComponent<CockpitMetrics>();
            metrics.Bind(cockpitCam, CollectGlass(root.transform));

            return new Result
            {
                CockpitCamera = cockpitCam, Panel = panel,
                ShakeRig = rig.transform, Identity = identity, Metrics = metrics,
                Screens = screens, Lights = lights,
            };
        }

        /// <summary>
        /// RenderTexture へ描く側。Canvas + TextMeshPro を専用の
        /// Orthographic カメラで撮る。カメラスタックには入れない。
        /// コックピットから遠く離れた場所に置いて、他のカメラに写らないようにする。
        /// </summary>
        /// <summary>
        /// 計器の本体だけを作る (Step 11-3c)。**帯もカメラも Canvas も作らない。**
        /// 文字を出す先は `BuildScreens` がコックピットの画面に組む。
        /// </summary>
        static InstrumentPanel BuildPanelOnly(Transform parent)
        {
            var go = new GameObject("Instruments");
            go.transform.SetParent(parent, false);
            return go.AddComponent<InstrumentPanel>();
        }

        /// <summary>
        /// 下端の帯 (Step 4〜7)。**箱コックピットのときだけ組む (11-3c)。**
        ///
        /// アセットを持たないクローンではコックピットが箱に落ち、画面が 1 枚も
        /// 無い。そこで帯まで消すと計器が出なくなるので、**画面が空のときだけ**
        /// 従来の帯を出す。実アセットのときは組まない。
        /// </summary>
        static InstrumentPanel BuildInstrumentStrip(Transform root, int cockpitLayer)
        {
            RenderTexture rt = GetOrCreateRenderTexture();
            Material panelMaterial = GetOrCreatePanelMaterial(rt);

            GameObject panelQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelQuad.name = "InstrumentSurface";
            panelQuad.transform.SetParent(root, false);
            panelQuad.layer = cockpitLayer;
            Object.DestroyImmediate(panelQuad.GetComponent<Collider>());

            // 画面の下端へ寄せる (Step 7)。高さは従来の半分 (0.40 -> 0.20)。
            // 中心は視線から約 20.9 度下、上下に約 5.4 度。垂直 FOV 60 度の
            // 下半分 (30 度) の内側に収まり、上 3/4 は完全に空く。
            // RT が 768x160 なのでアスペクトは 4.8 を保つ。
            panelQuad.transform.localPosition =
                new Vector3(0f, -HalfHeight * 0.62f, Size * 1.05f);
            panelQuad.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            panelQuad.transform.localScale = new Vector3(Size * 0.96f, Size * 0.20f, 1f);
            panelQuad.GetComponent<Renderer>().sharedMaterial = panelMaterial;

            return BuildInstrumentSource(root, rt);
        }

        static InstrumentPanel BuildInstrumentSource(Transform parent, RenderTexture rt)
        {
            var sourceRoot = new GameObject("InstrumentSource");
            sourceRoot.transform.SetParent(parent, false);
            // **原点の近くに置く (Step 11-3c)。** 他のカメラに写らないのは
            // Culling Mask（UI レイヤーだけ）で決まっている。遠くへ置くと
            // float の刻みが Canvas の 1 画素を超え、船を回すだけで絵が変わる。
            sourceRoot.transform.localPosition = new Vector3(0f, SourceOffset, 0f);

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

        /// <summary>
        /// アセットの姿勢を船に合わせる回転 (Step 11-2c)。
        /// 機首を Z+ へ、上を Y+ へ。
        ///
        /// ■ **`FromToRotation` は使わない（実機で踏んだ不具合）**
        /// 前方だけを渡すと、**Z+ と反平行のときに回転軸が一意に決まらない。**
        /// Unity は直交する軸を任意に選んで 180 度回すため、X 軸が選ばれると
        /// **前後と上下が同時に反転する。** Hi-Rez の機首はちょうど -Z なので
        /// 毎回この縮退に当たっていた。
        ///
        /// `LookRotation(forward, up)` は「前方を Z+ に、上方を Y+ に」写す回転の逆写像
        /// なので、その逆行列がアセット -> 船の補正になる。**2 軸で決まるので縮退しない。**
        /// ヨー 180 度を足すような応急処置にしないのは、別のアセットで同じ罠に
        /// 落ちるのを防ぐため。
        /// </summary>
        static Quaternion AlignToShipForward(SolarSystem.Core.CockpitDefinition definition)
        {
            var forward = new Vector3((float)definition.EyeForward.X,
                                      (float)definition.EyeForward.Y,
                                      (float)definition.EyeForward.Z);
            var up = new Vector3((float)definition.EyeUp.X,
                                 (float)definition.EyeUp.Y,
                                 (float)definition.EyeUp.Z);

            if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
            {
                return Quaternion.identity;
            }

            return Quaternion.Inverse(
                Quaternion.LookRotation(forward.normalized, up.normalized));
        }

        /// <summary>
        /// 取り込んだプレハブを置く (Step 11-2a)。**リンクのまま置く。**
        ///
        /// `PrefabUtility.InstantiatePrefab` を使うと、保存したシーンには
        /// **プレハブの GUID と差分だけ**が載る。`Object.Instantiate` だと階層が
        /// まるごとシーンへ展開され、アセットを持たないクローンでも中身が復元されて
        /// しまう。**EULA はアセットの再配布を禁じている。**
        /// </summary>
        static SolarSystem.Core.Vec3d PlacePrefab(
            Transform parent, SolarSystem.Core.CockpitDefinition definition, int cockpitLayer)
        {
            string path = AssetDatabase.GUIDToAssetPath(definition.PrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                // ここへ来るのは CockpitCatalog.Resolve が通したあとなので、
                // 取り込みが壊れている。黙って箱にせず落とす。
                throw new System.InvalidOperationException(
                    $"プレハブが読めない: {definition.Id} / GUID {definition.PrefabGuid}");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = AlignToShipForward(definition);
            instance.transform.localScale = Vector3.one * (float)definition.Scale;

            SetLayerRecursive(instance, cockpitLayer);

            CockpitBoundsSolver.Log(definition, prefab);

            Bounds? bounds = CockpitBoundsSolver.LocalBounds(prefab);
            return definition.EyeLocal
                   ?? (bounds.HasValue
                       ? CockpitBoundsSolver.SuggestEye(prefab, bounds.Value, definition)
                       : SolarSystem.Core.Vec3d.Zero);
        }

        /// <summary>
        /// 計器を映す面を組む (Step 11-3)。
        ///
        /// **面ごとに RenderTexture を 1 枚ずつ。** アトラスにしない理由は、ベンダーの
        /// UV 配置が画面上の必要解像度と釣り合っていないため（小さいゲージほど目に近い）。
        /// 実測ではアトラスだと 3072x2400 必要になるが、面ごとなら合計 1.31M px で足りる。
        ///
        /// **案 A と案 B の Canvas を両方組む。** 実機で見比べて決めるための一時的な足場。
        /// </summary>
        /// <summary>
        /// 計器の描画元を置く高さ [unit] (Step 11-3c)。**原点の近くに置く。**
        /// ここでの 1 unit は 1 m（コックピット空間）。
        /// </summary>
        const float SourceOffset = 40f;

        /// <summary>描画元どうしの間隔 [unit]。カメラの視野 (最大 2 unit) より広い。</summary>
        const float SourceSpacing = 4f;

        static List<CockpitScreens.Screen> BuildScreens(
            Transform root, SolarSystem.Core.CockpitDefinition definition, InstrumentPanel panel,
            Transform eye)
        {
            var built = new List<CockpitScreens.Screen>();
            IReadOnlyList<SolarSystem.Core.CockpitScreen> layout = definition.Screens;

            if (layout.Count == 0)
            {
                return built; // 箱には画面が無い
            }

            // **描画元は原点の近くに置く (Step 11-3c で直した)。**
            //
            // 以前は (1e5, 1e5, 0) に置いていた。他のカメラに写らないようにする
            // つもりだったが、**写らないのはレイヤー（Canvas は 5、コックピットは
            // 専用レイヤー）で決まっている**ので、離す必要はそもそも無かった。
            //
            // 離した代償が大きい。原点から 141,528 unit の位置では float の刻みが
            // 2^17 * 2^-23 = 0.0156 unit で、**Canvas の 1 画素 (1/544 unit) の
            // 8.5 倍**になる。Canvas は船の子なので、船が回ると世界座標が動き、
            // 丸めの出方が変わる。中身は何も変えていないのに絵が変わる。
            // 実測: 船を 0.5 度回すだけで RT の 48,620 画素が変わり、
            // テスト柄の円の外接が 32 px 動いた（「マウスを大きく動かすと歪む」）。
            var sourceRoot = new GameObject("ScreenSources");
            sourceRoot.transform.SetParent(root, false);
            sourceRoot.transform.localPosition = new Vector3(0f, SourceOffset, 0f);

            for (int i = 0; i < layout.Count; i++)
                {
                SolarSystem.Core.CockpitScreen a = layout[i];
                Renderer target = FindRenderer(root, a.RendererName);
                if (target == null)
                {
                    Debug.LogWarning($"[CockpitBuilder] 画面のレンダラーが無い: {a.RendererName}");
                    continue;
                }

                Vector2Int size = TextureSizeFor(target, a.TextureLongSide);
                RenderTexture texture = GetOrCreateScreenTexture(a.RendererName, size.x, size.y);

                // ---- 逆歪ませ (Step 11-3c) ----
                // **行列はメッシュと目の姿勢から自動で出す。** 手で数値を入れない。
                // 平面でなければここで例外になる（11-6 で曲面が来たときに黙って
                // 崩れないように）。
                CockpitWarp.Face warp =
                    CockpitWarp.Solve(target, eye, size.x / (float)size.y);

                // **圧縮される軸にだけ積む。** 逆歪ませた中身は RT の一部しか
                // 使わないので、そのぶん解像度が落ちる。歪ませたほうの RT は
                // 中身が歪んでいるので、比が面の実寸と違っていて構わない。
                var warpedSize = new Vector2Int(Round4(size.x * warp.TextureScale.x),
                                                Round4(size.y * warp.TextureScale.y));
                RenderTexture warped = GetOrCreateScreenTexture(
                    a.RendererName + "_Warped", warpedSize.x, warpedSize.y);

                Debug.Log($"[CockpitBuilder] 逆歪ませ {a.RendererName}: "
                          + $"平面からのずれ {warp.PlanarDeviation:P3} / "
                          + $"倍率 {warp.TextureScale.x:F2} x {warp.TextureScale.y:F2} / "
                          + $"RT {size.x}x{size.y} -> {warpedSize.x}x{warpedSize.y}");


                // **Canvas 同士が互いのカメラに写らないよう、組ごとに離して置く。**
                // カメラは正射影で高さ 1 unit・幅は最大 2 unit なので、
                // `SourceSpacing` 離れていれば視野に入らない。**必要最小限にする**
                // （遠いほど float の刻みが粗くなる。上のコメントを参照）。
                var slot = new GameObject("Screen_" + a.RendererName);
                slot.transform.SetParent(sourceRoot.transform, false);
                slot.transform.localPosition = new Vector3(i * SourceSpacing, 0f, 0f);

                built.Add(new CockpitScreens.Screen
                {
                    RendererName = a.RendererName,
                    Target = target,
                    Texture = texture,
                    BaseMapSt = BaseMapStFor(target),
                    Transparent = IsTransparent(target),
                    CameraA = BuildScreenSource(slot.transform, texture, a.Role, panel, 0f,
                                                IsTransparent(target)),
                    CameraPattern = BuildPatternSource(slot.transform, texture, SourceSpacing),
                    Facing = BuildFacingQuad(root, target, texture, eye, IsTransparent(target)),
                    Warped = warped,
                    Warp = warp.Warp,
                    WarpMaterial = GetOrCreateWarpMaterial(a.RendererName),
                });
            }

            return built;
        }

        /// <summary>
        /// **視線に正対するクアッドを、元の面の位置に置く (Step 11-3b)。**
        ///
        /// ■ なぜ成立するか
        /// このゲームは**目がコックピットに固定**されていて、船が回っても目と計器盤の
        /// 位置関係が変わらない。**見る角度が 1 つに決まる**ので、組み立て時に正対
        /// させておけば実行時も正対のまま（毎フレームの向き直しが要らない）。
        ///
        /// ■ 大きさと位置
        /// 元の面の頂点を目から見た視線方向の平面へ投影し、その外接矩形を覆う
        /// 大きさにする。**画面上で元の面をちょうど覆う。**
        /// 奥行きは元の面の最も近い頂点よりわずかに手前（2 %）に置く。
        ///
        /// **既定では無効。** F4 の「計器の向き」で出す。
        /// </summary>
        static Renderer BuildFacingQuad(Transform root, Renderer source, RenderTexture texture,
                                        Transform eye, bool transparent)
        {
            Mesh mesh = source.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null || eye == null)
            {
                return null;
            }

            // 目から見た座標へ。カメラは回っていないので、これがそのまま視線座標。
            float near = float.MaxValue;
            var points = new List<Vector3>();
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 view = eye.InverseTransformPoint(source.transform.TransformPoint(v));
                if (view.z <= 0f)
                {
                    continue;
                }

                points.Add(view);
                near = Mathf.Min(near, view.z);
            }

            if (points.Count < 3)
            {
                return null;
            }

            // 手前 2 % の平面へ、各頂点の視線を延ばして写す（画面上の位置は変わらない）。
            float plane = near * 0.98f;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 v in points)
            {
                float x = v.x * plane / v.z;
                float y = v.y * plane / v.z;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }

            // **RT の縦横比に合わせて内接させる (Step 11-3b)。**
            // 外接のままだと、投影の外接矩形の比（実測 351:119 = 2.95）と
            // RT の比（1024:544 = 1.88）の差で**横に 1.57 倍伸びる**（実測で楕円になった）。
            // 正対させる目的は歪みを消すことなので、覆う面積より比を優先する。
            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            float textureAspect = texture.width / (float)texture.height;

            if (boxWidth / boxHeight > textureAspect)
            {
                boxWidth = boxHeight * textureAspect;
            }
            else
            {
                boxHeight = boxWidth / textureAspect;
            }

            var go = new GameObject("FacingQuad_" + source.name);
            go.transform.SetParent(root, false);
            go.layer = source.gameObject.layer;

            // 目のローカル座標で置いてから、コックピットの下へ付け替える。
            go.transform.position = eye.TransformPoint(new Vector3(centerX, centerY, plane));
            go.transform.rotation = eye.rotation;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateQuadMesh(boxWidth, boxHeight);

            Debug.Log($"[CockpitBuilder] 正対クアッド {source.name}: 目からの距離 {plane:F3} m"
                      + $" / 大きさ {boxWidth:F3} x {boxHeight:F3} m"
                      + $" / 中心 ({centerX:F3}, {centerY:F3})"
                      + $" / 外接 {maxX - minX:F3} x {maxY - minY:F3}");

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetOrCreateFacingMaterial(transparent);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false; // 既定は「面に貼る」

            return renderer;
        }

        /// <summary>
        /// **内装だけを照らす補助光 (Step 11-4)。**
        ///
        /// ■ 他の段へ漏らさない
        /// `cullingMask` をコックピット層だけにする。**URP がこれを尊重するかは
        /// 実測で確かめる**（尊重しないなら Rendering Layers へ切り替える）。
        /// 判定は「過大な強度にして、外の天体の画素が動くか」で行う。
        ///
        /// ■ 範囲
        /// コックピット空間は 1 m = 1 unit なので、範囲 3 は 3 m。内装を包む
        /// だけの大きさにして、届く先を物理的にも狭めておく。
        ///
        /// ■ 影は落とさない
        /// 潰れを防ぐだけの光なので影は要らない。**影を出すとコックピット段の
        /// 深度クリアと相まって描画順の当たり外れが増える。**
        /// </summary>
        static Light BuildFillLight(Transform eye, int cockpitLayer)
        {
            var go = new GameObject("CockpitFillLight");
            go.transform.SetParent(eye, false);
            go.layer = cockpitLayer;

            SolarSystem.Core.Vec3d offset = SolarSystem.Core.CockpitDefinition.FillLightOffset;
            go.transform.localPosition =
                new Vector3((float)offset.X, (float)offset.Y, (float)offset.Z);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = (float)SolarSystem.Core.CockpitDefinition.FillLightRangeMeters;
            light.intensity =
                (float)SolarSystem.Core.CockpitDefinition.DefaultFillLightIntensity;
            light.color = new Color(0.82f, 0.88f, 1.00f); // わずかに青い機内灯
            light.shadows = LightShadows.None;
            // **止めているのはこちら。** `cullingMask` は URP では効かない
            // （実測 / 11-4a）が、他の描画経路のために正しい値を入れておく。
            light.renderingLayerMask = (int)SolarSystem.Core.CockpitDefinition
                .CockpitRenderingLayer;
            light.cullingMask = 1 << cockpitLayer;
            light.renderMode = LightRenderMode.ForcePixel;

            Debug.Log($"[CockpitBuilder] 補助光: 強さ {light.intensity:F2} / "
                      + $"範囲 {light.range:F1} m / 位置 {go.transform.localPosition} / "
                      + $"rendering layer 0x{light.renderingLayerMask:X} / "
                      + $"culling mask 0x{light.cullingMask:X}");

            return light;
        }

        /// <summary>XY 平面の四角。**法線は +Z（目の側）**、UV は 0..1。</summary>
        static Mesh CreateQuadMesh(float width, float height)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;

            var mesh = new Mesh { name = "FacingQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-hw, -hh, 0f), new Vector3(-hw, hh, 0f),
                new Vector3(hw, hh, 0f), new Vector3(hw, -hh, 0f),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f),
            };

            // **両面にする。**
            // 片面だと、巻き順を取り違えたときに「有効で、位置も正しいのに
            // 1 画素も出ない」という分かりにくい形で失敗する（実測でそうなった）。
            // 切り分けの道具なので、三角形 2 枚ぶんのコストより確実さを取る。
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mesh.normals = new[] { -Vector3.forward, -Vector3.forward,
                                   -Vector3.forward, -Vector3.forward };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>正対クアッド用の Unlit。**RT は MPB で面ごとに差す。**</summary>
        static Material GetOrCreateFacingMaterial(bool transparent)
        {
            string path = transparent
                ? "Assets/Materials/FacingScreenTransparent.mat"
                : "Assets/Materials/FacingScreen.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                name = Path.GetFileNameWithoutExtension(path),
            };

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = 3000;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// その面のマテリアルが Transparent か (Step 11-3b)。
        ///
        /// **透明な面には透明な RT を描く。** HUD (`CockpitEquipments_TargetScreens`) は
        /// Transparent で、キャノピー越しに外が見える位置にある。RT の背景を不透明にすると
        /// **黒い板が視界の中央を塞ぐ**（実機で確認）。
        /// </summary>
        static bool IsTransparent(Renderer renderer)
        {
            Material material = renderer.sharedMaterial;
            return material != null && material.HasProperty("_Surface")
                   && material.GetFloat("_Surface") > 0.5f;
        }

        static Renderer FindRenderer(Transform root, string name)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == name)
                {
                    return r;
                }
            }

            return null;
        }

        /// <summary>
        /// メッシュの UV 矩形を 0..1 へ写す `_BaseMap_ST` (Step 11-3b)。
        /// **ここで焼き込む。** 実行時にメッシュを読み直さない。
        /// </summary>
        static Vector4 BaseMapStFor(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return new Vector4(1f, 1f, 0f, 0f);
            }

            Vector2[] uv = mesh.uv;
            float u0 = 1f, u1 = 0f, v0 = 1f, v1 = 0f;
            foreach (Vector2 t in uv)
            {
                u0 = Mathf.Min(u0, t.x); u1 = Mathf.Max(u1, t.x);
                v0 = Mathf.Min(v0, t.y); v1 = Mathf.Max(v1, t.y);
            }

            SolarSystem.Core.UvRemap.ToUnit(u0, u1, out double sx, out double ox);
            SolarSystem.Core.UvRemap.ToUnit(v0, v1, out double sy, out double oy);
            return new Vector4((float)sx, (float)sy, (float)ox, (float)oy);
        }

        /// <summary>
        /// 面の実寸から RT の寸法を出す (Step 11-3b)。
        ///
        /// **UV の勾配（u/v 1 単位あたり面上で何 m 進むか）から実寸を出す。**
        /// UV は面上の位置に対して厳密に線形であることを実測済みなので、
        /// 一次独立な 3 頂点から勾配が一意に決まる。
        ///
        /// **投影サイズから決めてはいけない。** 見かけの大きさは目の位置と面の傾きで
        /// 変わるが、RT が貼られるのは面そのものなので、比が合わないと文字がつぶれる。
        /// </summary>
        static Vector2Int TextureSizeFor(Renderer renderer, int longSide)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.uv.Length < 3)
            {
                return new Vector2Int(longSide, longSide);
            }

            Vector3[] p = mesh.vertices;
            Vector2[] uv = mesh.uv;

            // uv が一次独立になる 3 点を探す。
            int i1 = -1, i2 = -1;
            for (int i = 1; i < uv.Length && i2 < 0; i++)
            {
                Vector2 d1 = uv[i] - uv[0];
                if (d1.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                if (i1 < 0)
                {
                    i1 = i;
                    continue;
                }

                Vector2 a1 = uv[i1] - uv[0];
                if (Mathf.Abs((a1.x * d1.y) - (a1.y * d1.x)) > 1e-6f)
                {
                    i2 = i;
                }
            }

            if (i1 < 0 || i2 < 0)
            {
                return new Vector2Int(longSide, longSide);
            }

            Vector2 e1 = uv[i1] - uv[0];
            Vector2 e2 = uv[i2] - uv[0];
            Vector3 f1 = p[i1] - p[0];
            Vector3 f2 = p[i2] - p[0];

            float det = (e1.x * e2.y) - (e1.y * e2.x);
            Vector3 dpdu = ((f1 * e2.y) - (f2 * e1.y)) / det;
            Vector3 dpdv = ((f2 * e1.x) - (f1 * e2.x)) / det;

            float u0 = float.MaxValue, u1 = float.MinValue;
            float v0 = float.MaxValue, v1 = float.MinValue;
            foreach (Vector2 t in uv)
            {
                u0 = Mathf.Min(u0, t.x); u1 = Mathf.Max(u1, t.x);
                v0 = Mathf.Min(v0, t.y); v1 = Mathf.Max(v1, t.y);
            }

            float widthMeters = dpdu.magnitude * (u1 - u0);
            float heightMeters = dpdv.magnitude * (v1 - v0);
            if (widthMeters <= 0f || heightMeters <= 0f)
            {
                return new Vector2Int(longSide, longSide);
            }

            float aspect = widthMeters / heightMeters;
            int w = aspect >= 1f ? longSide : Round4(longSide * aspect);
            int h = aspect >= 1f ? Round4(longSide / aspect) : longSide;

            Debug.Log($"[CockpitBuilder] {renderer.name} 実寸 {widthMeters * 1000f:F1} x "
                      + $"{heightMeters * 1000f:F1} mm (比 {aspect:F3}) -> RT {w}x{h}");

            return new Vector2Int(w, h);
        }

        static int Round4(float value) => Mathf.Max(4, Mathf.RoundToInt(value / 4f) * 4);

        /// <summary>
        /// 逆歪ませの blit に使うマテリアル (Step 11-3c)。**面ごとに 1 枚。**
        ///
        /// 行列は面ごとに違うが、**行列のプロパティは .mat に保存されない**
        /// （保存できるのは float / color / vector / texture だけ）。
        /// 実行時に `CockpitScreens.ApplyMode` が入れ直す。
        /// </summary>
        static Material GetOrCreateWarpMaterial(string rendererName)
        {
            string path = "Assets/Materials/Warp_"
                          + rendererName.Replace("CockpitEquipments_", string.Empty)
                          + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("SolarSystem/ScreenWarp");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "SolarSystem/ScreenWarp が見つからない");
            }

            var material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(path),
            };

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static RenderTexture GetOrCreateScreenTexture(string rendererName, int width, int height)
        {
            string path = "Assets/Materials/Screen_"
                          + rendererName.Replace("CockpitEquipments_", string.Empty)
                          + ".renderTexture";

            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (existing != null)
            {
                if (existing.width != width || existing.height != height)
                {
                    existing.Release();
                    existing.width = width;
                    existing.height = height;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

                return existing;
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Screen_" + rendererName,
                antiAliasing = 1,
            };

            AssetDatabase.CreateAsset(rt, path);
            return rt;
        }

        /// <summary>
        /// 1 つの面の描画元（カメラ + Canvas + TMP）を作る (Step 11-3)。
        /// **カメラは enabled = false のまま。** 描くのは `CockpitScreens` が 10 Hz で呼ぶ。
        /// </summary>
        static Camera BuildScreenSource(Transform parent, RenderTexture rt,
                                        SolarSystem.Core.ScreenRole role, InstrumentPanel panel,
                                        float offsetY, bool transparent)
        {
            var camGo = new GameObject("Cam_Screen");
            camGo.transform.SetParent(parent, false);
            camGo.transform.localPosition = new Vector3(0f, offsetY, -2f);

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 透明な面は背景も透明にする。不透明な面は暗い下地を敷いて文字を読みやすくする。
            cam.backgroundColor = transparent
                ? new Color(0f, 0f, 0f, 0f)
                : new Color(0.02f, 0.03f, 0.04f, 1f);
            cam.cullingMask = 1 << 5; // UI レイヤーのみ
            cam.targetTexture = rt;
            cam.enabled = false;

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(camGo.transform, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 4f;
            canvasGo.GetComponent<RectTransform>().sizeDelta = new Vector2(rt.width, rt.height);

            BuildRole(canvasGo.transform, role, panel, rt.height);
            return cam;
        }

        /// <summary>
        /// **テスト柄の描画元 (Step 11-3b の切り分け道具)。**
        ///
        /// 計器の表示ではなく、貼り方を見るための柄を描く:
        ///   - 最外周 1 px の枠線 … 面の端まで貼れているか
        ///   - 等間隔の格子 … 等間隔が崩れていれば貼り方の歪み
        ///   - 中心の真円 … 楕円に見えれば縦横比の問題
        ///   - 四隅の別々の印 (TL/TR/BL/BR) … 反転・回転
        ///   - 基準線に沿った "ABC 123" … 傾いていれば文字側の問題
        ///
        /// **柄は白の矩形だけで作る**（画像アセットを増やさない）。円は小さな点を
        /// 円周に並べて表す。
        /// </summary>
        static Camera BuildPatternSource(Transform parent, RenderTexture rt, float offsetY)
        {
            var camGo = new GameObject("Cam_ScreenPattern");
            camGo.transform.SetParent(parent, false);
            camGo.transform.localPosition = new Vector3(0f, offsetY, -2f);

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 1f);
            cam.cullingMask = 1 << 5;
            cam.targetTexture = rt;
            cam.enabled = false;

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(camGo.transform, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 4f;
            canvasGo.GetComponent<RectTransform>().sizeDelta = new Vector2(rt.width, rt.height);

            BuildPattern(canvasGo.transform, rt.width, rt.height);
            return cam;
        }

        /// <summary>
        /// テスト柄の中身 (Step 11-3b)。
        ///
        /// ■ **線は UI の矩形ではなく、画素を自分で置いたテクスチャで描く。**
        /// 1 px の `Image` を並べた最初の版では、実測で**縦線が 2 本（幅 4 px / 位置も
        /// 12 px ずれ）しか出ず、外周と横線が 1 本も出なかった。** UI のレイアウトを
        /// 経由するかぎり「1 px を確実に置く」を保証できないので、テクスチャを
        /// 自前で作って貼る形にした。**切り分けの土台なので、ここは確実さを取る。**
        ///
        /// 文字だけは TMP（計器と同じ経路）で載せる。文字側の傾きを見るため。
        /// </summary>
        static void BuildPattern(Transform canvas, int width, int height)
        {
            var raw = new GameObject("PatternTexture");
            raw.transform.SetParent(canvas, false);
            raw.layer = 5;

            var image = raw.AddComponent<UnityEngine.UI.RawImage>();
            image.texture = GetOrCreatePatternTexture(width, height);

            var rect = raw.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 四隅の印。**それぞれ違う文字**で反転・回転が分かる。
            float corner = Mathf.Min(width, height) * 0.18f;
            AddPatternText(canvas, "TL", "TL", 0.03f, 0.35f, 0.75f, 0.97f, corner);
            AddPatternText(canvas, "TR", "TR", 0.65f, 0.97f, 0.75f, 0.97f, corner);
            AddPatternText(canvas, "BL", "BL", 0.03f, 0.35f, 0.03f, 0.25f, corner);
            AddPatternText(canvas, "BR", "BR", 0.65f, 0.97f, 0.03f, 0.25f, corner);

            // 基準線（テクスチャ側に引いてある）に載る文字。
            AddPatternText(canvas, "Sample", "ABC 123", 0.05f, 0.95f, 0.5f, 0.72f,
                           Mathf.Min(width, height) * 0.16f);
        }

        /// <summary>
        /// テスト柄のテクスチャを作る（無ければ）。**画素を 1 つずつ置く。**
        ///
        ///   - 最外周 1 px の白枠 … 面の端まで貼れているか
        ///   - 1 px の格子（8 分割）… 等間隔が崩れていれば貼り方の歪み
        ///   - 中心の真円（1 px の輪）… 楕円に見えれば縦横比の問題
        ///   - 中央の基準線 … 文字の傾きを見る基準
        /// </summary>
        static Texture2D GetOrCreatePatternTexture(int width, int height)
        {
            string path = $"Assets/Materials/ScreenTestPattern_{width}x{height}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var background = new Color32(5, 5, 10, 255);
            var frame = new Color32(220, 245, 235, 255);
            var grid = new Color32(90, 140, 130, 255);
            var circle = new Color32(240, 180, 100, 255);
            var baseline = new Color32(140, 240, 190, 255);

            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            // **格子は正方形にする (Step 11-3c)。**
            // 軸ごとに 8 分割すると、1024x544 の RT では 128x68 の長方形になり、
            // **RT を直接見たときに「中身が横に伸びている」ように見える**
            // （実機で報告された）。間隔を両軸そろえれば、目で見て正方形かどうかが
            // そのまま歪みの判定になる。中心から外へ引く。
            int step = Mathf.Max(8, Mathf.Min(width, height) / 8);
            for (int gx = width / 2; gx < width; gx += step)
            {
                for (int y = 0; y < height; y++)
                {
                    pixels[(y * width) + gx] = grid;
                    pixels[(y * width) + width - 1 - gx] = grid;
                }
            }

            for (int gy = height / 2; gy < height; gy += step)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[(gy * width) + x] = grid;
                    pixels[((height - 1 - gy) * width) + x] = grid;
                }
            }

            // 中心の基準線。
            for (int x = 0; x < width; x++)
            {
                pixels[((height / 2) * width) + x] = baseline;
            }

            // 真円（1 px の輪）。**画素の縦横は等倍なので、貼って楕円なら貼り方の問題。**
            float radius = Mathf.Min(width, height) * 0.35f;
            int steps = Mathf.CeilToInt(radius * 8f);
            for (int i = 0; i < steps; i++)
            {
                float a = (i / (float)steps) * Mathf.PI * 2f;
                int cx = Mathf.RoundToInt((width * 0.5f) + (Mathf.Cos(a) * radius));
                int cy = Mathf.RoundToInt((height * 0.5f) + (Mathf.Sin(a) * radius));
                if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                {
                    pixels[(cy * width) + cx] = circle;
                }
            }

            // 最外周 1 px の枠。
            for (int x = 0; x < width; x++)
            {
                pixels[x] = frame;
                pixels[((height - 1) * width) + x] = frame;
            }

            for (int y = 0; y < height; y++)
            {
                pixels[y * width] = frame;
                pixels[(y * width) + width - 1] = frame;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)),
                               texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // **拡大縮小もフィルタも掛けない設定にする。** 1 px の線が消えては意味が無い。
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>箱の枠と外殻を組む。**取り込みが無いときのフォールバック。**</summary>
        static SolarSystem.Core.Vec3d BuildBoxHull(Transform root, int cockpitLayer)
        {
            // 前方に開口部、上下左右に厚みのある枠。プリミティブの箱だけで作る。
            Material frame = MaterialLibrary.SolidMaterial("CockpitFrame", new Color(0.16f, 0.17f, 0.19f));
            const float halfW = Size * 0.9f;
            const float halfH = HalfHeight;
            const float depth = Size * 1.2f;
            const float bar = Size * 0.12f;

            AddBox(root, "Frame_Top", new Vector3(0f, halfH, depth * 0.5f),
                new Vector3(halfW * 2f, bar, depth), frame, cockpitLayer);
            AddBox(root, "Frame_Bottom", new Vector3(0f, -halfH, depth * 0.5f),
                new Vector3(halfW * 2f, bar, depth), frame, cockpitLayer);
            AddBox(root, "Frame_Left", new Vector3(-halfW, 0f, depth * 0.5f),
                new Vector3(bar, halfH * 2f, depth), frame, cockpitLayer);
            AddBox(root, "Frame_Right", new Vector3(halfW, 0f, depth * 0.5f),
                new Vector3(bar, halfH * 2f, depth), frame, cockpitLayer);

            // 背面と床。振り返っても宇宙が見えないようにする。
            AddBox(root, "Hull_Back", new Vector3(0f, 0f, -Size * 0.6f),
                new Vector3(halfW * 2f, halfH * 2f, bar), frame, cockpitLayer);
            AddBox(root, "Hull_Floor", new Vector3(0f, -halfH * 0.55f, 0f),
                new Vector3(halfW * 1.6f, bar * 0.5f, Size), frame, cockpitLayer);

            // 箱は原点が目の位置。**設計上ここが基準**なので定義側には持たせない。
            return SolarSystem.Core.Vec3d.Zero;
        }

        /// <summary>
        /// 窓（ガラス）のレンダラーを集める (Step 11-2b)。
        ///
        /// **マテリアル名で引く。** 子の順番や階層の形に依存すると、別のアセットへ
        /// 差し替えたときに黙って壊れる。実測では `Cockpit3Grey_Glass` が 1 枚。
        /// </summary>
        static List<Renderer> CollectGlass(Transform root)
        {
            var found = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null
                        && material.name.Contains(CockpitMetrics.GlassMaterialKeyword))
                    {
                        found.Add(renderer);
                        break;
                    }
                }
            }

            return found;
        }

        /// <summary>コックピットの子へレイヤーを配る。</summary>
        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// **コックピット段のレンダラーへレンダリングレイヤーを配る (Step 11-4)。**
        ///
        /// 補助光は内装のレンダリングレイヤーしか照らさない。**組み立ての最後に
        /// 一度で配る**——面や正対クアッドは後から作られるので、生成箇所ごとに
        /// 書くと必ず取りこぼす（実測でクアッドが漏れた）。
        ///
        /// 既定のビットも残す。外すと**太陽光が当たらなくなる**
        /// （太陽の Directional は既定のビットしか持たない）。
        /// </summary>
        static void ApplyRenderingLayers(Transform root, int cockpitLayer)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.layer == cockpitLayer)
                {
                    r.renderingLayerMask =
                        SolarSystem.Core.CockpitDefinition.CockpitRenderingLayerMask;
                }
            }
        }

        /// <summary>役割ごとの中身。**行の並びだけを決め、値は InstrumentPanel が流す。**</summary>
        static void BuildRole(Transform canvas, SolarSystem.Core.ScreenRole role,
                              InstrumentPanel panel, int heightPixels)
        {
            switch (role)
            {
                case SolarSystem.Core.ScreenRole.Flight:
                    AddRows(canvas, panel, heightPixels, new[]
                    {
                        ("SPD", InstrumentPanel.Field.Speed),
                        ("DST", InstrumentPanel.Field.Distance),
                        ("ETA", InstrumentPanel.Field.Eta),
                    });
                    break;

                case SolarSystem.Core.ScreenRole.TargetFull:
                    AddRows(canvas, panel, heightPixels, new[]
                    {
                        ("TGT", InstrumentPanel.Field.Target),
                        ("ALN", InstrumentPanel.Field.Alignment),
                        ("DOCK", InstrumentPanel.Field.Docking),
                    });
                    break;

                case SolarSystem.Core.ScreenRole.Alignment:
                    AddRows(canvas, panel, heightPixels, new[]
                    {
                        ("ALN", InstrumentPanel.Field.Alignment),
                    });
                    break;

                case SolarSystem.Core.ScreenRole.SpeedDial:
                    AddRows(canvas, panel, heightPixels, new[]
                    {
                        ("DIAL", InstrumentPanel.Field.Dial),
                    });
                    break;

                case SolarSystem.Core.ScreenRole.Autopilot:
                    AddRows(canvas, panel, heightPixels, new[]
                    {
                        ("AP", InstrumentPanel.Field.Autopilot),
                    });
                    break;
            }
        }

        /// <summary>
        /// 見出しと値を N 行に並べる。**行の高さから文字の大きさを決める**ので、
        /// 256x256 の小さいゲージでも 1024x544 の大画面でも同じ手順で組める。
        /// </summary>
        static void AddRows(Transform canvas, InstrumentPanel panel, int heightPixels,
                            (string Caption, InstrumentPanel.Field Field)[] rows)
        {
            float rowHeight = 1f / rows.Length;
            float pixels = heightPixels * rowHeight;

            for (int i = 0; i < rows.Length; i++)
            {
                float top = 1f - (i * rowHeight);
                float bottom = top - rowHeight;

                // 見出しは行の上寄り、値は下寄り。1 行しかない役割では大きく出る。
                float split = rows.Length == 1 ? 0.55f : 0.45f;
                float mid = bottom + (rowHeight * split);

                AddScreenText(canvas, rows[i].Caption + "Caption", rows[i].Caption,
                              mid, top, pixels * 0.28f,
                              new Color(0.35f, 0.55f, 0.50f, 1f), null, panel);

                AddScreenText(canvas, rows[i].Field.ToString(), "---",
                              bottom, mid, pixels * 0.42f,
                              new Color(0.55f, 0.95f, 0.75f, 1f), rows[i].Field, panel);
            }
        }

        static void AddScreenText(Transform parent, string name, string initial,
                                  float yMin, float yMax, float fontSize, Color color,
                                  InstrumentPanel.Field? field, InstrumentPanel panel)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var text = go.AddComponent<TextMeshProUGUI>();

            // **自動縮小は使わない (Step 11-3b で撤去)。**
            // 文字列ごとに大きさが跳ね、視線を動かすたびに文字が波打っていた
            // （実測: HUD の整列表示が 64.5px と 146.8px の間で 2.3 倍動いていた）。
            // 代わりに**起こりうる最長の文字列が入る大きさに固定する。**
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = initial;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.02f, yMin);
            rect.anchorMax = new Vector2(0.98f, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (field.HasValue)
            {
                FitToLongest(text, field.Value, fontSize);
                panel.Register(field.Value, text);
            }
        }

        /// <summary>
        /// **起こりうる最長の文字列が枠に入る大きさへ縮める (Step 11-3b)。**
        ///
        /// TMP の `GetPreferredValues` で実測してから決めるので、書式が変わっても
        /// 追従する。**実行時には縮めない**（跳ねの原因になる）。
        /// </summary>
        static void FitToLongest(TMP_Text text, InstrumentPanel.Field field, float fontSize)
        {
            string longest = InstrumentPanel.LongestSample(field);

            var rect = text.rectTransform;
            var canvasRect = rect.parent as RectTransform;
            float width = canvasRect.sizeDelta.x * (rect.anchorMax.x - rect.anchorMin.x);
            float height = canvasRect.sizeDelta.y * (rect.anchorMax.y - rect.anchorMin.y);

            Vector2 preferred = text.GetPreferredValues(longest, 0f, 0f);
            if (preferred.x <= 0f || preferred.y <= 0f)
            {
                return;
            }

            // **余白を 15 % 取る (Step 11-3b)。**
            // 「ちょうど入る」設計だと、想定した文字列と実際の文字列の幅の差
            // （数字の字送りの違いなど）で枠から出る。実測では `90.0 deg` が
            // 幅 491 px のうち 421 px を使っており、9 文字で余白 18 px しか無かった。
            const float margin = 0.85f;
            float scale = Mathf.Min(width * margin / preferred.x, height * margin / preferred.y);
            if (scale < 1f)
            {
                text.fontSize = Mathf.Floor(fontSize * scale);
            }
        }

        /// <summary>テスト柄の文字 (Step 11-3b)。**計器と同じ TMP の経路で載せる。**</summary>
        static void AddPatternText(Transform parent, string name, string text,
                                   float xMin, float xMax, float yMin, float yMax, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 5;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = false;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = text;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
