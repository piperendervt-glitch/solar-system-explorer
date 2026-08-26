using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// デバッグ用の瞬間移動先 (Step 3a)。
    ///
    /// 等倍時間で待つ設計 (要件 §3) なので、目視確認のたびに実時間で飛んでいたら
    /// 検証が回らない。火星から指定距離の地点へ即座に置き直すためのテーブル。
    ///
    /// 7 番と 8 番は**プロキシ殻の近距離下限 (火星 6.777e3 units) より内側**。
    /// Step 2 で判明した破綻を目視するために置いてある (docs/01-architecture.md §3-3)。
    /// </summary>
    public static class DebugJumpTable
    {
        /// <summary>火星からの距離 [units]。キー 1〜8 に対応。</summary>
        static readonly double[] DistanceTable =
        {
            1.0e7,
            1.0e6,
            1.0e5,
            5.0e4,
            2.0e4,
            1.0e4,
            5.0e3, // プロキシ殻の下限 6777 units の内側
            4.0e3, // 同上 (火星半径 3389.5 units の外側。3e3 は惑星の内側だった)
        };

        public static IReadOnlyList<double> Distances => DistanceTable;

        public static int Count => DistanceTable.Length;

        /// <summary>
        /// 火星から distance だけ地球側へ戻った絶対座標。
        /// 太陽・地球・火星は一直線 (衝) なので、この点から火星を見ると
        /// 太陽は真後ろになり、火星は満月位相で照らされる。
        /// </summary>
        public static Vec3d PositionAt(SolarSystemModel model, double distanceFromMars)
        {
            Vec3d towardMars = (model.Mars.AbsolutePosition - model.Earth.AbsolutePosition).Normalized;
            return model.Mars.AbsolutePosition - towardMars * distanceFromMars;
        }

        public static Vec3d PositionForIndex(SolarSystemModel model, int index)
        {
            if (index < 0 || index >= DistanceTable.Length)
            {
                return model.Earth.AbsolutePosition;
            }

            return PositionAt(model, DistanceTable[index]);
        }

        /// <summary>
        /// この距離でプロキシ殻の手前側が Deep カメラの near clip を切るか。
        /// 切る = 天体の手前が欠けて見える。
        /// </summary>
        public static bool ClipsNearPlane(double bodyRadiusKm, double distance, double nearClip)
        {
            double shell = DeepProxyProjection.ShellRadius(distance);
            double front = shell - bodyRadiusKm * DeepProxyProjection.ScaleFactor(distance);
            return front < nearClip;
        }
    }
}
