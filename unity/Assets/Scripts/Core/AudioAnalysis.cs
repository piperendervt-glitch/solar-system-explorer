using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// 音の解析と加工 (Step 10-1)。**UnityEngine 非依存の純関数。**
    ///
    /// ここに置いた理由は 2 つ:
    ///   - ループ端の段差比を EditMode テストで縛れる（CLAUDE.md 0-B で
    ///     「人手不要にできる」と分類した項目）
    ///   - 加工そのものを ffmpeg ではなく C# で行うため。ffmpeg を
    ///     CLAUDE.md の環境前提に持ち込まない
    ///
    /// **加工パラメータと加工後の実測値は docs/audio-candidates.md に記録する。**
    /// 再生成物が一致することを検証できるようにするため。
    /// </summary>
    public static class AudioAnalysis
    {
        /// <summary>
        /// ループ端の段差比。
        /// **連結点の段差 ÷ 隣接サンプル差の平均。**
        ///
        /// 1 に近ければ、ループ端の跳びが素材のふつうの変化と同じ大きさ、
        /// つまり聴こえない。大きいほどクリックとして耳につく。
        /// 実測: 加工前の spaceEngineLow_003 は 85.18 倍。
        /// </summary>
        public static double SeamRatio(float[] samples)
        {
            if (samples == null || samples.Length < 2)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 1; i < samples.Length; i++)
            {
                sum += Math.Abs(samples[i] - samples[i - 1]);
            }

            double mean = sum / (samples.Length - 1);
            if (mean <= 0.0)
            {
                return 0.0;
            }

            double seam = Math.Abs(samples[0] - samples[samples.Length - 1]);
            return seam / mean;
        }

        public static double Peak(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return 0.0;
            }

            double peak = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                double a = Math.Abs(samples[i]);
                if (a > peak) { peak = a; }
            }

            return peak;
        }

        public static double Rms(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            return Math.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// 末尾を先頭へクロスフェードして、繋ぎ目の段差を消す。
        ///
        /// 出力の長さは <paramref name="samples"/> の長さ - crossfade。
        /// 先頭 crossfade サンプルが「元の先頭」と「元の末尾」の混合になるので、
        /// 出力の末尾から先頭へ戻ったときに波形が連続する。
        ///
        /// **エンジン音 (spaceEngineLow_003) 用。** 周期 5 秒は要件を満たしていて、
        /// 端の処理だけが未加工だった。
        /// </summary>
        public static float[] CrossfadeLoop(float[] samples, int crossfade)
        {
            if (samples == null || samples.Length == 0)
            {
                return Array.Empty<float>();
            }

            if (crossfade <= 0 || crossfade * 2 >= samples.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(crossfade),
                    $"クロスフェード長が不正: {crossfade} (サンプル数 {samples.Length})");
            }

            int length = samples.Length - crossfade;
            var result = new float[length];

            for (int i = 0; i < crossfade; i++)
            {
                // t = 0 で「元の末尾」寄り、t = 1 で「元の先頭」寄り。
                double t = (i + 0.5) / crossfade;
                result[i] = (float)(samples[i] * t + samples[length + i] * (1.0 - t));
            }

            for (int i = crossfade; i < length; i++)
            {
                result[i] = samples[i];
            }

            return result;
        }

        /// <summary>
        /// ピッチ違いの層を開始位置をずらして重ね、長いループを作る。
        ///
        /// **コックピット音 (forceField_000) 用。** 素材が 0.954 秒しかなく、
        /// そのままループさせると約 1 秒周期で反復に気付く。
        /// 段差は元々 0.05 倍しかないので、問題は周期の短さだけ。
        ///
        /// 素材は必要なだけ繰り返して読む（ffmpeg の stream_loop 相当）。
        /// ピッチは再生速度ごと変える（Unity の AudioSource.pitch と同じ挙動、
        /// ffmpeg の asetrate 相当）。層の合成は入力数で割る（amix normalize=1 相当）。
        /// </summary>
        public static float[] BuildLayeredLoop(
            float[] source, int sampleRate,
            double[] pitches, double[] offsetsSeconds,
            double lengthSeconds, double crossfadeSeconds)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<float>();
            }

            if (pitches == null || offsetsSeconds == null || pitches.Length != offsetsSeconds.Length)
            {
                throw new ArgumentException("ピッチとオフセットの数が合っていない");
            }

            if (pitches.Length == 0)
            {
                throw new ArgumentException("層が 1 つも無い");
            }

            int crossfade = (int)Math.Round(crossfadeSeconds * sampleRate);
            int length = (int)Math.Round(lengthSeconds * sampleRate);

            // クロスフェードで縮む分を見越して長めに作る。
            var mixed = new float[length + crossfade];

            for (int layer = 0; layer < pitches.Length; layer++)
            {
                double pitch = pitches[layer];
                double offset = offsetsSeconds[layer] * sampleRate;

                for (int i = 0; i < mixed.Length; i++)
                {
                    double position = offset + i * pitch;
                    mixed[i] += (float)(SampleLooped(source, position) / pitches.Length);
                }
            }

            return CrossfadeLoop(mixed, crossfade);
        }

        /// <summary>
        /// 素材を繰り返しながら線形補間で読む。
        /// **位置が素材の長さを超えたら先頭へ回る。**
        /// </summary>
        public static double SampleLooped(float[] source, double position)
        {
            if (source == null || source.Length == 0)
            {
                return 0.0;
            }

            int n = source.Length;
            double wrapped = position % n;
            if (wrapped < 0.0) { wrapped += n; }

            var i0 = (int)wrapped;
            int i1 = (i0 + 1) % n;
            double t = wrapped - i0;
            return source[i0] * (1.0 - t) + source[i1] * t;
        }
    }
}
