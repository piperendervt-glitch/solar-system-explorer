using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 画面隅の最小限のデバッグ表示 (Step 3a)。
    ///
    /// **Step 4 の計器 (RenderTexture + TextMeshPro) とは別物。**
    /// これは開発中に数値を目で追うためだけのもので、製品の画面には出さない。
    /// OnGUI は毎フレーム GC を出すが、デバッグ用途なので気にしない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        const float NearClip = CameraStackController.DeepNearClip;

        [SerializeField] UniverseRoot _root;
        [SerializeField] ShipRig _rig;

        GUIStyle _style;

        public void Bind(UniverseRoot root, ShipRig rig)
        {
            _root = root;
            _rig = rig;
        }

        /// <summary>表示内容を組み立てる。OnGUI とテストの両方から使う。</summary>
        public string BuildText()
        {
            if (_root == null || _root.Model == null || _rig == null)
            {
                return "(未初期化)";
            }

            CelestialBodyView mars = _root.SolarSystem != null ? _root.SolarSystem.Find("Mars") : null;
            double distance = _root.Model.Mars.DistanceFrom(_root.Ship.Position);
            double speed = _root.Ship.SpeedKmPerSec;

            string mode = "-";
            string angular = "-";
            if (mars != null)
            {
                mode = mars.Lod.MeshActive
                    ? (mars.Lod.PointActive ? $"クロスフェード {mars.Lod.Blend:0.00}" : "メッシュ")
                    : "光点";
                angular = $"{mars.LastAngularPixels:0.###} px";
            }

            bool clipping = DebugJumpTable.ClipsNearPlane(SolarSystemModel.MarsRadiusKm, distance, NearClip);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"ダイヤル : {_rig.Dial.Current.Label}  [{_rig.Dial.Index}/{SpeedDial.Steps.Count - 1}]  R/F で増減");
            sb.AppendLine($"速度     : {speed:0.###} km/s  ({UniverseConstants.KmPerSecToBeta(speed):0.####} c)  スラスト {_rig.LastThrust:+0.0;-0.0; 0.0}");
            sb.AppendLine($"火星まで : {distance:E4} units");
            sb.AppendLine($"表示     : {mode}   角直径 {angular}");
            SpaceStation station = _rig.TargetStation(_root.Model);
            if (station != null)
            {
                double sd = station.DistanceFrom(_root.Ship.Position);
                sb.AppendLine($"目標     : [{_rig.TargetIndex}] {station.Name}  距離 {sd:E3} units  " +
                              $"(Tab で切替)");
                sb.AppendLine($"ドック   : {_rig.Docking.State}  進行 {_rig.Docking.Progress:0.00}  " +
                              $"(Enter=要求 / BackSpace=出港)");
            }

            AutopilotSolver ap = _rig.Autopilot;
            string eta = double.IsInfinity(ap.EtaSeconds)
                ? "--:--"
                : $"{(int)(ap.EtaSeconds / 60):00}:{ap.EtaSeconds % 60:00.0}";
            sb.AppendLine($"AP       : {ap.State}  指令 {ap.CommandedSpeedKmPerSec:0.###} km/s  " +
                          $"残り {ap.RemainingDistance:E3} units  制動距離 {ap.BrakeDistance:E3}  ETA {eta}");

            if (mars != null)
            {
                sb.AppendLine($"実スケール: 引き渡し率 {mars.RealScaleBlend:0.00}  " +
                              $"(帯 {RealScaleHandoff.FadeStartDistance:E0} -> {RealScaleHandoff.FadeEndDistance:E0} units)");
            }

            sb.Append($"操作     : W/S/A/D=ピッチ/ヨー  Q/E=ロール  Space=前進  1〜8=ジャンプ  T=AP起動  G=AP解除");

            if (clipping)
            {
                sb.AppendLine();
                sb.Append($"警告     : プロキシ殻の手前が near={NearClip} を切っている (火星の下限 6777 units)");
            }

            return sb.ToString();
        }

        void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    richText = false,
                };
                _style.normal.textColor = Color.white;
            }

            GUI.Label(new Rect(12f, 12f, 900f, 200f), BuildText(), _style);
        }
    }
}
