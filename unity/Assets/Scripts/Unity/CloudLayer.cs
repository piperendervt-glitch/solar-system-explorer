namespace SolarSystem.Unity
{
    /// <summary>
    /// 雲層の寸法 (Step 8-3)。Editor と実行時の両方が同じ値を読む。
    /// </summary>
    public static class CloudLayer
    {
        /// <summary>
        /// 雲球の半径倍率。実寸 +8 km では 1 画素にならないので見た目優先で決めた
        /// (docs/02-demo2-plan.md 8-3)。
        /// </summary>
        public const float RadiusScale = 1.006f;
    }
}
