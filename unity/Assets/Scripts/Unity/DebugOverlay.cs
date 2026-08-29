using System.Text;
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

        /// <summary>起動引数。付けると HUD を出した状態で始まる (Step 8-0)。</summary>
        public const string DebugHudArg = "-debugHud";

        [SerializeField] UniverseRoot _root;
        [SerializeField] ShipRig _rig;
        [SerializeField] ScenarioRunner _scenario;

        GUIStyle _style;
        GUIStyle _checkStyle;

        /// <summary>
        /// HUD を出すか (Step 8-0)。**既定は非表示。** F1 で反転する。
        /// 起動引数 -debugHud が付いていれば初期表示。
        /// </summary>
        public bool Visible { get; set; }

        /// <summary>
        /// F4 パネルが開いている間だけ立つ。**左側の HUD だけを隠す。**
        /// パネルは左上に出るので場所が競合する。同時に両方は要らない。
        /// **右上のシナリオ確認項目は隠さない。** パネルで値を触りながら
        /// そのシナリオの確認項目を読めないと意味がない。
        /// </summary>
        public bool SuppressMainHud { get; set; }

        public void Bind(UniverseRoot root, ShipRig rig)
        {
            _root = root;
            _rig = rig;
        }

        public void BindScenario(ScenarioRunner scenario) => _scenario = scenario;

        /// <summary>どの定義で組まれたか (Step 11-0c)。**実機で取り違えに気づくため。**</summary>
        [SerializeField] CockpitIdentity _cockpit;

        /// <summary>判定リグ (Step 13-3b)。**-stationJudge のときだけ行が増える。**</summary>
        [SerializeField] StationJudgeRig _judge;

        public void BindJudge(StationJudgeRig judge) => _judge = judge;

        public void BindCockpit(CockpitIdentity cockpit) => _cockpit = cockpit;

        void Awake()
        {
            // 既定は非表示。引数が付いていれば出した状態で始まる。
            Visible = HasDebugHudArg();
        }

        public static bool HasDebugHudArg()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (string.Equals(a, DebugHudArg, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>F1。表示を反転する。</summary>
        public void Toggle() => Visible = !Visible;

        /// <summary>画面右上に出す確認項目。シナリオが無ければ空。</summary>
        public string BuildCheckText()
        {
            if (_scenario == null || !_scenario.IsActive)
            {
                return string.Empty;
            }

            SolarSystem.Core.Scenario s = _scenario.Current;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{_scenario.Index + 1}/{_scenario.Count}] {s.Name}");
            sb.AppendLine("F2 次 / F3 前");
            foreach (string line in s.CheckPoints)
            {
                sb.AppendLine($"  - {line}");
            }

            return sb.ToString().TrimEnd();
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

            // **フレーム時間 (Step 13-0b)。** 埋まっていなければ「---」を出す。
            // 0 ms と書くと「速い」と誤読されるので、数字にしない。
            sb.AppendLine(FrameTimeProbe.Enabled
                ? (FrameTimeProbe.HasLatest
                    ? $"フレーム : CPU {FrameTimeProbe.LatestCpuMs:0.00} ms / GPU {FrameTimeProbe.LatestGpuMs:0.00} ms"
                    : "フレーム : --- (FrameTimingManager が埋まっていない)")
                : "フレーム : 計測 OFF (-frameTimeOff)");
            SpaceStation station = _rig.TargetStation(_root.Model);
            if (station != null)
            {
                double sd = station.DistanceFrom(_root.Ship.Position);
                sb.AppendLine($"目標     : [{_rig.TargetIndex}] {station.Name}  距離 {sd:E3} units  " +
                              $"(Tab で切替)");
                sb.AppendLine($"ドック   : {_rig.Docking.State}  進行 {_rig.Docking.Progress:0.00}  " +
                              $"ずれ角 {_rig.LastAlignmentAngle:0.0} 度  (Enter=要求 / BackSpace=出港)");
                if (!string.IsNullOrEmpty(_rig.Docking.LastRejection))
                {
                    sb.AppendLine($"要求NG   : {_rig.Docking.LastRejection}");
                }
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

            // レンズフレアの遮蔽 (Step 9-3a)。
            // 「なぜ今フレアが出ていないのか」を目視で切り分けられるようにする。
            SunFlareController flare = _root.SunFlare;
            if (flare != null)
            {
                sb.AppendLine($"FLARE    : occl {flare.LastOcclusion:0.00}  int {flare.LastIntensity:0.00}  " +
                              $"(基準 {flare.Base:0.00})");
            }

            if (_cockpit != null)
            {
                sb.AppendLine($"コックピット: {_cockpit.Describe()}");
            }

            AppendJudge(sb);

            // **音は「鳴ったかどうか」を目で確かめられない (CLAUDE.md 0-B)。**
            // 聴き分けに自信が持てないときに、画面で裏を取れるようにする。
            AudioRouting audio = _root.Audio;
            if (audio != null)
            {
                sb.AppendLine(
                    $"音       : eng {audio.LastEngineVolume:0.000} p{audio.LastEnginePitch:0.00}  " +
                    $"cut {audio.LastCutoffHz:0} Hz  " +
                    $"直近 {audio.LastSound} vol {audio.LastSoundVolume:0.000} " +
                    $"(計 {audio.TotalPlayCount} 回)");
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
            // F1 オフのときは確認項目ごと消す。
            // これで「デモ」ではなく「ゲーム画面」のスクショが撮れる。
            if (!Visible)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    richText = false,
                };
                _style.normal.textColor = Color.white;
            }

            if (!SuppressMainHud)
            {
                // **枠は画面いっぱいに取る (Step 13-3b で直した)。**
                // 900x220 の固定枠だったので、行が増えるたびに下が黙って切れていた
                // （実測: FLARE / コックピット / 音 / 操作 の 4 行が出ていなかった）。
                // **測定器が計算した値を表示できていない**状態なので直す。
                GUI.Label(new Rect(12f, 12f, Screen.width - 24f, Screen.height - 24f),
                          BuildText(), _style);
            }

            string check = BuildCheckText();
            if (string.IsNullOrEmpty(check))
            {
                return;
            }

            if (_checkStyle == null)
            {
                _checkStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    richText = false,
                    alignment = TextAnchor.UpperRight,
                };
                _checkStyle.normal.textColor = new Color(0.75f, 0.95f, 0.8f, 1f);
            }

            GUI.Label(new Rect(Screen.width - 512f - 12f, 12f, 512f, 160f), check, _checkStyle);
        }

        /// <summary>
        /// **判定ビューの数値 (Step 13-3b)。**
        /// 接続面と Scale を決めた後に「その値がどういう数字だったか」を
        /// 数表へ残せるようにするための行。**-stationJudge のときだけ出る。**
        /// </summary>
        void AppendJudge(StringBuilder sb)
        {
            if (_judge == null || !_judge.gameObject.activeSelf)
            {
                return;
            }

            double scale = _judge.Scale;

            sb.AppendLine($"判定     : Scale {scale:F5}  視点 {_judge.Viewpoint}  "
                          + $"距離 {_judge.LastDistanceUnits:F5} units"
                          + (_judge.HasModel ? "" : "  **モデル無し**"));
            sb.AppendLine($"  全長   : {StationJudge.ToMeters(StationJudge.StationLengthMeters, scale):F2} m"
                          + $"  全幅 {StationJudge.ToMeters(StationJudge.StationWidthMeters, scale):F2} m"
                          + $"  半径 {StationJudge.RadiusUnits(scale):F5} units"
                          + $"  MinStandoff {StationJudge.MinStandoffUnits(scale):F5} units");

            AppendCandidate(sb, "(a) 金の円 ", PortFaceCandidate.GoldDisc, scale);
            AppendCandidate(sb, "(b) 突円盤 ", PortFaceCandidate.ProtrudingPlate, scale);
            AppendCandidate(sb, "(c) 胴     ", PortFaceCandidate.ModuleBody, scale);

            const double closeDistance = 0.5;
            sb.AppendLine($"  station-close (D={closeDistance} units / "
                          + $"{StationJudge.ReferenceWidth}x{StationJudge.ReferenceHeight} / "
                          + $"画角 {StationJudge.ReferenceFovDegrees:F0} 度) : "
                          + $"角直径 {StationJudge.AngularDiameterDegrees(scale, closeDistance):F1} 度  "
                          + $"被覆 {StationJudge.CoveragePercent(scale, closeDistance):F1} %");
            sb.AppendLine($"  船の全幅 {StationJudge.ShipWidthMeters:F4} m / "
                          + $"物差し {StationJudge.RatioMin:F1}〜{StationJudge.RatioMax:F1} 倍"
                          + "   輪の色: (a) 金 / (b) 青 / (c) 桃");
        }

        static void AppendCandidate(StringBuilder sb, string label,
                                    PortFaceCandidate candidate, double scale)
        {
            StationJudge.ScaleRangeFor(candidate, out double lo, out double hi);

            sb.AppendLine($"  {label}: {StationJudge.OpeningMeters(candidate, scale):F3} m  "
                          + $"船幅比 {StationJudge.RatioToShipWidth(candidate, scale):F2}"
                          + (StationJudge.WithinRule(candidate, scale) ? " [物差し内]" : "")
                          + $"  物差しを満たす Scale {lo:F5}〜{hi:F5}");
        }
    }
}
