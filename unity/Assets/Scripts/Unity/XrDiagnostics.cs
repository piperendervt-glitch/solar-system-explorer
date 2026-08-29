using System;
using System.Collections.Generic;
using System.Text;
using SolarSystem.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **XR 診断 (F5)。Step 12 の準備で、まだ XR は入れない。**
    ///
    /// ■ なぜ平面で先に作るのか
    /// XR の成否は「4 層が両眼で正しく重なるか」で決まるが、その判定基準を先に
    /// 数値で決めることはできない。**先に平面で層ごとの見え方を切り替えられる
    /// ようにして、人が目で承認した状態の数値を基準線として取る。**
    ///
    /// **ここでは絵の良し悪しを判断しない。** 出すのは画像と数値だけ。
    ///
    /// ■ 入力について
    /// F5 と数字キーは `Keyboard.current` を直接読む。**共有の .inputactions は
    /// 触らない**（飛行の入力面を増やしたくないため）。テストは API を直接叩く。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrDiagnostics : MonoBehaviour
    {
        /// <summary>測り直す間隔 [秒]。**毎フレーム測らない**（5 回描き直すので重い）。</summary>
        public const double MeasureIntervalSeconds = 0.5;

        /// <summary>測定に使う解像度。**実機の画面比と同じ 16:9。**</summary>
        public const int MeasureWidth = 640;
        public const int MeasureHeight = 360;

        // ---- 起動引数 ----
        public const string OpenArg = "-xrDiag";
        public const string IsolateArg = "-xrIsolate";
        public const string HideArg = "-xrHide";
        public const string ColorMapArg = "-xrColorMap";
        public const string MaskArg = "-xrMask";
        public const string ProbesArg = "-xrProbes";
        public const string FaultArg = "-xrFault";

        /// <summary>計器の画面を XR 診断の数値に差し替える起動引数。</summary>
        public const string ScreenArg = "-xrScreen";

        /// <summary>故意破壊。**既定はすべて OFF。**</summary>
        public enum Fault
        {
            None = 0,

            /// <summary>Overlay の深度クリアを外す。</summary>
            NoDepthClear = 1,

            /// <summary>Overlay の描画順を入れ替える。</summary>
            SwapOverlayOrder = 2,

            /// <summary>
            /// 指定した層の culling mask を空にする。
            ///
            /// **Deep に掛けても星空は消えない。** スカイボックスは clearFlags で
            /// 描かれ、culling mask の対象外だから（実測 / セッション 0b）。
            /// Deep に掛けたときに壊れるのは**プロキシ殻だけ**。
            /// </summary>
            EmptyCullingMask = 3,

            /// <summary>Base カメラの clearFlags を Skybox から SolidColor へ。**星空が消える。**</summary>
            SkyboxOff = 4,

            /// <summary>Base カメラの描画を止める。**画面が丸ごと出なくなる。**</summary>
            BaseCameraOff = 5,

            /// <summary>
            /// **片目だけ層を描かない (Step 12 の本番用)。口だけ切ってある。**
            /// 平面では目が 1 つしかないので **no-op**。動かそうとしないこと。
            /// </summary>
            DropLayerInOneEye = 6,

            /// <summary>
            /// **片目だけ深度クリアを飛ばす (Step 12 の本番用)。口だけ。**
            /// 平面では **no-op**。
            /// </summary>
            SkipDepthClearInOneEye = 7,
        }

        /// <summary>片目の故意破壊で、どちらの目を壊すか。**平面では効かない。**</summary>
        public enum Eye
        {
            Left = 0,
            Right = 1,
        }

        [Serializable]
        public sealed class Probe
        {
            public XrLayer Layer;
            public Renderer Renderer;
        }

        [SerializeField] CameraStackController _stack;
        [SerializeField] CockpitMetrics _metrics;
        [SerializeField] CockpitScreens _screens;
        [SerializeField] List<Probe> _probes = new List<Probe>();

        [SerializeField] bool _open;
        [SerializeField] bool _probesVisible;
        [SerializeField] bool _colorMap;
        [SerializeField] bool _maskOverlay;

        /// <summary>0 = 通常 / 1..4 = その層だけ表示 / -1..-4 = その層だけ非表示。</summary>
        [SerializeField] int _isolation;

        [SerializeField] Fault _fault = Fault.None;
        [SerializeField] XrLayer _faultLayer = XrLayer.Cockpit;
        [SerializeField] Eye _faultEye = Eye.Left;

        CameraClearFlags _savedClearFlags;
        bool _clearFlagsSaved;
        bool _savedBaseEnabled;

        readonly int[] _savedMask = new int[4];
        bool _maskSaved;
        bool _faultApplied;

        double _nextMeasureAt;
        Texture2D _colorMapTexture;
        GUIStyle _style;

        public bool IsOpen => _open;

        public bool ProbesVisible => _probesVisible;

        public bool ColorMapEnabled => _colorMap;

        public bool MaskOverlayEnabled => _maskOverlay;

        public int Isolation => _isolation;

        public Fault ActiveFault => _fault;

        public XrLayer FaultLayer => _faultLayer;

        public Eye FaultEye => _faultEye;

        /// <summary>
        /// **片目だけの故意破壊が実際に効いたか。**
        /// 平面では常に false（目が 1 つしかないので何もしない）。
        /// XR を入れたら true になるべき箇所。
        /// </summary>
        public bool PerEyeFaultApplied { get; private set; }

        public IReadOnlyList<Probe> Probes => _probes;

        /// <summary>直近の測定結果。**測っていなければ null。**</summary>
        public XrDiagnosticsResult Last { get; private set; }

        public void Bind(CameraStackController stack, CockpitMetrics metrics,
                         CockpitScreens screens, IEnumerable<Probe> probes)
        {
            _stack = stack != null
                ? stack
                : throw new ArgumentNullException(nameof(stack), "カメラスタックが無い");

            _metrics = metrics;
            _screens = screens;
            _probes = new List<Probe>(probes ?? Array.Empty<Probe>());
        }

        void Start()
        {
            ReadArgs();
            ApplyProbes();
            ApplyIsolation();
            ApplyFault();
        }

        // ---------------------------------------------------------------- 状態

        public void SetOpen(bool open)
        {
            _open = open;
            if (_open)
            {
                _nextMeasureAt = 0.0;
            }
        }

        public void SetProbesVisible(bool visible)
        {
            _probesVisible = visible;
            ApplyProbes();
        }

        public void SetColorMap(bool on) => _colorMap = on;

        /// <summary>計器の画面が診断の数値になっているか。</summary>
        public bool ScreenDiagnostics => _screens != null && _screens.DiagnosticsEnabled;

        /// <summary>
        /// 計器の画面を診断の数値に差し替える。**HMD の中から読むための口。**
        /// </summary>
        public void SetScreenDiagnostics(bool on)
        {
            if (_screens == null)
            {
                return;
            }

            _screens.SetDiagnostics(on);
            if (on)
            {
                _nextMeasureAt = 0.0;
                _screens.SetDiagnosticsText(ScreenText());
            }
        }

        public void SetMaskOverlay(bool on) => _maskOverlay = on;

        /// <summary>0 = 通常 / 1..4 = その層だけ / -1..-4 = その層だけ隠す。</summary>
        public void SetIsolation(int isolation)
        {
            _isolation = Mathf.Clamp(isolation, -4, 4);
            ApplyIsolation();
        }

        public void SetFault(Fault fault, XrLayer layer) => SetFault(fault, layer, _faultEye);

        public void SetFault(Fault fault, XrLayer layer, Eye eye)
        {
            ClearFault();
            _fault = fault;
            _faultLayer = layer;
            _faultEye = eye;
            ApplyFault();
        }

        // ---------------------------------------------------------------- 適用

        void SaveMasks()
        {
            if (_maskSaved || _stack == null)
            {
                return;
            }

            _savedMask[0] = _stack.Deep.cullingMask;
            _savedMask[1] = _stack.Near.cullingMask;
            _savedMask[2] = _stack.Nearfield.cullingMask;
            _savedMask[3] = _stack.Cockpit.cullingMask;
            _maskSaved = true;
        }

        Camera CameraOf(XrLayer layer)
        {
            switch (layer)
            {
                case XrLayer.Deep: return _stack.Deep;
                case XrLayer.Near: return _stack.Near;
                case XrLayer.Nearfield: return _stack.Nearfield;
                default: return _stack.Cockpit;
            }
        }

        /// <summary>
        /// 層の表示を切り替える。**カメラは無効にせず culling mask だけを空にする。**
        /// 段ごと止めると URP のポストプロセスの掛かり方が変わり、測っている絵が
        /// 実機と別物になる (CLAUDE.md §0-B)。
        /// </summary>
        void ApplyIsolation()
        {
            if (_stack == null)
            {
                return;
            }

            SaveMasks();

            for (int i = 0; i < 4; i++)
            {
                Camera cam = CameraOf((XrLayer)i);
                if (cam == null)
                {
                    continue;
                }

                bool visible = _isolation == 0
                    || (_isolation > 0 && _isolation - 1 == i)
                    || (_isolation < 0 && -_isolation - 1 != i);

                cam.cullingMask = visible ? _savedMask[i] : 0;
            }

            // 故意破壊はマスクを触るので、入れ直す。
            if (_faultApplied && _fault == Fault.EmptyCullingMask)
            {
                CameraOf(_faultLayer).cullingMask = 0;
            }
        }

        void ApplyProbes()
        {
            foreach (Probe probe in _probes)
            {
                if (probe?.Renderer != null)
                {
                    probe.Renderer.enabled = _probesVisible;
                }
            }
        }

        void ApplyFault()
        {
            if (_stack == null || _fault == Fault.None)
            {
                return;
            }

            switch (_fault)
            {
                case Fault.NoDepthClear:
                    SetClearDepth(false);
                    break;

                case Fault.SwapOverlayOrder:
                    SwapOverlayOrder();
                    break;

                case Fault.EmptyCullingMask:
                    SaveMasks();
                    CameraOf(_faultLayer).cullingMask = 0;
                    break;

                case Fault.SkyboxOff:
                    _savedClearFlags = _stack.Deep.clearFlags;
                    _clearFlagsSaved = true;
                    _stack.Deep.clearFlags = CameraClearFlags.SolidColor;
                    _stack.Deep.backgroundColor = Color.black;
                    break;

                case Fault.BaseCameraOff:
                    _savedBaseEnabled = _stack.Deep.enabled;
                    _stack.Deep.enabled = false;
                    break;

                case Fault.DropLayerInOneEye:
                    // **XR のときだけ効く (Step D)。**
                    // その段のカメラを片目だけに描かせる。平面では stereoEnabled が
                    // false なので触らない（no-op のまま）。
                    ApplyDropLayerInOneEye();
                    break;

                case Fault.SkipDepthClearInOneEye:
                    // **実装できていない。盲点として残す (Step D)。**
                    // 深度クリアはカメラ単位で、目ごとに分ける口が Unity/URP に無い。
                    // `stereoTargetEye` は描画先の目を選ぶだけでクリアを分けられない。
                    PerEyeFaultApplied = false;
                    break;
            }

            _faultApplied = true;
        }

        /// <summary>
        /// **片目だけ層を描かない (Step D)。**
        /// その段のカメラの `stereoTargetEye` を反対の目だけにする。
        /// XR が動いていなければ何もしない。
        /// </summary>
        void ApplyDropLayerInOneEye()
        {
            Camera cam = CameraOf(_faultLayer);
            if (cam == null || !cam.stereoEnabled)
            {
                PerEyeFaultApplied = false;
                return;
            }

            _savedTargetEye = cam.stereoTargetEye;
            _targetEyeSaved = true;
            _targetEyeCamera = cam;

            // 左目を落とすなら右目だけに描く。
            cam.stereoTargetEye = _faultEye == Eye.Left
                ? StereoTargetEyeMask.Right
                : StereoTargetEyeMask.Left;

            PerEyeFaultApplied = true;
        }

        StereoTargetEyeMask _savedTargetEye;
        bool _targetEyeSaved;
        Camera _targetEyeCamera;

        /// <summary>故意破壊を戻す。**元に戻せることまでが道具の責任。**</summary>
        public void ClearFault()
        {
            if (!_faultApplied)
            {
                _fault = Fault.None;
                return;
            }

            switch (_fault)
            {
                case Fault.NoDepthClear:
                    SetClearDepth(true);
                    break;

                case Fault.SwapOverlayOrder:
                    SwapOverlayOrder();
                    break;

                case Fault.EmptyCullingMask:
                    ApplyIsolationAfterFaultCleared();
                    break;

                case Fault.SkyboxOff:
                    if (_clearFlagsSaved)
                    {
                        _stack.Deep.clearFlags = _savedClearFlags;
                        _clearFlagsSaved = false;
                    }

                    break;

                case Fault.BaseCameraOff:
                    _stack.Deep.enabled = _savedBaseEnabled;
                    break;

                case Fault.DropLayerInOneEye:
                case Fault.SkipDepthClearInOneEye:
                    PerEyeFaultApplied = false;
                    break;
            }

            _faultApplied = false;
            _fault = Fault.None;
            if (_targetEyeSaved && _targetEyeCamera != null)
            {
                _targetEyeCamera.stereoTargetEye = _savedTargetEye;
            }

            _targetEyeSaved = false;
            _targetEyeCamera = null;
        }

        void ApplyIsolationAfterFaultCleared()
        {
            _faultApplied = false;
            ApplyIsolation();
        }

        /// <summary>
        /// Overlay の深度クリアを切り替える。
        ///
        /// **`UniversalAdditionalCameraData.clearDepth` は URP 17 では読み取り専用。**
        /// 故意破壊のためだけに private フィールドを書き換える。
        /// **効かなければ測定値が動かないので、そのときは報告して止まる**
        /// （勝手に別の壊し方へ差し替えない）。
        /// </summary>
        public bool SetClearDepth(bool clear)
        {
            bool applied = false;
            foreach (XrLayer layer in new[] { XrLayer.Near, XrLayer.Nearfield, XrLayer.Cockpit })
            {
                Camera cam = CameraOf(layer);
                if (cam == null)
                {
                    continue;
                }

                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                System.Reflection.FieldInfo field = typeof(UniversalAdditionalCameraData).GetField(
                    "m_ClearDepth",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field == null)
                {
                    return false;
                }

                field.SetValue(data, clear);
                applied = true;
            }

            return applied;
        }

        void SwapOverlayOrder()
        {
            if (_stack == null || _stack.Deep == null)
            {
                return;
            }

            List<Camera> order = _stack.Deep.GetUniversalAdditionalCameraData().cameraStack;
            if (order.Count < 2)
            {
                return;
            }

            Camera first = order[0];
            order[0] = order[order.Count - 1];
            order[order.Count - 1] = first;
        }

        // ---------------------------------------------------------------- 入力

        /// <summary>10 Hz の Tick から呼ばれる。elapsedSeconds は Core の積算時間。</summary>
        public void Tick(double elapsedSeconds)
        {
            ReadKeys();

            if (!_open && (_screens == null || !_screens.DiagnosticsEnabled))
            {
                return;
            }

            if (elapsedSeconds < _nextMeasureAt)
            {
                return;
            }

            _nextMeasureAt = elapsedSeconds + MeasureIntervalSeconds;
            Measure();

            // **HMD の中からはミラーウィンドウが見えない。**
            // 数値を計器の画面へ流し、かぶったまま読めるようにする。
            if (_screens != null && _screens.DiagnosticsEnabled)
            {
                _screens.SetDiagnosticsText(ScreenText());
            }
        }

        void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f5Key.wasPressedThisFrame)
            {
                SetOpen(!_open);
            }

            if (!_open)
            {
                return;
            }

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            KeyControl[] digits =
            {
                keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key,
            };

            for (int i = 0; i < digits.Length; i++)
            {
                if (!digits[i].wasPressedThisFrame)
                {
                    continue;
                }

                int wanted = shift ? -(i + 1) : i + 1;
                SetIsolation(_isolation == wanted ? 0 : wanted);
            }

            if (keyboard.digit0Key.wasPressedThisFrame) { SetIsolation(0); }
            if (keyboard.cKey.wasPressedThisFrame) { SetColorMap(!_colorMap); }
            if (keyboard.mKey.wasPressedThisFrame) { SetMaskOverlay(!_maskOverlay); }
            if (keyboard.pKey.wasPressedThisFrame) { SetProbesVisible(!_probesVisible); }
            if (keyboard.dKey.wasPressedThisFrame) { SetScreenDiagnostics(!ScreenDiagnostics); }
        }

        void ReadArgs()
        {
            if (StandaloneCapture.HasArg(OpenArg)) { SetOpen(true); }
            if (StandaloneCapture.HasArg(ColorMapArg)) { SetColorMap(true); }
            if (StandaloneCapture.HasArg(MaskArg)) { SetMaskOverlay(true); }
            if (StandaloneCapture.HasArg(ProbesArg)) { SetProbesVisible(true); }
            if (StandaloneCapture.HasArg(ScreenArg)) { SetScreenDiagnostics(true); }

            string isolate = StandaloneCapture.ArgValue(IsolateArg);
            if (!string.IsNullOrEmpty(isolate) && int.TryParse(isolate, out int only))
            {
                SetIsolation(only);
            }

            string hide = StandaloneCapture.ArgValue(HideArg);
            if (!string.IsNullOrEmpty(hide) && int.TryParse(hide, out int hidden))
            {
                SetIsolation(-hidden);
            }

            string fault = StandaloneCapture.ArgValue(FaultArg);
            if (!string.IsNullOrEmpty(fault))
            {
                SetFault(ParseFault(fault), _faultLayer);
            }
        }

        public static Fault ParseFault(string spelling)
        {
            switch ((spelling ?? string.Empty).ToLowerInvariant())
            {
                case "nodepthclear": return Fault.NoDepthClear;
                case "swaporder": return Fault.SwapOverlayOrder;
                case "emptymask": return Fault.EmptyCullingMask;
                case "skyboxoff": return Fault.SkyboxOff;
                case "basecameraoff": return Fault.BaseCameraOff;
                case "droplayeroneeye": return Fault.DropLayerInOneEye;
                case "skipdepthoneeye": return Fault.SkipDepthClearInOneEye;
                default: return Fault.None;
            }
        }

        // ---------------------------------------------------------------- 測定

        /// <summary>
        /// いまの画面を測る。**5 回描く**（通常 + 層ごとに 1 回抜いた絵）。
        /// 層の切り分けは**マスクの差分**で取る（段ごと止めない）。
        /// </summary>
        public XrDiagnosticsResult Measure()
        {
            if (_stack == null)
            {
                return null;
            }

            SaveMasks();

            int width = MeasureWidth;
            int height = MeasureHeight;

            byte[] all = Render(width, height);

            // **プローブは「消した絵との差分」でだけ数える。**
            // 色だけで拾うと場面の色を数えてしまう（実測: 内装の青を Cockpit の
            // プローブとして 29,968 px 数えていた）。
            bool[] probeMask = null;
            if (_probesVisible)
            {
                foreach (Probe probe in _probes)
                {
                    if (probe?.Renderer != null) { probe.Renderer.enabled = false; }
                }

                byte[] withoutProbes = Render(width, height);
                probeMask = Differs(all, withoutProbes, width, height);

                foreach (Probe probe in _probes)
                {
                    if (probe?.Renderer != null) { probe.Renderer.enabled = true; }
                }
            }

            var without = new byte[4][];
            for (int i = 0; i < 4; i++)
            {
                Camera cam = CameraOf((XrLayer)i);
                if (cam == null)
                {
                    continue;
                }

                int keep = cam.cullingMask;
                cam.cullingMask = 0;
                without[i] = Render(width, height);
                cam.cullingMask = keep;
            }

            var result = new XrDiagnosticsResult
            {
                Width = width,
                Height = height,
                Probes = XrDiagnosticsModel.MeasureProbes(all, width, height, probeMask),
                Owner = Owner(all, without, width, height),
            };

            bool[] deepVisible = Differs(all, without[(int)XrLayer.Deep], width, height);

            // **外の景色は 3 段にまたがる。** この場面で眺めを担うのは Near 段で、
            // Deep 段はプロキシ殻だけ。Deep 単独では 0 画素になる場面がある。
            var outsideVisible = new bool[deepVisible.Length];
            foreach (XrLayer layer in new[] { XrLayer.Deep, XrLayer.Near, XrLayer.Nearfield })
            {
                bool[] one = Differs(all, without[(int)layer], width, height);
                for (int i = 0; i < outsideVisible.Length; i++)
                {
                    outsideVisible[i] |= one[i];
                }
            }
            bool[] windowRegion = HullMask(WindowRenderers(), width, height);
            bool[] panelRegion = HullMask(PanelRenderers(), width, height);
            // **帯は盤の輪郭から取る。** 盤と窓の境目が、縁の漏れが出る場所。
            bool[] band = XrDiagnosticsModel.Dilate(
                XrDiagnosticsModel.Boundary(panelRegion, width, height), width, height,
                XrDiagnosticsModel.WindowEdgeBandPixels);

            result.Leak = XrDiagnosticsModel.MeasureLeak(
                deepVisible, windowRegion, panelRegion, band, outsideVisible);
            result.ProbeMask = probeMask;
            result.DeepVisible = deepVisible;
            result.OutsideVisible = outsideVisible;
            result.WindowRegion = windowRegion;
            result.PanelRegion = panelRegion;
            result.Frame = all;

            Last = result;
            return result;
        }

        IEnumerable<Renderer> WindowRenderers()
            => _metrics != null ? _metrics.Glass : Array.Empty<Renderer>();

        IEnumerable<Renderer> PanelRenderers()
        {
            var found = new List<Renderer>();
            if (_screens == null)
            {
                return found;
            }

            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                if (screen?.Target != null)
                {
                    found.Add(screen.Target);
                }
            }

            return found;
        }

        /// <summary>レンダラー群の投影凸包を塗ったマスク。**視点は Cockpit 段のカメラ。**</summary>
        bool[] HullMask(IEnumerable<Renderer> renderers, int width, int height)
        {
            var mask = new bool[width * height];
            Camera cam = _stack.Cockpit != null ? _stack.Cockpit : _stack.Deep;
            if (cam == null)
            {
                return mask;
            }

            foreach (Renderer r in renderers)
            {
                var points = new List<Vec2d>();
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Vector3[] vertices = mesh.vertices;
                int stride = Mathf.Max(1, vertices.Length / 256);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    Vector3 view = cam.transform.InverseTransformPoint(
                        r.transform.TransformPoint(vertices[i]));
                    if (view.z <= 1.0e-4f)
                    {
                        continue;
                    }

                    float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    double px = ((view.x / view.z) / (half * (width / (double)height))) * (width * 0.5)
                                + (width * 0.5);
                    double py = ((view.y / view.z) / half) * (height * 0.5) + (height * 0.5);
                    points.Add(new Vec2d(px, py));
                }

                if (points.Count < 3)
                {
                    continue;
                }

                var xs = new List<double>(points.Count);
                var ys = new List<double>(points.Count);
                foreach (Vec2d p in points)
                {
                    xs.Add(p.X);
                    ys.Add(p.Y);
                }

                List<int> hullIndices = ProjectedAreaSolver.ConvexHull(xs, ys);
                var hull = new List<Vec2d>(hullIndices.Count);
                foreach (int index in hullIndices)
                {
                    hull.Add(points[index]);
                }

                bool[] filled = XrDiagnosticsModel.FillPolygon(hull, width, height);
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] |= filled[i];
                }
            }

            return mask;
        }

        static bool[] Differs(byte[] a, byte[] b, int width, int height)
        {
            var mask = new bool[width * height];
            if (a == null || b == null)
            {
                return mask;
            }

            for (int i = 0; i < mask.Length; i++)
            {
                int j = i * 3;
                int d = Mathf.Max(Mathf.Abs(a[j] - b[j]),
                                  Mathf.Max(Mathf.Abs(a[j + 1] - b[j + 1]),
                                            Mathf.Abs(a[j + 2] - b[j + 2])));
                mask[i] = d > 2;
            }

            return mask;
        }

        /// <summary>
        /// 画素ごとに「どの層が見えているか」。**手前の段を優先する。**
        /// -1 はどの段も寄与していない（星空だけ）。
        /// </summary>
        static sbyte[] Owner(byte[] all, byte[][] without, int width, int height)
        {
            var owner = new sbyte[width * height];
            for (int i = 0; i < owner.Length; i++)
            {
                owner[i] = -1;
            }

            for (int layer = 0; layer < 4; layer++)
            {
                if (without[layer] == null)
                {
                    continue;
                }

                bool[] differs = Differs(all, without[layer], width, height);
                for (int i = 0; i < owner.Length; i++)
                {
                    if (differs[i])
                    {
                        owner[i] = (sbyte)layer; // 後の層ほど手前なので上書きでよい
                    }
                }
            }

            return owner;
        }

        byte[] Render(int width, int height)
        {
            // **Base カメラが止まっていれば描かない。**
            // `Camera.Render()` は enabled を無視して描いてしまうので、
            // ここで見ないと「Base を止める」故意破壊が効かない
            // （測定経路だけ生きていて、実機と違う絵を測ることになる）。
            if (!_stack.Deep.enabled)
            {
                return new byte[width * height * 3];
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture previousTarget = _stack.Deep.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Deep.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                shot.Apply();

                byte[] pixels = shot.GetRawTextureData();
                UnityEngine.Object.DestroyImmediate(shot);
                return pixels;
            }
            finally
            {
                _stack.Deep.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // ---------------------------------------------------------------- 表示

        void OnGUI()
        {
            if (!_open)
            {
                return;
            }

            _style = _style ?? new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.85f, 0.95f, 1f, 1f) },
            };

            if (_colorMap && Last != null)
            {
                DrawColorMap();
            }

            var box = new Rect(Screen.width - 430f, 12f, 418f, 320f);
            GUI.Box(box, GUIContent.none);

            float y = box.y + 6f;
            foreach (string line in Lines())
            {
                GUI.Label(new Rect(box.x + 8f, y, box.width - 16f, 18f), line, _style);
                y += 17f;
            }
        }

        void DrawColorMap()
        {
            if (_colorMapTexture == null
                || _colorMapTexture.width != Last.Width || _colorMapTexture.height != Last.Height)
            {
                _colorMapTexture = new Texture2D(Last.Width, Last.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                };
            }

            var colors = new Color32[Last.Width * Last.Height];
            for (int i = 0; i < colors.Length; i++)
            {
                sbyte owner = Last.Owner[i];
                if (owner < 0)
                {
                    colors[i] = new Color32(0, 0, 0, 255);
                    continue;
                }

                double[] c = XrDiagnosticsModel.ProbeColor[owner];
                colors[i] = new Color32((byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255), 255);
            }

            if (_maskOverlay && Last.DeepVisible != null)
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    if (!Last.OutsideVisible[i])
                    {
                        continue;
                    }

                    // 窓を通して Deep が見えている画素 = 緑 / 計器盤への漏れ = 赤。
                    if (Last.PanelRegion[i])
                    {
                        colors[i] = new Color32(220, 40, 40, 255);
                    }
                    else if (Last.WindowRegion[i])
                    {
                        colors[i] = new Color32(40, 220, 60, 255);
                    }
                }
            }

            _colorMapTexture.SetPixels32(colors);
            _colorMapTexture.Apply();

            var rect = new Rect(12f, 12f, Last.Width, Last.Height);
            GUI.DrawTexture(rect, _colorMapTexture, ScaleMode.StretchToFill, false);
        }

        /// <summary>オーバーレイと計器の画面に出す行。**同じ文字列を両方で使う。**</summary>
        public string[] Lines()
        {
            var lines = new List<string>
            {
                "=== F5 XR 診断 (平面版) ===",
                "1-4: その層だけ / Shift+1-4: その層を隠す / 0: 通常",
                "C: 層カラーマップ  M: 窓・遮蔽マスク  P: プローブ  D: 計器の画面へ",
                "",
                $"層アイソレーション: {IsolationText()}",
                $"プローブ: {(_probesVisible ? "表示" : "非表示")} / "
                + $"カラーマップ: {(_colorMap ? "ON" : "off")} / "
                + $"マスク: {(_maskOverlay ? "ON" : "off")}",
                $"故意破壊: {FaultText()}",
                "",
            };

            if (Last == null)
            {
                lines.Add("（まだ測っていない）");
                return lines.ToArray();
            }

            lines.Add($"測定 {Last.Width}x{Last.Height}");
            foreach (XrDiagnosticsModel.ProbeHit hit in Last.Probes)
            {
                lines.Add("  " + hit);
            }

            XrDiagnosticsModel.LeakResult leak = Last.Leak;
            lines.Add($"窓の中の外の景色: {leak.WindowOutside} px"
                      + $" (うち Deep {leak.WindowDeep}) / 窓 {leak.WindowPixels} px");
            lines.Add($"盤への漏れ: 内側 {leak.PanelOutsideInterior} px"
                      + $" / 縁の帯 {leak.PanelOutsideEdgeBand} px / 盤 {leak.PanelPixels} px");
            lines.Add($"外が見えている画素: {leak.OutsideVisiblePixels} px"
                      + $" (うち Deep {leak.DeepVisiblePixels})");

            return lines.ToArray();
        }

        string IsolationText()
        {
            if (_isolation == 0)
            {
                return "通常（4 段とも表示）";
            }

            string name = XrDiagnosticsModel.LayerNames[Mathf.Abs(_isolation) - 1];
            return _isolation > 0 ? $"{name} だけ表示" : $"{name} だけ非表示";
        }

        string FaultText()
        {
            switch (_fault)
            {
                case Fault.NoDepthClear: return "**深度クリアを外している**";
                case Fault.SwapOverlayOrder: return "**Overlay の描画順を入れ替えている**";
                case Fault.EmptyCullingMask:
                    return $"**{XrDiagnosticsModel.LayerNames[(int)_faultLayer]} の"
                           + " culling mask を空にしている**";
                case Fault.SkyboxOff: return "**星空 (skybox) を消している**";
                case Fault.BaseCameraOff: return "**Base カメラを止めている**";
                case Fault.DropLayerInOneEye:
                case Fault.SkipDepthClearInOneEye:
                    return "片目の故意破壊（**平面では no-op**）";
                default: return "なし";
            }
        }

        /// <summary>計器の画面へ出すための短い版。**行数を絞る**（RT は 1024x544）。</summary>
        public string ScreenText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("XR DIAG");

            if (Last == null)
            {
                sb.AppendLine("(no data)");
                return sb.ToString();
            }

            foreach (XrDiagnosticsModel.ProbeHit hit in Last.Probes)
            {
                sb.AppendLine(hit.ToString());
            }

            sb.AppendLine($"WIN {Last.Leak.WindowOutside} / LEAK {Last.Leak.PanelOutsideInterior}"
                          + $"+{Last.Leak.PanelOutsideEdgeBand}");
            sb.Append($"ISO {IsolationText()}");
            return sb.ToString();
        }
    }

    /// <summary>1 回の測定の結果。</summary>
    public sealed class XrDiagnosticsResult
    {
        public int Width;
        public int Height;
        public List<XrDiagnosticsModel.ProbeHit> Probes;
        public XrDiagnosticsModel.LeakResult Leak;

        /// <summary>画素ごとの「見えている層」。-1 はどの段も寄与していない。</summary>
        public sbyte[] Owner;

        /// <summary>プローブを消した絵との差分。**色だけで数えないための的。**</summary>
        public bool[] ProbeMask;

        public bool[] DeepVisible;

        /// <summary>Deep + Near + Nearfield のいずれかが見えている画素。</summary>
        public bool[] OutsideVisible;
        public bool[] WindowRegion;
        public bool[] PanelRegion;

        /// <summary>測ったときの絵 (RGB24)。</summary>
        public byte[] Frame;
    }
}
