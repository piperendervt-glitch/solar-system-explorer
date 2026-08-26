using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>速度ダイヤルの 1 段。</summary>
    public readonly struct SpeedStep
    {
        public SpeedStep(double kmPerSec, bool showAsBeta)
        {
            KmPerSec = kmPerSec;
            ShowAsBeta = showAsBeta;
        }

        public double KmPerSec { get; }

        /// <summary>true なら c 表記、false なら km/s 表記 (決定 D-11)。</summary>
        public bool ShowAsBeta { get; }

        public double Beta => UniverseConstants.KmPerSecToBeta(KmPerSec);

        public string Label
        {
            get
            {
                if (KmPerSec <= 0.0)
                {
                    return "STOP";
                }

                return ShowAsBeta
                    ? $"{Beta:0.###} c"
                    : $"{KmPerSec:0.###} km/s";
            }
        }
    }

    /// <summary>
    /// 速度ダイヤル (Step 3a)。0 から 0.9c までを段階で切り替える。
    ///
    /// **低速側は km/s 表記で上限 1 km/s (決定 D-11)。**
    /// 1 km/s なら 60fps で 1 フレーム 0.0167 units (16.7 m) しか進まず目で追える。
    /// c 表記にすると 0.0000033c になって計器上で意味をなさないので、単位を分ける。
    ///
    /// 高速側は c 表記。既定巡航 0.9c (決定 D-8) が上端。
    /// 加減速プロファイルは持たない。それは Step 3b の作業。
    /// </summary>
    public sealed class SpeedDial
    {
        static readonly SpeedStep[] StepTable =
        {
            new SpeedStep(0.0, false),   // STOP
            new SpeedStep(0.01, false),  // 10 m/s
            new SpeedStep(0.1, false),   // 100 m/s
            new SpeedStep(1.0, false),   // 1 km/s  <- 手動操作の上限 (D-11)
            new SpeedStep(UniverseConstants.BetaToKmPerSec(0.001), true),
            new SpeedStep(UniverseConstants.BetaToKmPerSec(0.01), true),
            new SpeedStep(UniverseConstants.BetaToKmPerSec(0.1), true),
            new SpeedStep(UniverseConstants.BetaToKmPerSec(0.5), true),
            new SpeedStep(UniverseConstants.BetaToKmPerSec(UniverseConstants.DefaultCruiseBeta), true),
        };

        public static IReadOnlyList<SpeedStep> Steps => StepTable;

        public int Index { get; private set; }

        public SpeedStep Current => StepTable[Index];

        /// <summary>手動操作の速度域 (1 km/s 以下) にいるか。</summary>
        public bool IsManualRegime => Current.KmPerSec <= UniverseConstants.ManualMaxSpeedKmPerSec;

        /// <summary>段を上下する。端で止まる (巡回しない — 巡航中に誤って STOP へ回らないため)。</summary>
        public void Shift(int delta)
        {
            Index = Math.Max(0, Math.Min(StepTable.Length - 1, Index + delta));
        }

        public void SetIndex(int index)
        {
            Index = Math.Max(0, Math.Min(StepTable.Length - 1, index));
        }

        public void Stop() => Index = 0;
    }
}
