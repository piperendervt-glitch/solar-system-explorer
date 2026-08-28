using System.Collections.Generic;
using SolarSystem.Core;
using TMPro;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// コックピットの計器 (Step 4 / docs/01-architecture.md §5)。
    ///
    /// 専用の Orthographic カメラで TextMeshPro を RenderTexture へ描き、
    /// その RT をコックピットのパネル面に貼る。
    ///
    /// **更新は 10 Hz。** TMP の SetText は毎回メッシュを作り直すので、
    /// 60 Hz で 4 項目を無条件に更新すると静止中でも無駄なリビルドが走る (§5-2)。
    /// 値が変わったときだけ SetText を呼ぶ。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InstrumentPanel : MonoBehaviour
    {
        public const double UpdateIntervalSeconds = 0.1; // 10 Hz

        /// <summary>
        /// 表示する値の種類 (Step 11-3a)。
        /// **同じ値を複数の面に出せる**ようにするため、種類ごとにラベルの一覧を持つ。
        /// 案 A と B の両方の面を同時に組んであるので、値は両方へ流れる
        /// （描かれるのは選ばれている側だけ）。
        /// </summary>
        public enum Field
        {
            Speed, Distance, Eta, Target, Alignment, Docking, Dial, Autopilot, Warning,
        }

        // **Dictionary は直列化できない**ので、2 本の List を添え字で対応させる。
        [SerializeField] List<Field> _fields = new List<Field>();
        [SerializeField] List<TMP_Text> _labels = new List<TMP_Text>();

        double _nextUpdateAt;

        public string LastSpeedText { get; private set; } = string.Empty;
        public string LastDistanceText { get; private set; } = string.Empty;
        public string LastEtaText { get; private set; } = string.Empty;
        public string LastTargetText { get; private set; } = string.Empty;
        public string LastAlignmentText { get; private set; } = string.Empty;

        /// <summary>整列が許容角以内か。表示色を変えるのに使う。</summary>
        public bool AlignmentInTolerance { get; private set; }

        /// <summary>SetText を実際に呼んだ回数。10 Hz に落ちているかの検証用。</summary>
        public int RebuildCount { get; private set; }

        public string LastDockingText { get; private set; } = string.Empty;
        public string LastDialText { get; private set; } = string.Empty;
        public string LastAutopilotText { get; private set; } = string.Empty;
        /// <summary>
        /// 警告灯の直近の表記。**初期値は消灯そのもの。**
        /// 空文字から始めると、警告が無くても初回の Tick で 1 回書き込みが走り、
        /// 「値が変わったときだけ SetText」が崩れる（EditMode テストが検出した）。
        /// </summary>
        public string LastWarningText { get; private set; } = WarningOffText;

        /// <summary>
        /// 種類ごとの**起こりうる最長の表示**（Step 11-3b）。
        ///
        /// ■ なぜ要るか（実機で踏んだ）
        /// TMP の自動縮小に任せると、**文字列が変わるたびに大きさが跳ぶ。**
        /// 実測では HUD の整列表示が "8.2 deg" で 146.8px、"12.3 deg ALIGNED" で
        /// 64.5px と **2.3 倍**も動いていた。視線を動かすと整列角が変わり続けるので、
        /// 10 Hz ごとに字の大きさが跳ね、**文字が波打って見えていた。**
        ///
        /// **最長の文字列で入る大きさに固定する。** そうすれば跳ねないし、はみ出さない。
        /// </summary>
        public static string LongestSample(Field field)
        {
            switch (field)
            {
                // **速度は「実際に出せる最大」から決める。**
                // 表示は 1.0 km/s (ArrivalMaxSpeedKmPerSec) 未満なら "0.000 km/s"、
                // それ以上なら光速比 "0.000 c" に切り替わる。ダイヤルの最大は
                // beta 0.9 (= 269813.2 km/s) なので "0.900 c"（7 文字）にしかならない。
                // **長いのは km/s 側の "0.999 km/s"（10 文字）。**
                case Field.Speed: return "0.999 km/s";
                case Field.Distance: return "1.234e+10 km";
                case Field.Eta: return "--:--:--";
                case Field.Target: return "MARS STATION";
                case Field.Alignment: return "180.0 deg";
                // ドッキングの 6 状態のうち最長は APPROACHING。
                case Field.Docking: return "APPROACHING";
                case Field.Dial: return "8/8";
                case Field.Autopilot: return "OFF";
                case Field.Warning: return WarningOnText;
                default: return "----------";
            }
        }

        /// <summary>
        /// ドッキング状態の表記 (Step 11-3b)。
        /// **`DockRequested` だけ 2 語に割る。** `DOCKREQUESTED` は読みにくい。
        /// </summary>
        public static string FormatDocking(DockingState state)
            => state == DockingState.DockRequested
                ? "DOCK REQ"
                : state.ToString().ToUpperInvariant();

        /// <summary>ラベルを 1 枚足す。**同じ種類を何枚でも足せる。**</summary>
        public void Register(Field field, TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            _fields.Add(field);
            _labels.Add(label);
        }

        /// <summary>箱コックピットの帯 (Step 4)。5 項目をまとめて登録する。</summary>
        public void Bind(TMP_Text speed, TMP_Text distance, TMP_Text eta, TMP_Text targetName,
                         TMP_Text alignment = null)
        {
            Register(Field.Speed, speed);
            Register(Field.Distance, distance);
            Register(Field.Eta, eta);
            Register(Field.Target, targetName);
            Register(Field.Alignment, alignment);
        }

        /// <summary>
        /// ポート正面からのずれ角の表記 (Step 6 / 11-3b で短縮)。
        ///
        /// **"ALIGNED" は外した。** 許容内かどうかは**色**（緑 / 橙）で出ており、
        /// 文字と重複していた。外したことで案 A の HUD の字が実測 19.8px -> 40.4px
        /// （2.04 倍）になる。幅で大きさが決まる面だったため。
        /// 許容の判定そのものは `AlignmentInTolerance` に残っている。
        /// </summary>
        public static string FormatAlignment(double angleDegrees)
        {
            bool ok = angleDegrees <= DockingSolver.AttitudeToleranceDegrees;
            return $"{angleDegrees:0.0} deg";
        }

        /// <summary>
        /// 速度の表記。**1 km/s 未満は km/s で出す。**
        /// 0.0000033c と出しても計器として意味をなさないため (決定 D-11 と同じ理由)。
        /// </summary>
        public static string FormatSpeed(double kmPerSec)
        {
            if (kmPerSec < UniverseConstants.ArrivalMaxSpeedKmPerSec)
            {
                return $"{kmPerSec:0.000} km/s";
            }

            return $"{UniverseConstants.KmPerSecToBeta(kmPerSec):0.000} c";
        }

        public static string FormatDistance(double units)
        {
            if (units < 1000.0)
            {
                return $"{units:0.0} km";
            }

            return $"{units:0.000e+0} km";
        }

        /// <summary>接近していないときは --:--:-- (決定 D-13)。</summary>
        public static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                return "--:--:--";
            }

            var span = System.TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 100.0)
            {
                return "--:--:--";
            }

            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        /// <summary>10 Hz で更新する。elapsedSeconds は Core の積算時間 (決定 D-24)。</summary>
        public void Tick(double elapsedSeconds, double speedKmPerSec, double distanceUnits,
                         double etaSeconds, string targetName,
                         string dockingText = null, string dialText = null,
                         string autopilotText = null, bool warning = false)
        {
            if (elapsedSeconds < _nextUpdateAt)
            {
                return;
            }

            _nextUpdateAt = elapsedSeconds + UpdateIntervalSeconds;

            // 値が変わったときだけ SetText を呼ぶ。TMP のメッシュ再構築を減らす。
            LastSpeedText = Apply(Field.Speed, LastSpeedText, FormatSpeed(speedKmPerSec));
            LastDistanceText = Apply(Field.Distance, LastDistanceText, FormatDistance(distanceUnits));
            LastEtaText = Apply(Field.Eta, LastEtaText, FormatEta(etaSeconds));
            LastTargetText = Apply(Field.Target, LastTargetText,
                targetName != null ? targetName.ToUpperInvariant() : "---");

            // 11-3a で足した面ぶん。**渡されなければ触らない**（箱の帯は 5 項目のまま）。
            if (dockingText != null)
            {
                LastDockingText = Apply(Field.Docking, LastDockingText, dockingText);
            }

            if (dialText != null)
            {
                LastDialText = Apply(Field.Dial, LastDialText, dialText);
            }

            if (autopilotText != null)
            {
                LastAutopilotText = Apply(Field.Autopilot, LastAutopilotText, autopilotText);
            }

            string warningText = warning ? WarningOnText : WarningOffText;
            if (warningText != LastWarningText)
            {
                LastWarningText = Apply(Field.Warning, LastWarningText, warningText);
                SetColor(Field.Warning, warning
                    ? new Color(0.95f, 0.45f, 0.35f, 1f)
                    : new Color(0.30f, 0.40f, 0.40f, 1f));
            }
        }

        /// <summary>
        /// 警告灯の表記 (Step 11-3a の案 B)。整列が許容外のときに点く。
        /// **飾りを外して 5 文字にした (11-3b)。** 幅で字の大きさが決まる面なので、
        /// `!! ALIGN !!` だと 18.0px、`ALIGN` なら 28.6px（実測 1.59 倍）。
        /// </summary>
        public const string WarningOnText = "ALIGN";
        public const string WarningOffText = "----";

        /// <summary>整列の表示だけを更新する。Tick と同じ 10 Hz。</summary>
        public void SetAlignment(double angleDegrees)
        {
            AlignmentInTolerance = angleDegrees <= DockingSolver.AttitudeToleranceDegrees;
            string next = FormatAlignment(angleDegrees);
            if (next == LastAlignmentText)
            {
                return;
            }

            LastAlignmentText = next;
            RebuildCount++;
            SetText(Field.Alignment, next);
            SetColor(Field.Alignment, AlignmentInTolerance
                ? new Color(0.55f, 0.95f, 0.75f, 1f)
                : new Color(0.95f, 0.70f, 0.40f, 1f));
        }

        void SetText(Field field, string value)
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                if (_fields[i] == field && _labels[i] != null)
                {
                    _labels[i].SetText(value);
                }
            }
        }

        void SetColor(Field field, Color color)
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                if (_fields[i] == field && _labels[i] != null)
                {
                    _labels[i].color = color;
                }
            }
        }

        /// <summary>
        /// 変わっていれば反映して数える。ラベル未バインドでも数えるので、
        /// TMP 抜きの EditMode テストから更新頻度を検証できる。
        /// </summary>
        string Apply(Field field, string previous, string next)
        {
            if (previous == next)
            {
                return previous;
            }

            RebuildCount++;
            SetText(field, next);
            return next;
        }

        public void ResetForTest()
        {
            _nextUpdateAt = 0.0;
            RebuildCount = 0;
        }
    }
}
