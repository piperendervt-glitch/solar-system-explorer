using System;

namespace SolarSystem.Core
{
    public enum AutopilotState
    {
        Idle,
        Align,
        Cruise,
        Brake,
        Arrived,
    }

    /// <summary>
    /// オートパイロット (Step 3b / docs/01-architecture.md §4-3)。
    ///
    /// Idle -> Align (機首を向ける) -> Cruise -> Brake -> Arrived
    ///
    /// 制動は**制動時間一定 10 秒** (決定 D-9)。速度 v からの制動距離は v * T / 2。
    /// 0.9c なら 1.349e6 units。
    ///
    /// 到着は**目標から 20 units 以内かつ 1 km/s 以下** (決定 D-10)。
    ///
    /// 姿勢は Unity 側 (float Quaternion / 決定 D-4) が持つので、
    /// ここへは「目標方向と機首のなす角」を毎ステップ渡してもらう。
    /// こちらは状態と指令速度だけを決める。
    /// </summary>
    public sealed class AutopilotSolver
    {
        /// <summary>この角度以内に向いたら Cruise へ移る。</summary>
        public const double AlignToleranceDegrees = 0.5;

        /// <summary>制動にかける時間 [s] (決定 D-9)。</summary>
        public const double BrakeSeconds = 10.0;

        public AutopilotState State { get; private set; } = AutopilotState.Idle;

        public Vec3d TargetPosition { get; private set; }

        /// <summary>要求された巡航速度 [km/s]。</summary>
        public double RequestedCruiseKmPerSec { get; private set; }

        /// <summary>実際に使う巡航速度 [km/s]。止まりきれない速度は出さない (下記)。</summary>
        public double EffectiveCruiseKmPerSec { get; private set; }

        /// <summary>この速度で進めという指令 [km/s]。Unity 側が目標方向へ適用する。</summary>
        public double CommandedSpeedKmPerSec { get; private set; }

        public double RemainingDistance { get; private set; }

        /// <summary>現在の指令速度から止まるのに要る距離 [units]。</summary>
        public double BrakeDistance => CommandedSpeedKmPerSec * BrakeSeconds * 0.5;

        /// <summary>残り時間 [s]。求まらないときは PositiveInfinity。</summary>
        public double EtaSeconds { get; private set; } = double.PositiveInfinity;

        public bool IsEngaged => State != AutopilotState.Idle;

        /// <summary>
        /// 目標を設定して起動する。
        ///
        /// **止まりきれない速度は出さない。** 制動距離 v*T/2 が残距離を超えると
        /// 目標を通り過ぎるので、v を 2*(残距離 - 到着半径)/T で頭打ちにする。
        /// 実距離 7.8e7 units では上限が 52c 相当なので効かないが、
        /// 1/1000 スケールの仮目標 7.8e4 units (決定 D-8) では 0.052c まで落ちる。
        /// </summary>
        public void Engage(Vec3d shipPosition, Vec3d target, double cruiseKmPerSec)
        {
            TargetPosition = target;
            RequestedCruiseKmPerSec = cruiseKmPerSec;

            double remaining = Vec3d.Distance(shipPosition, target);
            double stoppable = 2.0 * Math.Max(0.0, remaining - UniverseConstants.ArrivalRadiusUnits) / BrakeSeconds;
            EffectiveCruiseKmPerSec = Math.Min(cruiseKmPerSec, stoppable);

            RemainingDistance = remaining;
            CommandedSpeedKmPerSec = 0.0;
            EtaSeconds = double.PositiveInfinity;
            State = AutopilotState.Align;
        }

        public void Disengage()
        {
            State = AutopilotState.Idle;
            CommandedSpeedKmPerSec = 0.0;
            EtaSeconds = double.PositiveInfinity;
        }

        /// <summary>
        /// 1 ステップ進める。
        /// </summary>
        /// <param name="shipPosition">船の絶対位置。</param>
        /// <param name="alignmentAngleDegrees">機首と目標方向のなす角 [deg]。</param>
        /// <param name="deltaSeconds">経過時間 [s]。</param>
        public void Step(Vec3d shipPosition, double alignmentAngleDegrees, double deltaSeconds)
        {
            if (State == AutopilotState.Idle)
            {
                CommandedSpeedKmPerSec = 0.0;
                EtaSeconds = double.PositiveInfinity;
                return;
            }

            RemainingDistance = Vec3d.Distance(shipPosition, TargetPosition);

            switch (State)
            {
                case AutopilotState.Align:
                    CommandedSpeedKmPerSec = 0.0;
                    if (alignmentAngleDegrees <= AlignToleranceDegrees)
                    {
                        State = AutopilotState.Cruise;
                    }

                    break;

                case AutopilotState.Cruise:
                    CommandedSpeedKmPerSec = EffectiveCruiseKmPerSec;
                    if (RemainingDistance - UniverseConstants.ArrivalRadiusUnits <= BrakeDistance)
                    {
                        State = AutopilotState.Brake;
                    }

                    break;

                case AutopilotState.Brake:
                    StepBrake(deltaSeconds);
                    break;

                case AutopilotState.Arrived:
                    CommandedSpeedKmPerSec = 0.0;
                    break;
            }

            // 1 ステップで目標を追い越さないよう指令速度を頭打ちにする。
            // これが無いと 0.9c では 1 フレーム 4497 units 進んで到着圏を飛び越す。
            if (deltaSeconds > 0.0 && CommandedSpeedKmPerSec * deltaSeconds > RemainingDistance)
            {
                CommandedSpeedKmPerSec = RemainingDistance / deltaSeconds;
            }

            UpdateEta();
        }

        void StepBrake(double deltaSeconds)
        {
            // 減速度は「制動時間一定 10 秒」(決定 D-9) から決まる: a = v_cruise / T。
            double decel = EffectiveCruiseKmPerSec / BrakeSeconds;

            bool closeEnough = RemainingDistance <= UniverseConstants.ArrivalRadiusUnits;

            // **時間で速度を落とす開ループにしない。残距離から逆算する閉ループにする。**
            // 開ループ (v -= a*dt) を離散積分すると台形則の誤差で行き過ぎる。
            // 実測: 仮目標 7.8e4 units で 130 units 行き過ぎ、目標を突き抜けた。
            // v = sqrt(2*a*d) なら離散化誤差が次のステップで自動的に補正される。
            double d = RemainingDistance - UniverseConstants.ArrivalRadiusUnits;
            double allowed = d > 0.0 ? Math.Sqrt(2.0 * decel * d) : 0.0;

            CommandedSpeedKmPerSec = Math.Min(EffectiveCruiseKmPerSec, allowed);

            // sqrt は 0 へ漸近するだけなので、これが無いと到着圏の外で止まる。
            // 残り 1 km/s を切ったら、その速度のまま寄せ切る。
            if (!closeEnough && CommandedSpeedKmPerSec < UniverseConstants.ArrivalMaxSpeedKmPerSec)
            {
                CommandedSpeedKmPerSec = UniverseConstants.ArrivalMaxSpeedKmPerSec;
            }

            if (closeEnough && CommandedSpeedKmPerSec <= UniverseConstants.ArrivalMaxSpeedKmPerSec)
            {
                State = AutopilotState.Arrived;
                CommandedSpeedKmPerSec = 0.0;
            }

            _ = deltaSeconds;
        }

        void UpdateEta()
        {
            switch (State)
            {
                case AutopilotState.Arrived:
                    EtaSeconds = 0.0;
                    return;

                case AutopilotState.Cruise:
                {
                    double v = CommandedSpeedKmPerSec;
                    if (v <= 0.0)
                    {
                        EtaSeconds = double.PositiveInfinity;
                        return;
                    }

                    double cruiseLeg = RemainingDistance - UniverseConstants.ArrivalRadiusUnits - BrakeDistance;
                    EtaSeconds = Math.Max(0.0, cruiseLeg) / v + BrakeSeconds;
                    return;
                }

                case AutopilotState.Brake:
                {
                    double decel = EffectiveCruiseKmPerSec / BrakeSeconds;
                    EtaSeconds = decel > 0.0 ? CommandedSpeedKmPerSec / decel : double.PositiveInfinity;
                    return;
                }

                default:
                    EtaSeconds = double.PositiveInfinity;
                    return;
            }
        }
    }
}
