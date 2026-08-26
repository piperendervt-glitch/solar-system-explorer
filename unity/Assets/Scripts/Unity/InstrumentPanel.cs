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

        [SerializeField] TMP_Text _speed;
        [SerializeField] TMP_Text _distance;
        [SerializeField] TMP_Text _eta;
        [SerializeField] TMP_Text _targetName;
        [SerializeField] TMP_Text _alignment;

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

        public void Bind(TMP_Text speed, TMP_Text distance, TMP_Text eta, TMP_Text targetName,
                         TMP_Text alignment = null)
        {
            _speed = speed;
            _distance = distance;
            _eta = eta;
            _targetName = targetName;
            _alignment = alignment;
        }

        /// <summary>
        /// ポート正面からのずれ角の表記 (Step 6)。
        /// 許容角 30 度 (決定 D-18) 以内なら "ALIGNED" を添える。
        /// これが無いと、要求が通らない理由が画面から分からない。
        /// </summary>
        public static string FormatAlignment(double angleDegrees)
        {
            bool ok = angleDegrees <= DockingSolver.AttitudeToleranceDegrees;
            return ok ? $"{angleDegrees:0.0} deg  ALIGNED" : $"{angleDegrees:0.0} deg";
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
                         double etaSeconds, string targetName)
        {
            if (elapsedSeconds < _nextUpdateAt)
            {
                return;
            }

            _nextUpdateAt = elapsedSeconds + UpdateIntervalSeconds;

            // 値が変わったときだけ SetText を呼ぶ。TMP のメッシュ再構築を減らす。
            LastSpeedText = Apply(_speed, LastSpeedText, FormatSpeed(speedKmPerSec));
            LastDistanceText = Apply(_distance, LastDistanceText, FormatDistance(distanceUnits));
            LastEtaText = Apply(_eta, LastEtaText, FormatEta(etaSeconds));
            LastTargetText = Apply(_targetName, LastTargetText,
                targetName != null ? targetName.ToUpperInvariant() : "---");
        }

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
            if (_alignment != null)
            {
                _alignment.SetText(next);
                _alignment.color = AlignmentInTolerance
                    ? new Color(0.55f, 0.95f, 0.75f, 1f)
                    : new Color(0.95f, 0.70f, 0.40f, 1f);
            }
        }

        /// <summary>
        /// 変わっていれば反映して数える。ラベル未バインドでも数えるので、
        /// TMP 抜きの EditMode テストから更新頻度を検証できる。
        /// </summary>
        string Apply(TMP_Text label, string previous, string next)
        {
            if (previous == next)
            {
                return previous;
            }

            RebuildCount++;
            if (label != null)
            {
                label.SetText(next);
            }

            return next;
        }

        public void ResetForTest()
        {
            _nextUpdateAt = 0.0;
            RebuildCount = 0;
        }
    }
}
