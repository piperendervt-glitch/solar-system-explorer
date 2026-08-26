namespace SolarSystem.Core
{
    /// <summary>
    /// 絶対座標の等速直線運動。位置と速度を double で持ち、1 固定ステップ進める。
    ///
    /// Step 1 の範囲はここまで。加減速プロファイル・オートパイロット・
    /// ドッキング状態機械は Step 3 以降で ShipIntegrator / AutopilotSolver として足す
    /// (docs/01-architecture.md §4)。この型はそれらの土台になる積分部分だけ。
    /// </summary>
    public sealed class AbsoluteMotion
    {
        public Vec3d Position { get; private set; } = Vec3d.Zero;

        public Vec3d Velocity { get; private set; } = Vec3d.Zero;

        /// <summary>速さ [km/s]。</summary>
        public double SpeedKmPerSec => Velocity.Magnitude;

        /// <summary>速さの c 正規化値 β = v/c。相対論拡張はこれを読む (決定 D-21)。</summary>
        public double Beta => UniverseConstants.KmPerSecToBeta(SpeedKmPerSec);

        public void SetPosition(Vec3d position) => Position = position;

        public void SetVelocity(Vec3d velocity) => Velocity = velocity;

        /// <summary>固定ステップ 1 回ぶん進める。</summary>
        public void Step(double deltaSeconds)
        {
            Position += Velocity * deltaSeconds;
        }

        public void Reset()
        {
            Position = Vec3d.Zero;
            Velocity = Vec3d.Zero;
        }
    }
}
