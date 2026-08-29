using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// **ステーションのローカル軸をワールドへ写す基底 (Step 13-3 コミット2)。**
    ///
    /// ■ 何のためにあるか
    /// ポート位置を `PortLocal × Scale` から導くには、プレハブのローカル座標を
    /// ワールドへ写す向きが要る。**描画側（`StationView`）と判定側（`SpaceStation`）が
    /// 別々に向きを作ると、絵と数字が別のものを見る。** そこで向きを 1 箇所に置く。
    ///
    /// ■ **いまの規約: プレハブのローカル +Z がポート方向**
    /// 箱のポート（`SceneBuilder` が置く円柱）がローカル +Z にあり、
    /// `StationView` が `LookRotation(PortDirection)` で据えているので、これが現行の規約。
    /// `Unity` の `Quaternion.LookRotation(forward)`（up 既定 = +Y）と**同じ基底**を作る。
    /// 一致することは `StationBasisTests` が Unity 側と突き合わせて縛っている。
    ///
    /// ■ **`PortForward` / `PortUp` はまだ読んでいない**
    /// 姿勢（どの軸をポートに向け、ロールをどう決めるか）は 13-3 コミット3 の範囲。
    /// **箱の `PortLocal` は 0 なので、ロールが何であっても現在のポート位置は動かない。**
    /// 実物のモデルを置くときに、2 軸で姿勢を決めてここを差し替えること。
    /// </summary>
    public readonly struct StationBasis
    {
        StationBasis(Vec3d right, Vec3d up, Vec3d forward)
        {
            Right = right;
            Up = up;
            Forward = forward;
        }

        /// <summary>ローカル +X の行き先。</summary>
        public Vec3d Right { get; }

        /// <summary>ローカル +Y の行き先。</summary>
        public Vec3d Up { get; }

        /// <summary>ローカル +Z の行き先（= ポート方向）。</summary>
        public Vec3d Forward { get; }

        /// <summary>`Quaternion.LookRotation(forward)` が縮退するときに使う +X。</summary>
        public static Vec3d DegenerateRight => new Vec3d(1.0, 0.0, 0.0);

        /// <summary>縮退とみなす |cross| の下限。</summary>
        public const double DegenerateEpsilon = 1e-9;

        /// <summary>
        /// ポート方向から基底を作る。**`Quaternion.LookRotation(forward)` と同じ。**
        ///
        ///   z = normalize(forward)
        ///   x = normalize(cross(worldUp, z))     縮退したら (1, 0, 0)
        ///   y = cross(z, x)
        ///
        /// **`forward` が ±Y のときは縮退する**（このプロジェクトのステーションは
        /// まさに +Y に置かれている / `SolarSystemModel`）。そこで Unity が返す値と
        /// 同じ (1, 0, 0) を選ぶ。**推測ではなく `StationBasisTests` で突き合わせてある。**
        /// </summary>
        public static StationBasis FromPortDirection(Vec3d portDirection)
        {
            Vec3d z = portDirection.Normalized;
            if (z.SqrMagnitude <= 0.0)
            {
                throw new ArgumentException("ポート方向が 0 ベクトル", nameof(portDirection));
            }

            var worldUp = new Vec3d(0.0, 1.0, 0.0);
            Vec3d cross = Vec3d.Cross(worldUp, z);

            Vec3d x = cross.Magnitude <= DegenerateEpsilon
                ? DegenerateRight
                : cross.Normalized;

            Vec3d y = Vec3d.Cross(z, x);
            return new StationBasis(x, y, z);
        }

        /// <summary>ローカル座標をワールドへ写す（平行移動は含まない）。</summary>
        public Vec3d ToWorld(Vec3d local) => Right * local.X + Up * local.Y + Forward * local.Z;
    }
}
