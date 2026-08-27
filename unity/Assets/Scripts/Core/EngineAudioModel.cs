using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// エンジン音のスラスト連動 (Step 10-3)。**UnityEngine 非依存。**
    ///
    /// 入力（スラスト 0〜1、制動中か）から、**音量係数**と **pitch** を決める。
    /// 急変を避けるため一次遅れを通す。時定数は耳で決める値なので F4 で振れる。
    ///
    /// ■ **音量は「係数」であって絶対値ではない。**
    /// 絶対値を返すと 10-2 の「音量の掛け算は AudioRouting だけ」が崩れる。
    /// 実際の音量は AudioRouting が
    ///   Master × Engine × DockedScale × **この係数**
    /// として組み立てる。
    ///
    /// ■ **全開で 1.0 になるよう正規化してある。**
    /// 計画書 §10-3 は アイドル 0.25 / 全開 0.70 / 制動中 0.60 と書いているが、
    /// **比率はそのままに、全体を 0.70 で割った値**を使う。
    ///
    ///   アイドル 0.25 / 0.70 = 0.36
    ///   全開     0.70 / 0.70 = 1.00
    ///   制動中   0.60 / 0.70 = 0.86
    ///
    /// 理由: 10-2 の試聴時、エンジンは**この係数が掛かっていない状態**で鳴っており、
    /// その音を聴いて Engine = 0.10 が決まった。0.25〜0.70 をそのまま掛けると
    /// 全開でも 0.10 × 0.70 = 0.07 にしかならず、**耳で承認した音量を下回る。**
    /// 正規化すれば Engine = 0.10 が「全開時の音量」を意味し、決めた値がそのまま生きる。
    ///
    /// ■ ローパスは動かさない
    /// カットオフはドッキングだけで動かす（§10-5）。2 つの信号で 1 つのフィルタを
    /// 動かすと「エンジンが休んでいる」遷移の意味が曖昧になるため。
    /// 音色の変化は pitch 0.9〜1.2 で出す。
    /// </summary>
    public sealed class EngineAudioModel
    {
        // ---- 音量係数（全開 1.0 で正規化済み。上の説明を参照） ----

        public const double IdleVolumeScale = 0.36;
        public const double FullVolumeScale = 1.00;
        public const double BrakeVolumeScale = 0.86;

        // ---- pitch ----
        // **0.9〜1.2 に収める。** ループ素材の周期 4.900 秒はこの範囲で
        // 5.44〜4.08 秒に変わる。範囲を広げるとループ端の粗が目立つ。

        public const double IdlePitch = 0.9;
        public const double FullPitch = 1.2;

        /// <summary>制動中の pitch。**一定**にして「逆噴射している」感を出す。</summary>
        public const double BrakePitch = 1.05;

        public double VolumeScale { get; private set; } = IdleVolumeScale;
        public double Pitch { get; private set; } = IdlePitch;

        /// <summary>スラストと制動から、遅れを通す前の音量係数を出す。</summary>
        public static double TargetVolumeScale(double thrust01, bool braking)
        {
            if (braking)
            {
                return BrakeVolumeScale;
            }

            double t = AudioMix.Clamp01(thrust01);
            return IdleVolumeScale + (FullVolumeScale - IdleVolumeScale) * t;
        }

        /// <summary>スラストと制動から、遅れを通す前の pitch を出す。</summary>
        public static double TargetPitch(double thrust01, bool braking)
        {
            if (braking)
            {
                return BrakePitch;
            }

            double t = AudioMix.Clamp01(thrust01);
            return IdlePitch + (FullPitch - IdlePitch) * t;
        }

        /// <summary>
        /// 一次遅れ。**時定数 tau で目標の 63.2% まで動く。**
        ///
        /// 離散化は `1 - exp(-dt/tau)` の厳密形を使う。
        /// `dt/tau` の線形近似だと、フレームが飛んだとき（dt > tau）に
        /// 行き過ぎて発振する。
        /// </summary>
        public static double FirstOrderLag(double current, double target,
                                           double deltaSeconds, double tauSeconds)
        {
            if (tauSeconds <= 0.0 || deltaSeconds <= 0.0)
            {
                return target;
            }

            double alpha = 1.0 - Math.Exp(-deltaSeconds / tauSeconds);
            return current + (target - current) * alpha;
        }

        /// <summary>1 フレームぶん進める。</summary>
        public void Advance(double thrust01, bool braking, double deltaSeconds, double tauSeconds)
        {
            VolumeScale = FirstOrderLag(
                VolumeScale, TargetVolumeScale(thrust01, braking), deltaSeconds, tauSeconds);
            Pitch = FirstOrderLag(
                Pitch, TargetPitch(thrust01, braking), deltaSeconds, tauSeconds);
        }

        /// <summary>遅れを飛ばして今すぐ合わせる。シナリオの適用時に使う。</summary>
        public void Snap(double thrust01, bool braking)
        {
            VolumeScale = TargetVolumeScale(thrust01, braking);
            Pitch = TargetPitch(thrust01, braking);
        }
    }
}
