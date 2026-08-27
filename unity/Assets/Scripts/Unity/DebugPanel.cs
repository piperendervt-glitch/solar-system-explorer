using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 実行中に描画対象と数値を触る操作盤 (Step 8-0b)。
    ///
    /// **F1 の情報表示とは別。** F1 = 情報、F4 = 操作盤。
    /// 目で決めるしかない値を実機で決めるための道具で、
    /// 実装依頼 -> 再ビルド -> 起動 -> 目視 の往復を無くすのが目的。
    ///
    /// **F4 を押さなければ既存動作と完全に同一。** 閉じている間は何も適用しない。
    ///
    /// OnGUI なので RenderTexture 経由のスクショには写らない (CLAUDE.md 0-B)。
    /// exe なら ScreenCapture 経由で写る。
    ///
    /// Update() を持たない。UniverseRoot.Tick から呼ばれる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugPanel : MonoBehaviour
    {
        /// <summary>起動時からパネルを開く。見切れの確認を exe で自動化するため。</summary>
        public const string DebugPanelArg = "-debugPanel";

        /// <summary>シルエット測定で投影する頂点の上限。</summary>
        public const int MaxSampledVertices = 256;

        [SerializeField] UniverseRoot _root;
        [SerializeField] ShipRig _rig;
        [SerializeField] DebugPanelApplier _applier;
        [SerializeField] CameraStackController _stack;
        [SerializeField] DebugOverlay _overlay;

        GUIStyle _style;
        GUIStyle _cursorStyle;
        GUIStyle _dimStyle;
        Texture2D _backdrop;

        readonly Dictionary<int, Vector3[]> _sampleCache = new Dictionary<int, Vector3[]>();

        public DebugPanelModel Model { get; private set; }

        public bool IsOpen => Model != null && Model.IsOpen;

        /// <summary>直近に組んだ寸法。テストから見る。</summary>
        public DebugPanelLayout LastLayout { get; private set; }

        public void Bind(UniverseRoot root, ShipRig rig, DebugPanelApplier applier,
                         CameraStackController stack, DebugOverlay overlay)
        {
            _root = root;
            _rig = rig;
            _applier = applier;
            _stack = stack;
            _overlay = overlay;
        }

        /// <summary>既定値はコードの定数から取る。ここで数値を二重定義しない。</summary>
        public void Initialize(SolarSystemModel model)
        {
            var names = new List<string>();
            if (model != null)
            {
                foreach (CelestialBody body in model.Bodies)
                {
                    names.Add(body.Name);
                }
            }

            Model = DebugPanelModel.Create(
                names,
                PlanetAppearance.EarthAtmosphereStrength,
                PlanetAppearance.CloudOpacity,
                SunFlareController.BaseIntensity,
                CockpitShake.MaxAmplitudeRadians,
                PlanetAppearance.SunEmissionIntensity,
                PlanetAppearance.CoronaRadiusScale);

            if (HasDebugPanelArg())
            {
                Model.ToggleOpen();
            }
        }

        public static bool HasDebugPanelArg()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == DebugPanelArg)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>シナリオ切替時。**トグルだけ戻し、数値は保持する。**</summary>
        public void ResetTogglesForScenario() => Model?.ResetToggles();

        /// <summary>1 フレームぶん。入力を読んで反映する。</summary>
        public void Tick()
        {
            if (Model == null || _rig == null)
            {
                return;
            }

            if (_rig.DebugPanelPressed)
            {
                Model.ToggleOpen();
                if (!Model.IsOpen)
                {
                    // 閉じたときに、既定から変わった項目だけをログへ。
                    // これをコピーしてコードの定数に反映する運用。
                    Debug.Log(Model.BuildChangeLog());
                }
            }

            _rig.DebugPanelOpen = Model.IsOpen;

            // 場所が競合するので、開いている間は左側の HUD を隠す。
            // 右上のシナリオ確認項目は残す。
            if (_overlay != null)
            {
                _overlay.SuppressMainHud = Model.IsOpen;
            }

            if (Model.IsOpen)
            {
                if (_rig.DebugUpPressed) { Model.MoveCursor(-1); }
                if (_rig.DebugDownPressed) { Model.MoveCursor(1); }
                if (_rig.DebugLeftPressed) { Model.Adjust(-1); }
                if (_rig.DebugRightPressed) { Model.Adjust(1); }
                if (_rig.DebugSelectPressed) { Model.ToggleCurrent(); }
                if (_rig.DebugResetPressed) { Model.ResetAll(); }
            }

            // **閉じている間は何も適用しない。** 既存動作と同一にするため。
            if (Model.IsOpen && _applier != null)
            {
                _applier.Apply(Model);
            }
        }

        /// <summary>天体表の見出し。</summary>
        public static readonly string[] BodyHeader =
        {
            "天体", "距離[units]", "投影直径(計算)", "bbox(実測)", "引き渡し", "表現",
        };

        /// <summary>
        /// 天体ごとの 1 行を列に分けて返す。**計算値と実測を並べる。**
        ///
        /// 計算列は <see cref="AngularSizeSolver.ProjectedDiameterPixels"/>。
        /// LOD 判定に使う線形換算 (LastAngularPixels) ではない。
        /// 線形換算は画面いっぱいの大角度で真のシルエットと 15% ずれるので、
        /// 実測と並べる相手には使えない。**LOD 側は従来どおり線形のまま。**
        /// </summary>
        public List<string[]> BuildBodyRows()
        {
            var rows = new List<string[]>();
            if (_root == null || _root.SolarSystem == null || Model == null)
            {
                return rows;
            }

            foreach (CelestialBodyView view in _root.SolarSystem.Views)
            {
                if (view == null || view.Body == null)
                {
                    continue;
                }

                string name = view.Body.Name;
                double proxyShare = 1.0 - view.RealScaleBlend;
                double aPoint = (1.0 - view.Lod.Blend) * proxyShare;
                double aProxy = view.Lod.Blend * proxyShare;
                double aReal = view.RealScaleBlend;

                bool onPoint = Model.BoolOf(DebugPanelModel.BodyId(name, "point"));
                bool onProxy = Model.BoolOf(DebugPanelModel.BodyId(name, "proxy"));
                bool onReal = Model.BoolOf(DebugPanelModel.BodyId(name, "real"));

                string parts = Symbol(onPoint, aPoint, "点")
                               + Symbol(onProxy, aProxy, "殻")
                               + Symbol(onReal, aReal, "実");

                Renderer r = PickVisible(view, onPoint, onProxy, onReal,
                                         aPoint, aProxy, aReal, out bool useNear);
                Camera cam = CameraFor(useNear);

                rows.Add(new[]
                {
                    name,
                    view.LastDistance.ToString("E3"),
                    Projected(view, cam),
                    MeasureSilhouette(r, cam),
                    view.RealScaleBlend.ToString("F3"),
                    parts,
                });
            }

            return rows;
        }

        /// <summary>
        /// 表現ごとの記号。
        /// **パネルで OFF にしたもの (x) と、引き渡し / LOD でアルファ 0 に
        /// なっているもの (-) を区別する。** 同じ「見えない」でも原因が違う。
        /// </summary>
        static string Symbol(bool toggledOn, double alpha, string glyph)
        {
            if (!toggledOn) { return "x"; }
            return alpha > 0.0 ? glyph : "-";
        }

        /// <summary>**実効アルファが最大のものだけ。** 見えていないものは測らない。</summary>
        static Renderer PickVisible(CelestialBodyView view, bool onPoint, bool onProxy, bool onReal,
                                    double aPoint, double aProxy, double aReal, out bool useNear)
        {
            Renderer r = null;
            useNear = false;
            double best = 0.0;

            if (onReal && aReal > best) { r = view.RealMeshRenderer; best = aReal; useNear = true; }
            if (onProxy && aProxy > best) { r = view.MeshRenderer; best = aProxy; useNear = false; }
            if (onPoint && aPoint > best) { r = view.PointRenderer; best = aPoint; useNear = false; }

            return r;
        }

        Camera CameraFor(bool useNear)
        {
            if (_stack == null)
            {
                return null;
            }

            Camera cam = useNear ? _stack.Near : _stack.Deep;
            return cam != null ? cam : _stack.Deep;
        }

        /// <summary>
        /// 計算列。**実際のカメラと実際の画面高から出す。**
        ///
        /// UniverseRoot.RadiansPerPixel は 1080p / 60 度の**参照値**で、
        /// LOD の切替基準をウィンドウの大きさで動かさないための固定値。
        /// それを実解像度の実測と並べると、720p では 816 対 544 のように
        /// 常に食い違う (実機で確認)。**LOD 側は参照値のまま変えない。**
        ///
        /// AngularDiameterPixels の線形換算ではなく ProjectedDiameterPixels を使う。
        /// 画面いっぱいの大角度では線形換算が真のシルエットと 15% ずれる。
        /// </summary>
        string Projected(CelestialBodyView view, Camera cam)
        {
            if (cam == null || Screen.height <= 0)
            {
                return "---";
            }

            double focal = AngularSizeSolver.FocalLengthPixels(cam.fieldOfView, Screen.height);
            double px = AngularSizeSolver.ProjectedDiameterPixels(
                view.Body.RadiusKm, view.LastDistance, focal);

            return double.IsInfinity(px) ? "inf" : px.ToString("F2");
        }

        /// <summary>
        /// 実際に描かれているメッシュの**シルエット**の幅 x 高さ [px]。
        ///
        /// 描画器の AABB ではなく**頂点そのもの**を投影する。AABB の手前面の隅は
        /// 球のシルエットより外に出るので、近い天体では 1.5 倍ほど大きく出る
        /// (地球 1.6e4 units で 816 -> 1247 px)。箱ではなく形を測れば、
        /// 計算列と同じものを見ていることになる。
        ///
        /// **頂点は最大 <see cref="MaxSampledVertices"/> 点に間引く。**
        /// シルエットの最外点を取りこぼすことがあり、計算値より数 % 小さく出る。
        /// **数 % の差は正常。** 桁で違うときだけ疑う。
        ///
        /// 隅がカメラの後ろにあるときは測れないので --- を返す。
        /// 嘘の数字を並べるより、測れないことが分かるほうがよい。
        /// </summary>
        string MeasureSilhouette(Renderer r, Camera cam)
        {
            if (r == null || cam == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                return "---";
            }

            Vector3[] samples = GetSamples(r);
            if (samples == null || samples.Length == 0)
            {
                return "---";
            }

            Matrix4x4 toWorld = r.localToWorldMatrix;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < samples.Length; i++)
            {
                Vector3 p = cam.WorldToScreenPoint(toWorld.MultiplyPoint3x4(samples[i]));
                if (p.z <= 0f)
                {
                    return "---"; // カメラの後ろ。投影が破綻する
                }

                if (p.x < minX) { minX = p.x; }
                if (p.x > maxX) { maxX = p.x; }
                if (p.y < minY) { minY = p.y; }
                if (p.y > maxY) { maxY = p.y; }
            }

            return string.Format("{0:F0}x{1:F0}", maxX - minX, maxY - minY);
        }

        Vector3[] GetSamples(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return null;
            }

            int key = mesh.GetInstanceID();
            if (_sampleCache.TryGetValue(key, out Vector3[] cached))
            {
                return cached;
            }

            Vector3[] all = mesh.vertices;
            int stride = all.Length <= MaxSampledVertices
                ? 1
                : Mathf.CeilToInt(all.Length / (float)MaxSampledVertices);

            var picked = new List<Vector3>(MaxSampledVertices + 1);
            for (int i = 0; i < all.Length; i += stride)
            {
                picked.Add(all[i]);
            }

            Vector3[] result = picked.ToArray();
            _sampleCache[key] = result;
            return result;
        }

        GUIStyle _measure;

        /// <summary>基準フォントでの列の間隔 [px]。</summary>
        const float Gap = 16f;

        float TextWidth(string s) => _measure.CalcSize(new GUIContent(s)).x;

        /// <summary>
        /// 中身から幅を出す。**画面内に必ず収める**ための入力になる。
        /// 比例フォントなので、空白でパディングしても列は揃わない。
        /// 列ごとに幅を測って x を決める。
        /// </summary>
        float MeasureContent(IReadOnlyList<DebugItem> items, List<string[]> rows,
                             string[] headerLines, out float markW, out float labelW,
                             out float[] colW)
        {
            markW = TextWidth("> ");
            labelW = 0f;
            float valueW = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                labelW = Mathf.Max(labelW, TextWidth(items[i].Label));
                valueW = Mathf.Max(valueW, TextWidth(items[i].ValueText()));
            }

            colW = new float[BodyHeader.Length];
            for (int c = 0; c < BodyHeader.Length; c++)
            {
                colW[c] = TextWidth(BodyHeader[c]);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i];
                for (int c = 0; c < colW.Length && c < row.Length; c++)
                {
                    colW[c] = Mathf.Max(colW[c], TextWidth(row[c]));
                }
            }

            float bodyW = 0f;
            for (int c = 0; c < colW.Length; c++)
            {
                bodyW += colW[c];
            }

            bodyW += Gap * (colW.Length - 1);

            float widest = markW + labelW + Gap + valueW;
            widest = Mathf.Max(widest, bodyW);
            for (int i = 0; i < headerLines.Length; i++)
            {
                widest = Mathf.Max(widest, TextWidth(headerLines[i]));
            }

            // 端数と字送りのぶんだけ余裕を持たせる。ぴったりだと最後の文字が欠ける。
            return widest + 8f;
        }

        void OnGUI()
        {
            if (Model == null || !Model.IsOpen)
            {
                return;
            }

            if (_measure == null)
            {
                // **wordWrap を切る。** 既定の label は折り返すので、
                // CalcSize の幅ぴったりの Rect に描くと最後の 1〜2 文字が
                // 折り返して消える (1920x1080 の実機で見出し 3 行目が切れた)。
                _measure = new GUIStyle(GUI.skin.label)
                {
                    fontSize = DebugPanelLayoutSolver.MaxFontSize,
                    richText = false,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                };
            }

            string[] headerLines =
            {
                "=== F4 デバッグパネル ===",
                "上下=項目  左右=増減  Space=ON/OFF  R=全部リセット  F4=閉じる",
                "**開いている間は船の操作を止めています** (Space と R をパネルが使うため)",
            };

            IReadOnlyList<DebugItem> items = Model.Items;
            List<string[]> rows = BuildBodyRows();

            float baseWidth = MeasureContent(items, rows, headerLines,
                                             out float markW, out float labelW, out float[] colW);

            DebugPanelLayout layout = DebugPanelLayoutSolver.Solve(
                Screen.width, Screen.height,
                headerLines.Length, items.Count, rows.Count + 1,
                baseWidth, Model.Cursor);
            LastLayout = layout;

            EnsureDrawStyles(layout.FontSize);

            float k = layout.FontSize / (float)DebugPanelLayoutSolver.MaxFontSize;
            float pad = DebugPanelLayoutSolver.Padding;
            float x0 = DebugPanelLayoutSolver.Margin;
            float y0 = DebugPanelLayoutSolver.Margin;

            // **背景板。** 惑星の上に重なっても文字が読めるように。
            GUI.DrawTexture(new Rect(x0, y0, layout.Width, layout.Height), Backdrop());

            float x = x0 + pad;
            float y = y0 + pad;
            float lh = layout.LineHeight;
            float inner = layout.Width - pad * 2f;

            for (int i = 0; i < headerLines.Length; i++)
            {
                GUI.Label(new Rect(x, y, inner, lh), headerLines[i], _style);
                y += lh;
            }

            int first = layout.FirstItem;
            int last = first + layout.ItemCount;

            if (layout.Windowed)
            {
                GUI.Label(new Rect(x, y, inner, lh),
                          first > 0 ? string.Format("... 上に {0} 件", first) : "", _dimStyle);
                y += lh;
            }

            for (int i = first; i < last && i < items.Count; i++)
            {
                DebugItem item = items[i];
                bool onCursor = i == Model.Cursor;
                GUIStyle s = onCursor ? _cursorStyle : _style;

                if (onCursor)
                {
                    GUI.Label(new Rect(x, y, markW * k, lh), ">", s);
                }

                GUI.Label(new Rect(x + markW * k, y, labelW * k, lh), item.Label, s);
                float valueX = (markW + labelW + Gap) * k;
                GUI.Label(new Rect(x + valueX, y, inner - valueX, lh), item.ValueText(), s);
                y += lh;
            }

            if (layout.Windowed)
            {
                int rest = items.Count - last;
                GUI.Label(new Rect(x, y, inner, lh),
                          rest > 0 ? string.Format("... 下に {0} 件", rest) : "", _dimStyle);
                y += lh;
            }

            y += lh; // 天体表を離す

            DrawRow(x, y, k, colW, BodyHeader, _dimStyle, lh);
            y += lh;

            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(x, y, k, colW, rows[i], _style, lh);
                y += lh;
            }
        }

        void DrawRow(float x, float y, float k, float[] colW, string[] cells, GUIStyle s, float lh)
        {
            float cx = x;
            for (int c = 0; c < colW.Length; c++)
            {
                string text = c < cells.Length ? cells[c] : "";
                GUI.Label(new Rect(cx, y, colW[c] * k + Gap * k, lh), text, s);
                cx += (colW[c] + Gap) * k;
            }
        }

        void EnsureDrawStyles(int fontSize)
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    richText = false,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                };
                _style.normal.textColor = new Color(0.88f, 0.93f, 0.98f, 1f);

                _cursorStyle = new GUIStyle(_style);
                _cursorStyle.normal.textColor = new Color(1f, 0.85f, 0.3f, 1f);

                _dimStyle = new GUIStyle(_style);
                _dimStyle.normal.textColor = new Color(0.62f, 0.70f, 0.78f, 1f);
            }

            _style.fontSize = fontSize;
            _cursorStyle.fontSize = fontSize;
            _dimStyle.fontSize = fontSize;
        }

        Texture2D Backdrop()
        {
            if (_backdrop == null)
            {
                _backdrop = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
                _backdrop.Apply();
                _backdrop.hideFlags = HideFlags.HideAndDontSave;
            }

            return _backdrop;
        }
    }
}
