using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// **頭姿勢の各段への配布と、視差・方向変化の理論値 (Step 12-1)。**
    ///
    /// XR パッケージには依存しない。**純粋な計算だけ。**
    /// ここが後のセッションで「実測と突き合わせる基準」になる。
    ///
    /// ■ **実測とずれても、この式を実測に合わせて変えないこと。**
    /// ずれたら疑うのは式ではなく描画側の実装（目のインデックスの伝播、
    /// 段ごとの姿勢の配り方、スケールの掛け忘れ）。ずれの事実は数値で記録する。
    ///
    /// ■ 単位の約束
    ///   頭の位置と IPD とベースラインは **[m]**（HMD が返す単位）
    ///   段の中の距離は **[units]**（その段の描画空間の単位）
    ///   `scale` が m -> units の換算。**外 3 段 0.001 / コックピット段 1.0**
    ///
    /// **回転はスケールしない。** 縮尺を変えても向きは向きのまま。
    /// 位置だけを s 倍することで、コックピット段と外 3 段で
    /// 「同じ頭の動き」が「同じ見かけの角度」になる。
    /// </summary>
    public static class StereoGeometry
    {
        /// <summary>
        /// コックピット段の換算。**1 m = 1 unit。**
        /// コックピット段だけ 1000 倍の描画空間で組んである
        /// （`CameraStackController.CockpitRenderScale`。near clip の下限 0.01 を避けるため）。
        /// </summary>
        public const double CockpitStageScale = 1.0;

        /// <summary>
        /// 外 3 段 (Deep / Near / Nearfield) の換算。**1 m = 0.001 units。**
        /// この世界は 1 unit = 1 km なので、1 m は 1e-3 units。
        /// </summary>
        public const double WorldStageScale = 0.001;

        // **IPD と焦点距離の既定値はここに置かない (Step 12-1b)。**
        //
        // 「IPD 63 mm」も「f = 935.31 px（1080p / 60 度）」も、平面のときの
        // 見積もりであって XR の入力ではない。12-0d の実測で目テクスチャは
        // 1512x1680 で、画角は HMD の投影行列が決めていた。
        //
        // 既定値を持つと、渡し忘れたときに黙って平面の値で計算が通ってしまい、
        // 「実測 / 理論」が 1.0 から外れたときに**描画の壊れなのか入力違いなのかを
        // 切り分けられなくなる。** そこで f も IPD も**呼び出し側から必ず受け取る。**
        // 実行時の値は `XrStereoOptics`（Unity 側）が測る。

        public enum Eye
        {
            Left = 0,
            Right = 1,
        }

        /// <summary>
        /// 頭のローカル姿勢。**位置は [m]。**
        /// 回転は Quaternion ではなく**前方と上方の 2 軸**で持つ
        /// （Core に Quaternion は無い。11-2c で 1 軸だと前後と上下が同時に
        /// 反転しうることを実機で踏んでいる）。
        /// </summary>
        public readonly struct HeadPose
        {
            public readonly Vec3d PositionMeters;
            public readonly Vec3d Forward;
            public readonly Vec3d Up;

            public HeadPose(Vec3d positionMeters, Vec3d forward, Vec3d up)
            {
                PositionMeters = positionMeters;
                Forward = forward;
                Up = up;
            }

            public static HeadPose Identity =>
                new HeadPose(Vec3d.Zero, new Vec3d(0.0, 0.0, 1.0), new Vec3d(0.0, 1.0, 0.0));
        }

        /// <summary>
        /// ある段のカメラのローカル姿勢。**位置はその段の [units]。**
        /// </summary>
        public readonly struct StagePose
        {
            public readonly Vec3d Position;
            public readonly Vec3d Forward;
            public readonly Vec3d Up;

            public StagePose(Vec3d position, Vec3d forward, Vec3d up)
            {
                Position = position;
                Forward = forward;
                Up = up;
            }

            /// <summary>右方向。**Unity と同じ左手系**（右 = up x forward）。</summary>
            public Vec3d Right => Vec3d.Cross(Up, Forward);
        }

        // ---- 1. 頭姿勢を段へ配る ----

        /// <summary>
        /// 頭のローカル姿勢を、スケール s の段のカメラのローカル姿勢に直す。
        ///
        /// **位置だけ s 倍する。回転はそのまま。**
        /// これが「同じ頭の動きが、どの段でも同じ見かけの角度になる」ことの正体。
        /// </summary>
        public static StagePose CameraLocal(HeadPose head, double scale)
        {
            RequirePositiveScale(scale);
            return new StagePose(head.PositionMeters * scale, head.Forward, head.Up);
        }

        // ---- 2. 両眼の位置 ----

        /// <summary>
        /// 頭の中心から見た片目のずれ [m]。左は -IPD/2、右は +IPD/2。
        /// **右方向は前方と上方から作る**（別々に渡さない。ずれると左右が入れ替わる）。
        /// </summary>
        public static Vec3d EyeOffsetMeters(HeadPose head, Eye eye, double ipdMeters)
        {
            RequireNonNegative(ipdMeters, nameof(ipdMeters));

            Vec3d right = Vec3d.Cross(head.Up, head.Forward).Normalized;
            double sign = eye == Eye.Right ? 0.5 : -0.5;
            return right * (ipdMeters * sign);
        }

        /// <summary>
        /// 片目のカメラのローカル姿勢。**目のずれもスケールに従う。**
        /// 外 3 段では IPD 63 mm が 6.3e-5 units になる。
        /// </summary>
        public static StagePose EyeLocal(HeadPose head, Eye eye, double ipdMeters, double scale)
        {
            RequirePositiveScale(scale);

            Vec3d positionMeters = head.PositionMeters + EyeOffsetMeters(head, eye, ipdMeters);
            return new StagePose(positionMeters * scale, head.Forward, head.Up);
        }

        /// <summary>IPD をその段の単位に直す [units]。</summary>
        public static double IpdInStageUnits(double ipdMeters, double scale)
        {
            RequirePositiveScale(scale);
            RequireNonNegative(ipdMeters, nameof(ipdMeters));
            return ipdMeters * scale;
        }

        // ---- 3. 視差の理論値 ----

        /// <summary>
        /// 投影行列と目テクスチャの高さから焦点距離 [px] を出す (Step 12-1b)。
        ///
        /// 透視投影の `m11` は 1/tan(縦画角/2)（非対称な錐台でも 2n/(t-b)）なので、
        /// **f [px] = (H/2) * m11。**
        ///
        /// **XR では画角はカメラの `fieldOfView` ではなく HMD の投影行列が決める。**
        /// 平面の f（1080p / 60 度で 935.31 px）をそのまま持ち込まないこと。
        /// 左右で違いうるので、**目ごとに出す。**
        /// </summary>
        public static double FocalLengthPixelsFromProjection(double m11, int eyeTextureHeight)
        {
            RequirePositive(m11, nameof(m11));

            if (eyeTextureHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(eyeTextureHeight), eyeTextureHeight, "目テクスチャの高さは正の数");
            }

            return eyeTextureHeight * 0.5 * m11;
        }

        /// <summary>
        /// 両眼の像のずれ [px]。**視差 = f * IPD / 距離。**
        ///
        /// これは近似ではない。ピンホールで x = f*X/Z なので、カメラを視線に
        /// 垂直へ IPD だけ動かすと像は厳密に f*IPD/Z ずれる。
        /// **ただし距離は視線方向の奥行き** [units]（放射距離ではない）。
        ///
        /// IPD [m] と距離 [units] の単位が違うので `scale` で揃える。
        /// </summary>
        public static double ParallaxPixels(
            double ipdMeters, double distanceStageUnits, double scale, double focalLengthPixels)
        {
            RequirePositiveDistance(distanceStageUnits);
            RequirePositive(focalLengthPixels, nameof(focalLengthPixels));

            return focalLengthPixels * IpdInStageUnits(ipdMeters, scale) / distanceStageUnits;
        }

        // ---- 4. 頭を動かしたときの方向変化 ----

        /// <summary>
        /// 頭を baseline [m] 動かしたときの、距離 D の物体の方向変化 [rad]。
        /// **Δθ = baseline / D。**
        ///
        /// **これは小角近似。** 移動が距離に対して小さいことを前提にしている。
        /// 厳密には atan(baseline / D) で、比が大きいと乖離する
        /// （0.5 m 先を 0.3 m 動くと 0.600 rad 対 0.540 rad で 11 % 違う）。
        /// **式は近似のまま置く。** 実測と食い違ったときに、どちらの差なのかを
        /// 切り分けられるよう `DirectionChangeExactRadians` を並べてある。
        ///
        /// 移動が視線に垂直で、物体が正面にある場合の値。
        /// </summary>
        public static double DirectionChangeRadians(
            double baselineMeters, double distanceStageUnits, double scale)
        {
            RequirePositiveDistance(distanceStageUnits);
            RequireNonNegative(baselineMeters, nameof(baselineMeters));
            RequirePositiveScale(scale);

            return baselineMeters * scale / distanceStageUnits;
        }

        /// <summary>
        /// **参考値。指定された式ではない。** 厳密な atan 版。
        /// 小角近似がどこから外れるかを数値で見るために置いてある。
        /// </summary>
        public static double DirectionChangeExactRadians(
            double baselineMeters, double distanceStageUnits, double scale)
            => Math.Atan(DirectionChangeRadians(baselineMeters, distanceStageUnits, scale));

        /// <summary>方向変化を px に直す [px]。</summary>
        public static double DirectionChangePixels(
            double baselineMeters, double distanceStageUnits, double scale, double focalLengthPixels)
        {
            RequirePositive(focalLengthPixels, nameof(focalLengthPixels));
            return DirectionChangeRadians(baselineMeters, distanceStageUnits, scale) * focalLengthPixels;
        }

        // ---- 前提の検査 ----
        //
        // **黙って 0 や NaN を返さない。** 単位を取り違えたまま「小さい値が出た」
        // で通ると、あとから実測と突き合わせたときに原因が見えなくなる。

        static void RequirePositiveScale(double scale)
        {
            if (!(scale > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "スケールは正の数");
            }
        }

        static void RequirePositiveDistance(double distance)
        {
            if (!(distance > 0.0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(distance), distance, "距離は正の数（0 で割らない）");
            }
        }

        static void RequirePositive(double value, string name)
        {
            if (!(value > 0.0))
            {
                throw new ArgumentOutOfRangeException(name, value, name + " は正の数");
            }
        }

        static void RequireNonNegative(double value, string name)
        {
            if (!(value >= 0.0))
            {
                throw new ArgumentOutOfRangeException(name, value, name + " は 0 以上");
            }
        }
    }
}
