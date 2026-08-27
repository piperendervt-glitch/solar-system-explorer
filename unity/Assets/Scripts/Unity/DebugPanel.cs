using System.Collections.Generic;
using System.Text;
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
    /// 人が exe で見るための道具なので、それでよい。
    ///
    /// Update() を持たない。UniverseRoot.Tick から呼ばれる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugPanel : MonoBehaviour
    {
        [SerializeField] UniverseRoot _root;
        [SerializeField] ShipRig _rig;
        [SerializeField] DebugPanelApplier _applier;
        [SerializeField] CameraStackController _stack;

        GUIStyle _style;
        GUIStyle _cursorStyle;

        public DebugPanelModel Model { get; private set; }

        public bool IsOpen => Model != null && Model.IsOpen;

        public void Bind(UniverseRoot root, ShipRig rig, DebugPanelApplier applier,
                         CameraStackController stack)
        {
            _root = root;
            _rig = rig;
            _applier = applier;
            _stack = stack;
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
                CockpitShake.MaxAmplitudeRadians);
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

        /// <summary>天体ごとの 1 行。計算値と実測を並べる。</summary>
        public string BuildBodyLines()
        {
            var sb = new StringBuilder();
            sb.AppendLine("天体      距離[units]  角直径(計算)  bbox(実測)   引き渡し  表現");

            if (_root == null || _root.SolarSystem == null)
            {
                return sb.ToString();
            }

            foreach (CelestialBodyView view in _root.SolarSystem.Views)
            {
                if (view == null || view.Body == null)
                {
                    continue;
                }

                string parts = (view.Lod.PointActive ? "点" : "-")
                               + (view.Lod.MeshActive ? "殻" : "-")
                               + (view.RealScaleBlend > 0.0 ? "実" : "-");

                sb.AppendLine(string.Format(
                    "{0,-8} {1,11:E3} {2,12:F2} {3,12} {4,8:F3}  {5}",
                    view.Body.Name,
                    view.LastDistance,
                    view.LastAngularPixels,
                    MeasureBbox(view),
                    view.RealScaleBlend,
                    parts));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 画面上の実測 bbox の幅 [px]。
        /// **隅がカメラの後ろにあるときは測れないので --- を返す。**
        /// 嘘の数字を並べるより、測れないことが分かるほうがよい。
        /// </summary>
        string MeasureBbox(CelestialBodyView view)
        {
            Renderer r = view.RealScaleBlend > 0.0 ? view.RealMeshRenderer
                       : view.Lod.MeshActive ? view.MeshRenderer
                       : view.PointRenderer;

            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || _stack == null)
            {
                return "---";
            }

            Camera cam = view.RealScaleBlend > 0.0 ? _stack.Near : _stack.Deep;
            if (cam == null)
            {
                return "---";
            }

            Bounds b = r.bounds;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);

                Vector3 p = cam.WorldToScreenPoint(corner);
                if (p.z <= 0f)
                {
                    return "---"; // カメラの後ろ。投影が破綻する
                }

                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            return string.Format("{0:F0}x{1:F0}", maxX - minX, maxY - minY);
        }

        void OnGUI()
        {
            if (Model == null || !Model.IsOpen)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
                _style.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 1f);

                _cursorStyle = new GUIStyle(_style);
                _cursorStyle.normal.textColor = new Color(1f, 0.9f, 0.4f, 1f);
            }

            const float width = 620f;
            float x = Screen.width - width - 12f;

            var sb = new StringBuilder();
            sb.AppendLine("=== F4 デバッグパネル ===");
            sb.AppendLine("上下=項目  左右=増減  Space=ON/OFF  R=全部リセット  F4=閉じる");
            sb.AppendLine("**開いている間は船の操作を止めています** (Space と R をパネルが使うため)");
            sb.AppendLine();
            GUI.Label(new Rect(x, 12f, width, 90f), sb.ToString(), _style);

            float y = 100f;
            IReadOnlyList<DebugItem> items = Model.Items;
            for (int i = 0; i < items.Count; i++)
            {
                DebugItem item = items[i];
                string mark = i == Model.Cursor ? ">" : " ";
                string line = string.Format("{0} {1,-22} {2}", mark, item.Label, item.ValueText());
                GUI.Label(new Rect(x, y, width, 18f), line, i == Model.Cursor ? _cursorStyle : _style);
                y += 17f;
            }

            y += 10f;
            GUI.Label(new Rect(x, y, width, 120f), BuildBodyLines(), _style);
        }
    }
}
