using System;
using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **接続面と Scale を人間が絵で決めるための判定リグ (Step 13-3b)。**
    ///
    /// ■ これは道具であって配置ではない
    /// 実際のステーション（`StationView` の箱）には**一切触らない。**
    /// `StationDefinition` にも書かない。**値は人間が決める。**
    ///
    /// ■ 既定は OFF
    /// `-stationJudge` が付いたときだけ動く。無指定なら `SetActive(false)` のまま。
    /// **平面の 36 枚は動かない。**
    ///
    /// ■ 置き方
    /// カメラを動かさず、**モデルのほうをカメラの前へ毎フレーム置き直す。**
    /// 船・オートパイロット・シナリオに影響を出さないため。
    /// コックピットはそのまま描かれるので、**実機では既知の大きさの基準になる**
    /// （Demo 3 で寸法が成立済み）。
    ///
    /// ■ 目印は 3 種、個別に ON/OFF
    ///   船の断面枠 : 1.6075 x 1.6312 m の矩形。**比較対象そのものを同じ場所に置く**
    ///   候補 3 円   : (a) 金色の円 / (b) 突き出した円盤 / (c) module1 の胴
    ///   1 m グリッド : 絶対的な大きさの手がかり
    ///
    /// **マテリアルはシーン側から渡される**（`SceneBuilder` が `MaterialLibrary` で作る）。
    /// ここでは実行時のインスタンスに発光だけ載せる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StationJudgeRig : MonoBehaviour
    {
        /// <summary>目印を出す位置のオフセット [units]。ポート面と z ファイトしない距離。</summary>
        const float MarkerLift = 0.0002f;

        /// <summary>円の輪の太さ（直径に対する比）。</summary>
        const float RingThickness = 0.03f;

        /// <summary>1 m グリッドの広がり [m]（中心から片側）。</summary>
        const int GridHalfExtentMeters = 5;

        /// <summary>グリッドと枠の線の太さ [m]。</summary>
        const float LineWidthMeters = 0.02f;

        [SerializeField] Transform _model;
        [SerializeField] Camera _camera;

        [SerializeField] Transform _shipFrame;
        [SerializeField] Transform _grid;
        [SerializeField] Transform _ringGold;
        [SerializeField] Transform _ringPlate;
        [SerializeField] Transform _ringBody;

        /// <summary>モデルが取り込まれていて、リンクが解決できているか。</summary>
        public bool HasModel => _model != null;

        /// <summary>**プレハブ単位 -> units の倍率。** F4 が振る。</summary>
        public double Scale { get; private set; } = StationJudge.ScaleInitial;

        public JudgeViewpoint Viewpoint { get; private set; } = JudgeViewpoint.Docking;

        /// <summary>
        /// **ドッキング視点での目からポート面までの距離 [units]。** F4 が振る。
        /// 下限は Nearfield の near clip（= ゲーム本体の制約）。**割れない。**
        /// </summary>
        public double DockingDistanceUnits { get; private set; }
            = StationJudge.ProvisionalStandoffUnits;

        public void SetDockingDistance(double value)
            => DockingDistanceUnits = StationJudge.ClampDockingDistance(value);

        public bool ShowShipFrame { get; private set; } = true;
        public bool ShowRings { get; private set; } = true;
        public bool ShowGrid { get; private set; } = true;

        /// <summary>直近の視点距離 [units]。HUD が読む。</summary>
        public double LastDistanceUnits { get; private set; }

        public void SetScale(double value)
        {
            Scale = Math.Max(StationJudge.ScaleMin, Math.Min(StationJudge.ScaleMax, value));
        }

        public void SetViewpoint(JudgeViewpoint value) => Viewpoint = value;

        public void SetMarkers(bool shipFrame, bool rings, bool grid)
        {
            ShowShipFrame = shipFrame;
            ShowRings = rings;
            ShowGrid = grid;
        }

        public void Bind(Camera camera) => _camera = camera;

        /// <summary>
        /// **目印に発光を載せる (Step 13-3b)。**
        /// URP/Lit の単色マテリアルは太陽光の当たり方で暗くなる。目印は
        /// 「そこに何メートルの円があるか」を読むためのものなので、
        /// 照明に依らず同じ明るさで出したい。
        ///
        /// **触るのは実行時のマテリアルインスタンスだけ**（`renderer.material`）。
        /// アセットには書き戻さない（F4 の運用と同じ / §0-C）。
        /// </summary>
        void Awake()
        {
            foreach (Transform t in new[] { _shipFrame, _grid, _ringGold, _ringPlate, _ringBody })
            {
                if (t == null)
                {
                    continue;
                }

                var renderer = t.GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                Material instance = renderer.material;
                Color baseColor = instance.HasProperty("_BaseColor")
                    ? instance.GetColor("_BaseColor")
                    : Color.white;

                instance.EnableKeyword("_EMISSION");
                instance.SetColor("_EmissionColor", baseColor * MarkerEmission);
            }
        }

        /// <summary>目印の発光の強さ。**bloom のしきい値 0.90 の下**（滲ませない）。</summary>
        const float MarkerEmission = 0.85f;

        /// <summary>起動引数に `-stationJudge` があるか。</summary>
        public static bool Requested()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], StationJudge.Arg, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>`UniverseRoot.Tick` から呼ぶ。**Update() は持たない。**</summary>
        public void Apply()
        {
            if (_camera == null || _model == null)
            {
                return;
            }

            float scale = (float)Scale;
            Transform cam = _camera.transform;

            double fov = _camera.fieldOfView;
            LastDistanceUnits = Viewpoint == JudgeViewpoint.Docking
                ? DockingDistanceUnits
                : StationJudge.OverviewDistanceUnits(Scale, fov);

            var distance = (float)LastDistanceUnits;
            Vector3 anchor = cam.position + cam.forward * distance;

            _model.localScale = new Vector3(scale, scale, scale);

            if (Viewpoint == JudgeViewpoint.Docking)
            {
                // ポート面をカメラの正面へ。モデルのローカル +Z がカメラを向く。
                Quaternion rotation = Quaternion.LookRotation(-cam.forward, cam.up);
                _model.rotation = rotation;

                Vec3d p = StationJudge.PortFaceLocal;
                var portLocal = new Vector3((float)p.X, (float)p.Y, (float)p.Z) * scale;
                _model.position = anchor - rotation * portLocal;

                PlaceMarkers(anchor - cam.forward * MarkerLift, rotation, scale, true);
                LogOnce("dock", _model.position, cam.position);
            }
            else
            {
                // 全長を縦に。モデルのローカル +Z が画面の上、ローカル +Y がカメラ側。
                Quaternion rotation = Quaternion.LookRotation(cam.up, -cam.forward);
                _model.rotation = rotation;

                // bbox 中心を画面の中心へ。
                var center = new Vector3(0.0300f, 0.2481f, -6.5522f) * scale;
                _model.position = anchor - rotation * center;

                // 目印はポート面に付いたまま運ぶ。
                Vec3d p = StationJudge.PortFaceLocal;
                var portLocal = new Vector3((float)p.X, (float)p.Y, (float)p.Z) * scale;
                Vector3 port = _model.position + rotation * portLocal;
                PlaceMarkers(port, rotation, scale, false);
                LogOnce("over", _model.position, cam.position);
            }
        }

        bool _logged;

        /// <summary>
        /// **最初の 1 回だけ、置いた結果を数で残す (Step 13-3b)。**
        /// 「置いたつもり」と「そこに在る」は別なので、実測を 1 行残す。
        /// </summary>
        void LogOnce(string tag, Vector3 modelPosition, Vector3 cameraPosition)
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            var renderer = _model.GetComponentInChildren<Renderer>();

            Debug.Log($"[StationJudge] {tag} / scale={Scale:F5}"
                      + $" / 距離={LastDistanceUnits:F5} units"
                      + $" / model={modelPosition:F5} cam={cameraPosition:F5}"
                      + $" / |model-cam|={Vector3.Distance(modelPosition, cameraPosition):F5}"
                      + $" / renderer={(renderer != null ? renderer.name : "無し")}"
                      + $" enabled={(renderer != null && renderer.enabled)}"
                      + $" layer={(renderer != null ? renderer.gameObject.layer : -1)}"
                      + $" / camMask={_camera.cullingMask} near={_camera.nearClipPlane} far={_camera.farClipPlane}");
        }

        void PlaceMarkers(Vector3 position, Quaternion rotation, float scale, bool faceCamera)
        {
            // 目印はメートルで作ってあるので units へ写す（1 unit = 1 km）。
            const float MetersToUnits = 0.001f;

            Quaternion markerRotation = faceCamera
                ? rotation
                : Quaternion.LookRotation(_camera.transform.position - position,
                                          _camera.transform.up);

            Place(_shipFrame, position, markerRotation,
                  Vector3.one * MetersToUnits, ShowShipFrame);

            Place(_grid, position, markerRotation,
                  Vector3.one * MetersToUnits, ShowGrid);

            // 円は**構造物の一部**なので、寸法はプレハブ単位 x Scale [units]。
            Place(_ringGold, position, markerRotation,
                  Vector3.one * (float)StationJudge.ToUnits(StationJudge.GoldDiscMeters, Scale),
                  ShowRings);
            Place(_ringPlate, position, markerRotation,
                  Vector3.one * (float)StationJudge.ToUnits(
                      StationJudge.ProtrudingPlateMeters, Scale),
                  ShowRings);
            Place(_ringBody, position, markerRotation,
                  Vector3.one * (float)StationJudge.ToUnits(StationJudge.ModuleBodyMeters, Scale),
                  ShowRings);
        }

        static void Place(Transform t, Vector3 position, Quaternion rotation,
                          Vector3 localScale, bool visible)
        {
            if (t == null)
            {
                return;
            }

            if (t.gameObject.activeSelf != visible)
            {
                t.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            t.position = position;
            t.rotation = rotation;
            t.localScale = localScale;
        }

        // ---- 目印のメッシュ（プロシージャル）----
        //
        // **新しいシェーダは書かない。** URP/Lit の単色マテリアル（`MaterialLibrary`）に
        // 実行時に発光を載せる。シェーダを増やさないので SPI マクロの話は出ない
        // （Step 12 の規約は自前シェーダにだけ掛かる）。

        /// <summary>直径 1 の平らな輪。XY 平面、法線 +Z。</summary>
        public static Mesh BuildRing(int segments)
        {
            if (segments < 8)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), segments, "8 以上");
            }

            const float outer = 0.5f;
            float inner = outer - RingThickness * 0.5f;

            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);
                vertices[i * 2] = new Vector3(c * inner, s * inner, 0f);
                vertices[i * 2 + 1] = new Vector3(c * outer, s * outer, 0f);

                int n = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = n * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = n * 2 + 1;
                triangles[t + 5] = n * 2;
            }

            return Finish(vertices, triangles, "JudgeRing");
        }

        /// <summary>矩形の枠。**メートルで作る**（呼び手が units へ写す）。</summary>
        public static Mesh BuildFrame(float width, float height, float lineWidth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float t = lineWidth * 0.5f;

            AddQuad(vertices, triangles, -hw - t, hh - t, hw + t, hh + t);   // 上
            AddQuad(vertices, triangles, -hw - t, -hh - t, hw + t, -hh + t); // 下
            AddQuad(vertices, triangles, -hw - t, -hh, -hw + t, hh);         // 左
            AddQuad(vertices, triangles, hw - t, -hh, hw + t, hh);           // 右

            return Finish(vertices.ToArray(), triangles.ToArray(), "JudgeFrame");
        }

        /// <summary>1 m 刻みの格子。**メートルで作る。**</summary>
        public static Mesh BuildGrid(int halfExtentMeters, float lineWidth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            float e = halfExtentMeters;
            float t = lineWidth * 0.5f;

            for (int i = -halfExtentMeters; i <= halfExtentMeters; i++)
            {
                AddQuad(vertices, triangles, i - t, -e, i + t, e);
                AddQuad(vertices, triangles, -e, i - t, e, i + t);
            }

            return Finish(vertices.ToArray(), triangles.ToArray(), "JudgeGrid");
        }

        static void AddQuad(List<Vector3> vertices, List<int> triangles,
                            float x0, float y0, float x1, float y1)
        {
            int b = vertices.Count;
            vertices.Add(new Vector3(x0, y0, 0f));
            vertices.Add(new Vector3(x1, y0, 0f));
            vertices.Add(new Vector3(x1, y1, 0f));
            vertices.Add(new Vector3(x0, y1, 0f));

            triangles.Add(b);
            triangles.Add(b + 2);
            triangles.Add(b + 1);
            triangles.Add(b);
            triangles.Add(b + 3);
            triangles.Add(b + 2);
        }

        static Mesh Finish(Vector3[] vertices, int[] triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3>(vertices));
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static int DefaultGridHalfExtent => GridHalfExtentMeters;

        public static float DefaultLineWidth => LineWidthMeters;
    }
}
