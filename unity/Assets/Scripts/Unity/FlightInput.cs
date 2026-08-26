using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 1 フレームぶんの操縦入力。Input System から読み取った結果をここへ詰めて
    /// ShipRig.Apply へ渡す。
    ///
    /// **こうしてある理由:** Input System を ShipRig の内部で直接読むと、
    /// 制御ロジック (ダイヤルの立ち上がり、ジャンプ、姿勢、速度) をテストするのに
    /// 実機のキー入力が要る。batchmode の PlayMode では Input System の
    /// プレイヤー側状態が更新されず、QueueStateEvent も InputState.Change も
    /// 効かなかった (実測)。入力の読み取りを端 1 箇所に閉じ込めておけば、
    /// ロジックは EditMode / PlayMode の両方から素の値で叩ける。
    /// </summary>
    public struct FlightInput
    {
        /// <summary>マウス移動量 [px]。x=ヨー / y=ピッチ。</summary>
        public Vector2 LookMouse;

        /// <summary>キーによる旋回 (-1..1)。x=ヨー / y=ピッチ。</summary>
        public Vector2 LookKeys;

        /// <summary>ロール (-1..1)。</summary>
        public float Roll;

        /// <summary>前進 (-1..1)。ダイヤル値に掛かる。</summary>
        public float Thrust;

        public bool DialUp;

        public bool DialDown;

        /// <summary>デバッグジャンプの段 (0 起点)。押されていなければ -1。</summary>
        public int JumpIndex;

        /// <summary>オートパイロット起動 (Step 3b)。</summary>
        public bool AutopilotEngage;

        /// <summary>オートパイロット解除 (Step 3b)。</summary>
        public bool AutopilotCancel;

        /// <summary>目標を次のステーションへ切り替える (Step 5)。</summary>
        public bool CycleTarget;

        /// <summary>ドッキング要求 (Step 5)。</summary>
        public bool DockRequest;

        /// <summary>出港 / ドッキング要求の取り消し (Step 5)。</summary>
        public bool Undock;

        /// <summary>デバッグ HUD の表示を反転する (F1 / Step 8-0)。</summary>
        public bool ToggleDebugHud;

        /// <summary>次のシナリオへ (F2 / Step 8-0)。</summary>
        public bool ScenarioNext;

        /// <summary>前のシナリオへ (F3 / Step 8-0)。</summary>
        public bool ScenarioPrev;

        /// <summary>手動の操作入力があるか。オートパイロットの解除条件に使う。</summary>
        public bool HasManualActivity =>
            Thrust != 0f || Roll != 0f
            || LookKeys.sqrMagnitude > 0f || LookMouse.sqrMagnitude > 0f;

        public static FlightInput None => new FlightInput { JumpIndex = -1 };

        public static FlightInput Jump(int index) => new FlightInput { JumpIndex = index };

        public static FlightInput Forward(float thrust) =>
            new FlightInput { Thrust = thrust, JumpIndex = -1 };
    }
}
