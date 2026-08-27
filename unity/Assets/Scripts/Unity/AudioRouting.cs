using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 音のグループと、飛行 ⇔ ドッキングの遷移 (Step 10-2 / 10-5)。
    ///
    /// ■ **ローパスを持つ唯一の場所がここ。**
    /// `AudioLowPassFilter` はエンジンの AudioSource に 1 個だけ付き、
    /// その cutoff を書くのはこのクラスだけ。**他のどこにも付けないこと。**
    /// Step 6 は AudioSource 側のローパスと Mixer 側のローパスを二重に持つ
    /// 予定になっており、そのまま進めれば二重掛けになっていた。
    /// 「効いている場所が 1 つ」を保つのがこのクラスの責務。
    ///
    /// ■ **なぜ AudioMixer を使わないか。**
    /// `AudioMixer` アセットを作る公開 API が無い。生成系は
    /// `UnityEditor.Audio.AudioMixerController`（`CreateMixerControllerAtPath` /
    /// `CreateAudioMixerGroupController` / `CreateAudioMixerSnapshotController`）に
    /// あるが、**すべて internal**。使うにはリフレクションが要り、Unity の更新で
    /// 壊れたときに**コンパイルではなく実行時まで分からない**。
    /// GUI で手作りするのは CLAUDE.md 0-B の「新たに GUI を要する手順を
    /// 持ち込まない」に反する。
    /// **将来 Mixer が必要になったら、この制約が解けたかをまず確認すること。**
    ///
    /// ■ スナップショットの代わり
    /// Mixer を使わないのでスナップショット機能も使えない。ドッキングで
    /// 音がこもる遷移は `AudioMix` の純関数で時間補間する。
    /// **補間曲線を EditMode で数値検証できる**ので、このプロジェクトには
    /// こちらのほうが合っている。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioRouting : MonoBehaviour
    {
        [SerializeField] AudioSource _engine;
        [SerializeField] AudioSource _cockpit;
        [SerializeField] AudioSource _sfx;

        /// <summary>**唯一のローパス。** エンジンにだけ掛かる。</summary>
        [SerializeField] AudioLowPassFilter _engineLowPass;

        double _master = AudioMix.MasterVolume;
        double _engineVolume = AudioMix.EngineVolume;
        double _cockpitVolume = AudioMix.CockpitVolume;
        double _sfxVolume = AudioMix.SfxVolume;

        /// <summary>0 = 飛行 / 1 = ドッキング。時間で補間する。</summary>
        public double Docked01 { get; private set; }

        /// <summary>エンジンの速度連動 (Step 10-3) が書く。0〜1。</summary>
        public double Thrust01 { get; set; }

        public AudioSource Engine => _engine;
        public AudioSource Cockpit => _cockpit;
        public AudioSource Sfx => _sfx;
        public AudioLowPassFilter EngineLowPass => _engineLowPass;

        /// <summary>直近に適用した値。テストと F1 の HUD から見る。</summary>
        public float LastEngineVolume { get; private set; }
        public float LastCockpitVolume { get; private set; }
        public float LastCutoffHz { get; private set; }

        public void Bind(AudioSource engine, AudioSource cockpit, AudioSource sfx,
                         AudioLowPassFilter engineLowPass)
        {
            _engine = engine;
            _cockpit = cockpit;
            _sfx = sfx;
            _engineLowPass = engineLowPass;
        }

        public double VolumeOf(AudioGroup group)
        {
            switch (group)
            {
                case AudioGroup.Master: return _master;
                case AudioGroup.Engine: return _engineVolume;
                case AudioGroup.Cockpit: return _cockpitVolume;
                default: return _sfxVolume;
            }
        }

        /// <summary>F4 のパネルが呼ぶ。**アセットには書き戻さない。**</summary>
        public void SetVolume(AudioGroup group, double value)
        {
            double v = value < AudioMix.MinVolume ? AudioMix.MinVolume
                     : value > AudioMix.MaxVolume ? AudioMix.MaxVolume : value;

            switch (group)
            {
                case AudioGroup.Master: _master = v; break;
                case AudioGroup.Engine: _engineVolume = v; break;
                case AudioGroup.Cockpit: _cockpitVolume = v; break;
                default: _sfxVolume = v; break;
            }
        }

        /// <summary>遷移を飛ばして今すぐ反映する。シナリオの適用時に使う。</summary>
        public void SnapDocked(bool docked)
        {
            Docked01 = docked ? 1.0 : 0.0;
            Apply();
        }

        /// <summary>1 フレームぶん。UniverseRoot.Tick から呼ばれる。</summary>
        public void Tick(bool docked, double deltaSeconds)
        {
            Docked01 = AudioMix.AdvanceDocked(Docked01, docked, deltaSeconds);
            Apply();
        }

        void Apply()
        {
            double engineScale = AudioMix.EngineScale(Docked01);

            if (_engine != null)
            {
                LastEngineVolume = (float)(_master * _engineVolume * engineScale);
                _engine.volume = LastEngineVolume;
            }

            if (_cockpit != null)
            {
                LastCockpitVolume = (float)(_master * _cockpitVolume);
                _cockpit.volume = LastCockpitVolume;
            }

            if (_sfx != null)
            {
                _sfx.volume = (float)(_master * _sfxVolume);
            }

            // **ここがローパスを書く唯一の箇所。**
            LastCutoffHz = (float)AudioMix.CutoffHz(Docked01);
            if (_engineLowPass != null)
            {
                _engineLowPass.cutoffFrequency = LastCutoffHz;
            }
        }

        /// <summary>単発を鳴らす。SFX グループの音量が掛かる。</summary>
        public void PlayOneShot(AudioClip clip, float scale = 1f)
        {
            if (clip == null || _sfx == null)
            {
                return;
            }

            _sfx.PlayOneShot(clip, (float)(_master * _sfxVolume) * scale);
        }
    }
}
