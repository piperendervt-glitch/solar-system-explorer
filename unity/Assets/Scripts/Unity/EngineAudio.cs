using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// エンジンのアンビエント音 (Step 6)。
    ///
    /// **外部素材は使わない。** ホワイトノイズを一次ローパスに通した低周波を
    /// AudioClip としてコードで生成し、ループ再生する。
    /// スラストに応じて音量とローパスのカットオフ (＝音色) を動かす。
    ///
    /// 生成は一度きり。毎フレームやることは AudioSource の volume と
    /// AudioLowPassFilter の cutoff を追従させるだけ。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EngineAudio : MonoBehaviour
    {
        public const int SampleRate = 44100;
        public const int LoopSeconds = 4;

        /// <summary>生成時のローパス係数。小さいほど低い音になる。</summary>
        const float GenerationSmoothing = 0.02f;

        [SerializeField] AudioSource _source;
        [SerializeField] AudioLowPassFilter _lowPass;

        [Header("スラスト 0 / 1 のときの値")]
        [SerializeField] float _idleVolume = 0.06f;
        [SerializeField] float _fullVolume = 0.45f;
        [SerializeField] float _idleCutoff = 220f;
        [SerializeField] float _fullCutoff = 1400f;

        public float LastVolume { get; private set; }
        public float LastCutoff { get; private set; }

        public AudioSource Source => _source;

        public void Bind(AudioSource source, AudioLowPassFilter lowPass)
        {
            _source = source;
            _lowPass = lowPass;
        }

        /// <summary>
        /// ノイズを一次ローパスに通した低周波ループを作る。
        /// 端をクロスフェードして継ぎ目が鳴らないようにする。
        /// </summary>
        public static AudioClip CreateRumbleClip(int seed = 12345)
        {
            int total = SampleRate * LoopSeconds;
            var samples = new float[total];

            var random = new System.Random(seed);
            float state = 0f;

            for (int i = 0; i < total; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                state += (white - state) * GenerationSmoothing;
                samples[i] = state;
            }

            // 継ぎ目を消す。末尾 0.25 秒を先頭へクロスフェードする。
            int fade = SampleRate / 4;
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                samples[i] = Mathf.Lerp(samples[total - fade + i], samples[i], t);
            }

            // 正規化。generation の後は振幅が小さいので持ち上げる。
            float peak = 0f;
            for (int i = 0; i < total; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            if (peak > 0f)
            {
                float gain = 0.9f / peak;
                for (int i = 0; i < total; i++)
                {
                    samples[i] *= gain;
                }
            }

            var clip = AudioClip.Create("EngineRumble", total, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>スラスト (0..1) に追従させる。UniverseRoot から毎フレーム呼ばれる。</summary>
        public void Apply(float thrust01)
        {
            float t = Mathf.Clamp01(Mathf.Abs(thrust01));
            LastVolume = Mathf.Lerp(_idleVolume, _fullVolume, t);
            LastCutoff = Mathf.Lerp(_idleCutoff, _fullCutoff, t);

            if (_source != null)
            {
                _source.volume = LastVolume;
                if (!_source.isPlaying && _source.clip != null)
                {
                    _source.Play();
                }
            }

            if (_lowPass != null)
            {
                _lowPass.cutoffFrequency = LastCutoff;
            }
        }
    }
}
