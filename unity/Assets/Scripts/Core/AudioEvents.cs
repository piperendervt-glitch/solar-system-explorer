namespace SolarSystem.Core
{
    /// <summary>鳴らす単発音 (Step 10-4)。</summary>
    public enum SoundId
    {
        /// <summary>何も鳴らさない。</summary>
        None,

        /// <summary>接岸しきった瞬間の金属音。船体を伝わる振動。</summary>
        DockImpact,

        /// <summary>出港の操作が受理された瞬間。船内スピーカー。</summary>
        Undock,

        /// <summary>目標切替・速度ダイヤル。船内スピーカー。</summary>
        UiSelect,

        /// <summary>ドッキング要求が受理された瞬間。船内スピーカー。</summary>
        UiConfirm,

        /// <summary>ドッキング要求が拒否された瞬間。船内スピーカー。</summary>
        Warning,
    }

    /// <summary>
    /// イベント音の発火表 (Step 10-4)。**UnityEngine 非依存の純関数。**
    ///
    /// ■ **状態遷移のエッジで鳴らす。**
    /// 「今この状態か」で鳴らすと、`Docking` のように 5 秒間毎フレーム真になる
    /// 状態で連続発火する。**前フレームと今フレームの組**で判定すれば 1 回で済む。
    ///
    /// ■ どの遷移で鳴らすかの根拠（Step 5 の 6 状態を見たうえで決めた）
    ///
    ///   Approaching -> DockRequested   要求が受理された     UiConfirm
    ///   Docking     -> Docked          **接岸しきった瞬間** DockImpact
    ///   Docked      -> Undocking       **出港の操作が受理された瞬間** Undock
    ///
    /// `DockRequested -> Docking` は「接岸の開始」で、5 秒の補間が始まるだけ。
    /// 金属音は接岸しきった側が自然。
    /// `Undocking -> Free` は 5 秒後に離れきった瞬間で、スイッチ音には遅すぎる。
    /// `switch_004` はスイッチを入れる音なので開始側に置く。
    /// </summary>
    public static class AudioEvents
    {
        /// <summary>
        /// 警告の最小間隔 [秒]。**これ未満の連続は捨てる。**
        /// 条件を満たさないまま要求を連打されると耳障りなため。
        /// 他の音は重ねてよい（`select_001` は 0.043 秒しかなく実質重ならない）。
        /// </summary>
        public const double WarningMinIntervalSeconds = 0.15;

        /// <summary>状態遷移から鳴らす音を決める。**遷移が無ければ None。**</summary>
        public static SoundId OnTransition(DockingState previous, DockingState current)
        {
            if (previous == current)
            {
                return SoundId.None;
            }

            if (previous == DockingState.Approaching && current == DockingState.DockRequested)
            {
                return SoundId.UiConfirm;
            }

            if (previous == DockingState.Docking && current == DockingState.Docked)
            {
                return SoundId.DockImpact;
            }

            if (previous == DockingState.Docked && current == DockingState.Undocking)
            {
                return SoundId.Undock;
            }

            return SoundId.None;
        }

        /// <summary>
        /// 最小間隔を守れているか。守れていなければ鳴らさない。
        /// <paramref name="sinceLastSeconds"/> が負なら「まだ一度も鳴っていない」。
        /// </summary>
        public static bool CanPlay(SoundId sound, double sinceLastSeconds)
        {
            if (sound != SoundId.Warning)
            {
                return true; // 他は重ねてよい
            }

            return sinceLastSeconds < 0.0 || sinceLastSeconds >= WarningMinIntervalSeconds;
        }
    }
}
