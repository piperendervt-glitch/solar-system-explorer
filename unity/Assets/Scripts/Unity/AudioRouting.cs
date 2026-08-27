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

        // ---- 単発クリップ (Step 10-4) ----
        [SerializeField] AudioClip _dockImpact;
        [SerializeField] AudioClip _undock;
        [SerializeField] AudioClip _uiSelect;
        [SerializeField] AudioClip _uiConfirm;
        [SerializeField] AudioClip _warning;

        /// <summary>音ごとの発音回数。**診断表示 (F1) とテストが見る。**</summary>
        readonly int[] _playCounts = new int[System.Enum.GetValues(typeof(SoundId)).Length];

        /// <summary>音ごとの最後に鳴らした時刻 [秒]。最小間隔の判定に使う。</summary>
        readonly double[] _lastPlayedAt = new double[System.Enum.GetValues(typeof(SoundId)).Length];

        double _now;

        double _master = AudioMix.MasterVolume;
        double _engineVolume = AudioMix.EngineVolume;
        double _cockpitVolume = AudioMix.CockpitVolume;
        double _sfxVolume = AudioMix.SfxVolume;

        /// <summary>0 = 飛行 / 1 = ドッキング。時間で補間する。</summary>
        public double Docked01 { get; private set; }

        /// <summary>スラスト 0〜1。UniverseRoot が毎フレーム書く。</summary>
        public double Thrust01 { get; set; }

        /// <summary>制動中か。UniverseRoot が毎フレーム書く。</summary>
        public bool Braking { get; set; }

        /// <summary>スラスト連動 (Step 10-3)。**音量は係数、pitch は絶対値。**</summary>
        public EngineAudioModel EngineModel { get; } = new EngineAudioModel();

        /// <summary>一次遅れの時定数 [秒]。F4 が書き換える。</summary>
        public double EngineLagSeconds { get; set; } = AudioMix.EngineLagSeconds;

        public AudioSource Engine => _engine;
        public AudioSource Cockpit => _cockpit;
        public AudioSource Sfx => _sfx;
        public AudioLowPassFilter EngineLowPass => _engineLowPass;

        /// <summary>直近に適用した値。テストと F1 の HUD から見る。</summary>
        public float LastEngineVolume { get; private set; }
        public float LastCockpitVolume { get; private set; }
        public float LastCutoffHz { get; private set; }
        public float LastEnginePitch { get; private set; }

        // ---- 発音の記録 (Step 10-4) ----
        //
        // **診断表示として持つ。** 音は絵と違って「鳴ったかどうか」を目で
        // 確かめられないので、F1 の HUD に出して画面で裏を取れるようにする。
        // 診断表示として存在するので、テストがこれを見るのは自然。
        // テストのためだけの状態を本番コードに持たせる形を避けられる。

        /// <summary>直近に鳴らした音。まだ何も鳴っていなければ None。</summary>
        public SoundId LastSound { get; private set; } = SoundId.None;

        /// <summary>直近に鳴らした音の音量。</summary>
        public float LastSoundVolume { get; private set; }

        /// <summary>音ごとの発音回数。</summary>
        public int PlayCount(SoundId sound) => _playCounts[(int)sound];

        /// <summary>全ての発音回数の合計。</summary>
        public int TotalPlayCount
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _playCounts.Length; i++) { sum += _playCounts[i]; }
                return sum;
            }
        }

        public void Bind(AudioSource engine, AudioSource cockpit, AudioSource sfx,
                         AudioLowPassFilter engineLowPass)
        {
            _engine = engine;
            _cockpit = cockpit;
            _sfx = sfx;
            _engineLowPass = engineLowPass;
        }

        /// <summary>単発クリップを渡す (Step 10-4)。</summary>
        public void BindClips(AudioClip dockImpact, AudioClip undock,
                              AudioClip uiSelect, AudioClip uiConfirm, AudioClip warning)
        {
            _dockImpact = dockImpact;
            _undock = undock;
            _uiSelect = uiSelect;
            _uiConfirm = uiConfirm;
            _warning = warning;
        }

        public AudioClip ClipOf(SoundId sound)
        {
            switch (sound)
            {
                case SoundId.DockImpact: return _dockImpact;
                case SoundId.Undock: return _undock;
                case SoundId.UiSelect: return _uiSelect;
                case SoundId.UiConfirm: return _uiConfirm;
                case SoundId.Warning: return _warning;
                default: return null;
            }
        }

        /// <summary>
        /// 単発を鳴らす (Step 10-4)。
        ///
        /// **最小間隔を守れないものは捨てる**（警告のみ。AudioEvents.CanPlay）。
        /// 他は重ねてよい。select_001 は 0.043 秒しかなく実質重ならない。
        /// </summary>
        public bool PlaySound(SoundId sound)
        {
            if (sound == SoundId.None)
            {
                return false;
            }

            var index = (int)sound;
            double since = _playCounts[index] == 0 ? -1.0 : _now - _lastPlayedAt[index];
            if (!AudioEvents.CanPlay(sound, since))
            {
                return false;
            }

            AudioClip clip = ClipOf(sound);
            if (clip == null || _sfx == null)
            {
                return false;
            }

            var volume = (float)(_master * _sfxVolume);
            _sfx.PlayOneShot(clip, volume);

            _playCounts[index]++;
            _lastPlayedAt[index] = _now;
            LastSound = sound;
            LastSoundVolume = volume;
            return true;
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
            EngineModel.Snap(Thrust01, Braking);
            Apply();
        }

        /// <summary>1 フレームぶん。UniverseRoot.Tick から呼ばれる。</summary>
        public void Tick(bool docked, double deltaSeconds)
        {
            _now += deltaSeconds;
            Docked01 = AudioMix.AdvanceDocked(Docked01, docked, deltaSeconds);
            EngineModel.Advance(Thrust01, Braking, deltaSeconds, EngineLagSeconds);
            Apply();
        }

        void Apply()
        {
            double engineScale = AudioMix.EngineScale(Docked01);

            if (_engine != null)
            {
                // **音量の掛け算はここだけ。** EngineAudioModel が返すのは係数で、
                // 全開で 1.0 になるよう正規化されている (Step 10-3)。
                LastEngineVolume =
                    (float)(_master * _engineVolume * engineScale * EngineModel.VolumeScale);
                _engine.volume = LastEngineVolume;

                LastEnginePitch = (float)EngineModel.Pitch;
                _engine.pitch = LastEnginePitch;
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
