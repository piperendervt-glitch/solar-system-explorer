using System;

namespace SolarSystem.Core
{
    /// <summary>音のグループ (Step 10-2)。**Mixer は使わない。理由は AudioRouting の doc を参照。**</summary>
    public enum AudioGroup
    {
        /// <summary>全体。他の 3 つに掛かる。</summary>
        Master,

        /// <summary>エンジン。船内に伝わる振動。**ローパスが掛かる唯一のグループ。**</summary>
        Engine,

        /// <summary>コックピットの環境音。船内に常時ある低いうなり。</summary>
        Cockpit,

        /// <summary>単発。ドッキングの金属音（船体を伝わる振動）と、UI / 警告（船内スピーカー）。</summary>
        Sfx,
    }

    /// <summary>
    /// 音量とローパスの定義 (Step 10-2 / 10-5)。**UnityEngine 非依存。**
    ///
    /// **「宇宙は無音が正しい」（計画書 §7）を選んでいるので、鳴る音はすべて船内音。**
    /// ドッキングの金属音は船体を伝わる振動、UI と警告は船内スピーカー、
    /// エンジン音は船内に伝わる振動、という整理でグループを切っている。
    /// だから全て 2D 再生で、距離減衰も定位も無い。
    ///
    /// **UI は SFX に統合した。** 船内スピーカーから鳴るという整理では
    /// UI と警告は同じ経路なので、グループを分ける理由が無い。
    /// </summary>
    public static class AudioMix
    {
        /// <summary>グループの数。Master を含む。</summary>
        public const int GroupCount = 4;

        // ---- 既定音量 ----
        //
        // **実機で耳で決めた (2026-08-27)。** F4 の数値項目で振れる。
        // ループ 2 本は「鳴っていることに気付かない」程度まで下げている。
        // 船内の環境音なので、意識に上がると航行の邪魔になる。

        public const double MasterVolume = 1.00;

        /// <summary>エンジン。**0.55 では大きすぎた。**</summary>
        public const double EngineVolume = 0.10;

        /// <summary>コックピット。エンジンよりさらに奥に置く。</summary>
        public const double CockpitVolume = 0.05;

        /// <summary>
        /// 単発（ドッキング / 出港 / UI / 警告）。
        /// **これだけ暫定値。** 10-1 / 10-2 の時点では単発を鳴らす配線が
        /// まだ無く（10-4）、耳で判断できないため据え置いた。
        /// **10-4 で実際に鳴らしてから決め直すこと。**
        /// </summary>
        public const double SfxVolume = 0.70;

        /// <summary>音量の下限 / 上限。パネルの範囲もこれに合わせる。</summary>
        public const double MinVolume = 0.0;
        public const double MaxVolume = 2.0;

        // ---- ローパス (Step 10-5) ----
        //
        // **Mixer のスナップショットは使わない。** Mixer 自体を使っていないため。
        // 代わりにカットオフと音量をコード側で時間補間する。
        // **補間そのものを EditMode で数値検証できるので、このプロジェクトには
        // こちらのほうが合っている。**

        /// <summary>飛行中のカットオフ [Hz]。ほぼ素通し。</summary>
        public const double FlyingCutoffHz = 12000.0;

        /// <summary>ドッキング中のカットオフ [Hz]。こもらせる。</summary>
        public const double DockedCutoffHz = 2000.0;

        /// <summary>ドッキング中のエンジン音量の倍率。「エンジンが休んでいる」感。</summary>
        public const double DockedEngineScale = 0.35;

        /// <summary>飛行 ⇔ ドッキングの遷移にかける秒数。</summary>
        public const double TransitionSeconds = 1.5;

        public static double DefaultVolume(AudioGroup group)
        {
            switch (group)
            {
                case AudioGroup.Master: return MasterVolume;
                case AudioGroup.Engine: return EngineVolume;
                case AudioGroup.Cockpit: return CockpitVolume;
                case AudioGroup.Sfx: return SfxVolume;
                default: throw new ArgumentOutOfRangeException(nameof(group));
            }
        }

        /// <summary>
        /// 遷移の進み具合を 0〜1 で進める。
        /// docked が true なら 1 へ、false なら 0 へ、TransitionSeconds かけて動く。
        /// </summary>
        public static double AdvanceDocked(double current, bool docked, double deltaSeconds)
        {
            if (TransitionSeconds <= 0.0)
            {
                return docked ? 1.0 : 0.0;
            }

            double step = deltaSeconds / TransitionSeconds;
            double target = docked ? 1.0 : 0.0;

            if (current < target)
            {
                return Math.Min(target, current + step);
            }

            return Math.Max(target, current - step);
        }

        /// <summary>
        /// 遷移の進み具合からカットオフ [Hz] を出す。
        ///
        /// **対数で補間する。** 線形だと 12000 → 2000 の前半でほとんど変化が
        /// 聴こえず、終わり際に急にこもる。周波数は耳に対して対数なので、
        /// 対数で補間したほうが均一に聴こえる。
        /// </summary>
        public static double CutoffHz(double docked01)
        {
            double t = Clamp01(docked01);
            double a = Math.Log(FlyingCutoffHz);
            double b = Math.Log(DockedCutoffHz);
            return Math.Exp(a + (b - a) * Smooth(t));
        }

        /// <summary>遷移の進み具合からエンジン音量の倍率を出す。</summary>
        public static double EngineScale(double docked01)
        {
            double t = Smooth(Clamp01(docked01));
            return 1.0 + (DockedEngineScale - 1.0) * t;
        }

        /// <summary>両端で速度 0 になる補間。遷移の開始と終了を目立たなくする。</summary>
        public static double Smooth(double t)
        {
            double x = Clamp01(t);
            return x * x * (3.0 - 2.0 * x);
        }

        public static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
