namespace SolarSystem.Core
{
    /// <summary>
    /// 浮動原点 (決定 D-3: 毎フレーム再基準化)。
    ///
    /// Unity の (0,0,0) がどの絶対座標に対応しているかを保持し、
    /// 絶対座標 → 原点相対座標 の変換を提供する。
    ///
    /// 毎フレームカメラ (= 船) の絶対位置を Origin に据えるので、
    /// 船の原点相対座標は常に厳密に 0 になり、近傍の物体も原点の近くに来る。
    /// docs/01-architecture.md §2-2 の実測:
    ///   原点から 0.01 units (10 m) での float32 の刻みは 0.93 µm。
    ///   閾値方式だと 0.9c/60fps では 1 フレーム 4497 units 離れ、刻みは 49 cm になる。
    ///
    /// この型は状態としてオフセットを 1 つ持つだけ。誤差は蓄積しない
    /// (毎回「引き算」をするだけで、前回の結果を足し込まないため)。
    /// </summary>
    public sealed class FloatingOrigin
    {
        /// <summary>Unity の (0,0,0) が指す絶対座標。</summary>
        public Vec3d Origin { get; private set; } = Vec3d.Zero;

        /// <summary>直前の Rebase で原点が動いた量。VFX の追従などに使う。</summary>
        public Vec3d LastShift { get; private set; } = Vec3d.Zero;

        /// <summary>Rebase が呼ばれた回数。テストと診断用。</summary>
        public long ShiftCount { get; private set; }

        /// <summary>原点を指定の絶対座標へ据え直す。</summary>
        public void Rebase(Vec3d newOrigin)
        {
            LastShift = newOrigin - Origin;
            Origin = newOrigin;
            ShiftCount++;
        }

        /// <summary>絶対座標 → 原点相対座標。Transform に書くのはこの値。</summary>
        public Vec3d ToOriginRelative(Vec3d absolute) => absolute - Origin;

        /// <summary>原点相対座標 → 絶対座標。</summary>
        public Vec3d ToAbsolute(Vec3d originRelative) => originRelative + Origin;

        public void Reset()
        {
            Origin = Vec3d.Zero;
            LastShift = Vec3d.Zero;
            ShiftCount = 0;
        }
    }
}
