using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// 光点⇔メッシュの切替 (docs/01-architecture.md §3-3 / 決定 D-7)。
    ///
    /// 角直径 4 px でメッシュを出し始め、8 px で光点を消す。その間はクロスフェード。
    ///
    /// **ブレンド率は px の連続関数**にしてある。状態を持たせて段差を作らないため。
    /// ヒステリシスは「レンダラを有効/無効にするタイミング」にだけ掛ける。
    /// alpha が既に 0 の領域で切り替えるので、見た目は一切変わらない。
    ///   - メッシュ: 4 px で有効化、3.8 px で無効化 (この間 alpha = 0)
    ///   - 光点  : 8 px で無効化、7.6 px で有効化 (この間 alpha = 0)
    /// これで px が 4 px 付近を往復してもレンダラが毎フレーム ON/OFF せず、
    /// かつブレンド率が飛ばない。
    /// </summary>
    public sealed class BodyLodSolver
    {
        public const double MeshEnterPixels = 4.0;
        public const double MeshCompletePixels = 8.0;

        /// <summary>有効/無効のヒステリシス幅 (境界の 5%)。</summary>
        public const double HysteresisFraction = 0.05;

        const double MeshLeavePixels = MeshEnterPixels * (1.0 - HysteresisFraction);      // 3.8
        const double PointReenterPixels = MeshCompletePixels * (1.0 - HysteresisFraction); // 7.6

        public BodyLodSolver()
        {
            PointActive = true;
            MeshActive = false;
        }

        /// <summary>0 = 光点のみ / 1 = メッシュのみ。px の連続関数。</summary>
        public double Blend { get; private set; }

        public bool PointActive { get; private set; }

        public bool MeshActive { get; private set; }

        /// <summary>レンダラの有効/無効が切り替わった回数。ちらつきの検出用。</summary>
        public int ToggleCount { get; private set; }

        public void Update(double angularDiameterPixels)
        {
            Blend = Clamp01((angularDiameterPixels - MeshEnterPixels)
                            / (MeshCompletePixels - MeshEnterPixels));

            bool meshActive = MeshActive
                ? angularDiameterPixels >= MeshLeavePixels
                : angularDiameterPixels >= MeshEnterPixels;

            bool pointActive = PointActive
                ? angularDiameterPixels < MeshCompletePixels
                : angularDiameterPixels < PointReenterPixels;

            if (meshActive != MeshActive)
            {
                MeshActive = meshActive;
                ToggleCount++;
            }

            if (pointActive != PointActive)
            {
                PointActive = pointActive;
                ToggleCount++;
            }
        }

        public void Reset()
        {
            Blend = 0.0;
            PointActive = true;
            MeshActive = false;
            ToggleCount = 0;
        }

        static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
