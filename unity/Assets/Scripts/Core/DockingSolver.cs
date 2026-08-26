using System;

namespace SolarSystem.Core
{
    public enum DockingState
    {
        Free,
        Approaching,
        DockRequested,
        Docking,
        Docked,
        Undocking,
    }

    /// <summary>
    /// ドッキングの状態機械 (Step 5 / docs/01-architecture.md §6)。
    ///
    /// **判定は Unity の Trigger を使わず、double の解析判定で行う (決定 D-16)。**
    /// 巡航中は 1 フレームに 4497 units 進むので、離散判定の Trigger は原理的に
    /// すり抜ける (§0-2)。
    ///
    /// 到着圏は 20 units 以内かつ 1 km/s 以下 (決定 D-10)。
    /// 補間は 5 秒 (決定 D-17)、姿勢の許容角は 30 度 (決定 D-18)。
    /// </summary>
    public sealed class DockingSolver
    {
        /// <summary>ドッキング補間にかける時間 [s] (決定 D-17)。</summary>
        public const double DockSeconds = 5.0;

        /// <summary>ポート正面からの許容角 [deg] (決定 D-18)。</summary>
        public const double AttitudeToleranceDegrees = 30.0;

        /// <summary>Approaching から抜ける距離の倍率。境界での振動を防ぐ。</summary>
        public const double LeaveMultiplier = 1.2;

        /// <summary>Undocking を終える距離 [units]。</summary>
        public const double UndockDistanceUnits = UniverseConstants.ArrivalRadiusUnits * LeaveMultiplier;

        public DockingState State { get; private set; } = DockingState.Free;

        /// <summary>補間の進み 0..1。Docking / Undocking のときだけ動く。</summary>
        public double Progress { get; private set; }

        /// <summary>直近に評価した目標までの距離 [units]。</summary>
        public double LastDistance { get; private set; }

        public bool IsDocked => State == DockingState.Docked;

        /// <summary>船の操縦を state machine が握っているか。true の間は手動入力を無視する。</summary>
        public bool ControlsShip => State == DockingState.Docking || State == DockingState.Undocking;

        /// <summary>直近の要求が通らなかった理由 (通ったら空)。Step 6 で計器に出す。</summary>
        public string LastRejection { get; private set; } = string.Empty;

        /// <summary>
        /// ドッキング要求が今この場で通るかを判定し、通らないなら理由を返す。
        /// 距離 -> 速度 -> 角度 の順に見る (遠ければ速度も角度も意味がないため)。
        /// </summary>
        public static string RejectionReason(double distance, double speedKmPerSec, double angleDegrees)
        {
            if (distance > UniverseConstants.ArrivalRadiusUnits)
            {
                return $"距離が遠い ({distance:0.0} > {UniverseConstants.ArrivalRadiusUnits:0} units)";
            }

            if (speedKmPerSec > UniverseConstants.ArrivalMaxSpeedKmPerSec)
            {
                return $"速度が高い ({speedKmPerSec:0.000} > {UniverseConstants.ArrivalMaxSpeedKmPerSec:0} km/s)";
            }

            if (angleDegrees > AttitudeToleranceDegrees)
            {
                return $"ポート正面からずれている ({angleDegrees:0.0} > {AttitudeToleranceDegrees:0} 度)";
            }

            return string.Empty;
        }

        /// <summary>
        /// 1 ステップ進める。
        /// </summary>
        /// <param name="distanceToStation">ステーション中心までの距離 [units]。</param>
        /// <param name="speedKmPerSec">船の速さ [km/s]。</param>
        /// <param name="attitudeAngleDegrees">機首とポート正面のなす角 [deg]。</param>
        /// <param name="requestPressed">ドッキング要求 (立ち上がり済み)。</param>
        /// <param name="undockPressed">出港要求 (立ち上がり済み)。</param>
        /// <param name="deltaSeconds">経過時間 [s]。</param>
        public void Step(double distanceToStation, double speedKmPerSec, double attitudeAngleDegrees,
                         bool requestPressed, bool undockPressed, double deltaSeconds)
        {
            LastDistance = distanceToStation;

            // ドッキングに至っていない間は、何が足りないのかを常に持っておく。
            // Enter を押した瞬間だけだと、姿勢を直している最中に表示が消えてしまう。
            if (State == DockingState.Free || State == DockingState.Approaching
                || State == DockingState.DockRequested)
            {
                LastRejection = RejectionReason(distanceToStation, speedKmPerSec, attitudeAngleDegrees);
            }
            else
            {
                LastRejection = string.Empty;
            }

            bool inRange = distanceToStation <= UniverseConstants.ArrivalRadiusUnits
                           && speedKmPerSec <= UniverseConstants.ArrivalMaxSpeedKmPerSec;

            switch (State)
            {
                case DockingState.Free:
                    if (inRange)
                    {
                        State = DockingState.Approaching;
                    }

                    break;

                case DockingState.Approaching:
                    // ヒステリシス: 20 units で入って 24 units で出る。
                    if (distanceToStation > UniverseConstants.ArrivalRadiusUnits * LeaveMultiplier)
                    {
                        State = DockingState.Free;
                    }
                    else if (requestPressed)
                    {
                        State = DockingState.DockRequested;
                    }

                    break;

                case DockingState.DockRequested:
                    if (undockPressed)
                    {
                        State = DockingState.Approaching;
                    }
                    else if (speedKmPerSec <= UniverseConstants.ArrivalMaxSpeedKmPerSec
                             && attitudeAngleDegrees <= AttitudeToleranceDegrees)
                    {
                        State = DockingState.Docking;
                        Progress = 0.0;
                    }

                    break;

                case DockingState.Docking:
                    Progress = Math.Min(1.0, Progress + deltaSeconds / DockSeconds);
                    if (Progress >= 1.0)
                    {
                        State = DockingState.Docked;
                    }

                    break;

                case DockingState.Docked:
                    if (undockPressed)
                    {
                        State = DockingState.Undocking;
                        Progress = 0.0;
                    }

                    break;

                case DockingState.Undocking:
                    Progress = Math.Min(1.0, Progress + deltaSeconds / DockSeconds);
                    if (Progress >= 1.0)
                    {
                        State = DockingState.Free;
                        Progress = 0.0;
                    }

                    break;
            }
        }

        /// <summary>補間の始点と終点の間の現在位置。Docking / Undocking で使う。</summary>
        public static Vec3d Interpolate(Vec3d from, Vec3d to, double progress)
        {
            double t = progress < 0.0 ? 0.0 : (progress > 1.0 ? 1.0 : progress);
            // 端で速度が 0 になる補間。等速だと着いた瞬間に止まって見える。
            double smooth = t * t * (3.0 - 2.0 * t);
            return from + (to - from) * smooth;
        }

        public void Reset()
        {
            State = DockingState.Free;
            Progress = 0.0;
            LastDistance = 0.0;
        }

        /// <summary>ドッキング済みとして始める (往復テスト用)。</summary>
        public void ForceDocked()
        {
            State = DockingState.Docked;
            Progress = 1.0;
        }
    }
}
