using SolarSystem.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 手動操作 (Step 3a)。
    ///
    /// **Update() を持たない。** UniverseRoot.Tick の先頭から呼ばれる (決定 D-1)。
    ///
    /// 構造:
    ///   ReadInput()  … Input System から読むだけ。ここだけが InputSystem に依存する。
    ///   Apply()      … 制御ロジック。素の FlightInput しか見ないのでテストできる。
    ///
    /// 姿勢は float の Quaternion で Transform が持つ (決定 D-4)。
    /// 絶対位置と速度だけが Core の double。
    ///
    /// Step 3a の割り切り:
    ///   - 加減速なし。速度 = 前方 x ダイヤル値 x スラスト。離すと即停止する。
    ///     慣性と制動プロファイルは Step 3b。
    ///   - オートパイロット・ETA なし (Step 3b)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipRig : MonoBehaviour
    {
        public const float MouseDegreesPerPixel = 0.12f;
        public const float KeyDegreesPerSecond = 45f;
        public const float RollDegreesPerSecond = 60f;

        /// <summary>Align 中に機首を振る速さ [deg/s]。</summary>
        public const float AlignDegreesPerSecond = 90f;

        [SerializeField] InputActionAsset _actions;
        [SerializeField] Transform _shipTransform;

        readonly SpeedDial _dial = new SpeedDial();

        InputActionMap _flight;
        InputAction _lookMouse;
        InputAction _lookKeys;
        InputAction _roll;
        InputAction _thrust;
        InputAction _dialUp;
        InputAction _dialDown;
        InputAction _apEngage;
        InputAction _apCancel;
        InputAction[] _jumps;

        // 押下の立ち上がりは自前で持つ。InputAction.WasPressedThisFrame() は
        // Input System 側の更新回数に依存するので、ApplyInput の呼び出しが
        // 入力更新とずれると取りこぼす。
        bool _dialUpHeld;
        bool _dialDownHeld;
        bool _jumpHeld;
        bool _apEngageHeld;
        bool _apCancelHeld;

        readonly AutopilotSolver _autopilot = new AutopilotSolver();

        public SpeedDial Dial => _dial;

        public AutopilotSolver Autopilot => _autopilot;

        /// <summary>直近に適用したスラスト入力 (-1..1)。</summary>
        public float LastThrust { get; private set; }

        /// <summary>直近に踏んだデバッグジャンプの段 (未使用なら -1)。</summary>
        public int LastJumpIndex { get; private set; } = -1;

        public Transform ShipTransform => _shipTransform;

        /// <summary>
        /// 入力の差し替え。テストが本番と同じ経路 (Tick -> ApplyInput -> ReadInput -> Apply)
        /// を通すために使う。null なら Input System から読む。
        /// </summary>
        public FlightInput? InputOverride { get; set; }

        public void Bind(InputActionAsset actions, Transform shipTransform)
        {
            _actions = actions;
            _shipTransform = shipTransform;
        }

        void OnEnable() => Resolve();

        void OnDisable() => _flight?.Disable();

        /// <summary>アクションを解決して有効化する。</summary>
        public void Resolve()
        {
            if (_actions == null)
            {
                return;
            }

            _flight = _actions.FindActionMap("Flight", throwIfNotFound: false);
            if (_flight == null)
            {
                Debug.LogWarning("[ShipRig] Flight アクションマップが見つからない。");
                return;
            }

            _lookMouse = _flight.FindAction("LookMouse");
            _lookKeys = _flight.FindAction("LookKeys");
            _roll = _flight.FindAction("Roll");
            _thrust = _flight.FindAction("Thrust");
            _dialUp = _flight.FindAction("DialUp");
            _dialDown = _flight.FindAction("DialDown");
            _apEngage = _flight.FindAction("AutopilotEngage");
            _apCancel = _flight.FindAction("AutopilotCancel");

            _jumps = new InputAction[DebugJumpTable.Count];
            for (int i = 0; i < _jumps.Length; i++)
            {
                _jumps[i] = _flight.FindAction($"Jump{i + 1}");
            }

            _flight.Enable();
        }

        /// <summary>Input System から 1 フレームぶんを読む。ここだけが InputSystem に依存する。</summary>
        public FlightInput ReadInput()
        {
            if (InputOverride.HasValue)
            {
                return InputOverride.Value;
            }

            var input = FlightInput.None;
            if (_flight == null)
            {
                return input;
            }

            if (_lookMouse != null) input.LookMouse = _lookMouse.ReadValue<Vector2>();
            if (_lookKeys != null) input.LookKeys = _lookKeys.ReadValue<Vector2>();
            if (_roll != null) input.Roll = _roll.ReadValue<float>();
            if (_thrust != null) input.Thrust = _thrust.ReadValue<float>();

            input.DialUp = IsDown(_dialUp);
            input.DialDown = IsDown(_dialDown);
            input.AutopilotEngage = IsDown(_apEngage);
            input.AutopilotCancel = IsDown(_apCancel);

            if (_jumps != null)
            {
                for (int i = 0; i < _jumps.Length; i++)
                {
                    if (IsDown(_jumps[i]))
                    {
                        input.JumpIndex = i;
                        break;
                    }
                }
            }

            return input;
        }

        static bool IsDown(InputAction action) => action != null && action.ReadValue<float>() > 0.5f;

        /// <summary>UniverseRoot.Tick から呼ばれる入口。</summary>
        public void ApplyInput(UniverseRoot root, double realDeltaSeconds)
            => Apply(root, ReadInput(), realDeltaSeconds);

        /// <summary>
        /// 制御ロジック本体。Input System には触れない。
        /// テストはここへ素の FlightInput を渡す。
        /// </summary>
        public void Apply(UniverseRoot root, FlightInput input, double realDeltaSeconds)
        {
            if (root == null || _shipTransform == null)
            {
                return;
            }

            HandleDial(input);
            HandleJump(root, input);
            HandleAutopilotInput(root, input);

            if (_autopilot.IsEngaged)
            {
                StepAutopilot(root, realDeltaSeconds);
            }
            else
            {
                HandleAttitude(input, (float)realDeltaSeconds);
                HandleVelocity(root, input);
            }
        }

        void HandleAutopilotInput(UniverseRoot root, FlightInput input)
        {
            if (input.AutopilotEngage && !_apEngageHeld)
            {
                EngageAutopilot(root, root.Model.Mars.AbsolutePosition);
            }

            _apEngageHeld = input.AutopilotEngage;

            bool cancel = input.AutopilotCancel
                          || (_autopilot.IsEngaged && input.HasManualActivity);

            if (cancel && !_apCancelHeld)
            {
                // 手動入力が入ったら解除する。車の ACC と同じで説明不要にわかる。
                // (要決定にはしない。実装しながらの最小の選択)
                _autopilot.Disengage();
            }

            _apCancelHeld = cancel;
        }

        /// <summary>オートパイロットを起動する。既定巡航速度は 0.9c (決定 D-8)。</summary>
        public void EngageAutopilot(UniverseRoot root, Vec3d target)
        {
            _autopilot.Engage(
                root.Ship.Position,
                target,
                UniverseConstants.BetaToKmPerSec(UniverseConstants.DefaultCruiseBeta));
        }

        void StepAutopilot(UniverseRoot root, double dt)
        {
            Vec3d toTarget = _autopilot.TargetPosition - root.Ship.Position;
            Vec3d dir = toTarget.Normalized;
            var forwardTarget = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);

            float angle = forwardTarget.sqrMagnitude > 0f
                ? Vector3.Angle(_shipTransform.forward, forwardTarget)
                : 0f;

            _autopilot.Step(root.Ship.Position, angle, dt);

            // 機首を目標へ振る。Align 中も Cruise 中も向け続ける。
            if (forwardTarget.sqrMagnitude > 0f)
            {
                _shipTransform.rotation = Quaternion.RotateTowards(
                    _shipTransform.rotation,
                    Quaternion.LookRotation(forwardTarget, Vector3.up),
                    AlignDegreesPerSecond * (float)dt);
            }

            LastThrust = _autopilot.CommandedSpeedKmPerSec > 0.0 ? 1f : 0f;
            root.Ship.SetVelocity(dir * _autopilot.CommandedSpeedKmPerSec);
        }

        void HandleDial(FlightInput input)
        {
            if (input.DialUp && !_dialUpHeld)
            {
                _dial.Shift(+1);
            }

            _dialUpHeld = input.DialUp;

            if (input.DialDown && !_dialDownHeld)
            {
                _dial.Shift(-1);
            }

            _dialDownHeld = input.DialDown;
        }

        void HandleJump(UniverseRoot root, FlightInput input)
        {
            bool held = input.JumpIndex >= 0;
            if (held && !_jumpHeld)
            {
                JumpTo(root, input.JumpIndex);
            }

            _jumpHeld = held;
        }

        /// <summary>デバッグジャンプ。火星から指定距離の地点へ即座に置き直す。</summary>
        public void JumpTo(UniverseRoot root, int index)
        {
            LastJumpIndex = index;
            _dial.Stop();
            _autopilot.Disengage();
            root.PlaceObserver(DebugJumpTable.PositionForIndex(root.Model, index));
            LookAtMars(root);
        }

        /// <summary>火星の方を向く。ジャンプ直後に画面外を向いていると確認できないため。</summary>
        public void LookAtMars(UniverseRoot root)
        {
            Vec3d dir = root.Model.Mars.DirectionFrom(root.Ship.Position);
            var forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            if (forward.sqrMagnitude > 0f)
            {
                _shipTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        void HandleAttitude(FlightInput input, float dt)
        {
            float yaw = input.LookMouse.x * MouseDegreesPerPixel
                        + input.LookKeys.x * KeyDegreesPerSecond * dt;
            float pitch = -input.LookMouse.y * MouseDegreesPerPixel
                          - input.LookKeys.y * KeyDegreesPerSecond * dt;
            float roll = -input.Roll * RollDegreesPerSecond * dt;

            if (pitch != 0f || yaw != 0f || roll != 0f)
            {
                _shipTransform.Rotate(pitch, yaw, roll, Space.Self);
            }
        }

        void HandleVelocity(UniverseRoot root, FlightInput input)
        {
            LastThrust = input.Thrust;

            double speed = _dial.Current.KmPerSec * input.Thrust;
            Vector3 f = _shipTransform.forward;
            root.Ship.SetVelocity(new Vec3d(f.x, f.y, f.z) * speed);
        }
    }
}
