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
        InputAction _cycleTarget;
        InputAction _dockRequest;
        InputAction _undock;
        InputAction[] _jumps;

        // 押下の立ち上がりは自前で持つ。InputAction.WasPressedThisFrame() は
        // Input System 側の更新回数に依存するので、ApplyInput の呼び出しが
        // 入力更新とずれると取りこぼす。
        bool _dialUpHeld;
        bool _dialDownHeld;
        bool _jumpHeld;
        bool _apEngageHeld;
        bool _apCancelHeld;
        bool _cycleHeld;
        bool _dockHeld;
        bool _undockHeld;

        readonly AutopilotSolver _autopilot = new AutopilotSolver();
        readonly DockingSolver _docking = new DockingSolver();

        InputAction _debugHudToggle;
        InputAction _scenarioNext;
        InputAction _scenarioPrev;

        InputAction _debugPanel;
        InputAction _debugUp;
        InputAction _debugDown;
        InputAction _debugLeft;
        InputAction _debugRight;
        InputAction _debugSelect;
        InputAction _debugReset;

        bool _panelHeld;
        bool _upHeld;
        bool _downHeld;
        bool _leftHeld;
        bool _rightHeld;
        bool _selectHeld;
        bool _resetHeld;

        /// <summary>デバッグパネルが開いている間 true。船の操作を止める (Step 8-0b)。</summary>
        public bool DebugPanelOpen { get; set; }

        /// <summary>この Tick で押された (立ち上がりだけ)。パネルが読む。</summary>
        public bool DebugPanelPressed { get; private set; }
        public bool DebugUpPressed { get; private set; }
        public bool DebugDownPressed { get; private set; }
        public bool DebugLeftPressed { get; private set; }
        public bool DebugRightPressed { get; private set; }
        public bool DebugSelectPressed { get; private set; }
        public bool DebugResetPressed { get; private set; }

        bool _hudHeld;
        bool _nextHeld;
        bool _prevHeld;

        /// <summary>この Tick で F1 が押された (押しっぱなしでは 1 回だけ / Step 8-0)。</summary>
        public bool DebugHudTogglePressed { get; private set; }

        /// <summary>この Tick で F2 が押された (Step 8-0)。</summary>
        public bool ScenarioNextPressed { get; private set; }

        /// <summary>この Tick で F3 が押された (Step 8-0)。</summary>
        public bool ScenarioPrevPressed { get; private set; }

        int _targetIndex;
        Vec3d _dockFrom;

        public SpeedDial Dial => _dial;

        public AutopilotSolver Autopilot => _autopilot;

        public DockingSolver Docking => _docking;

        /// <summary>直近のポート正面からのずれ角 [deg] (Step 6)。</summary>
        public float LastAlignmentAngle { get; private set; } = 180f;

        /// <summary>選択中のステーションの番号 (Step 5)。</summary>
        public int TargetIndex => _targetIndex;

        public SpaceStation TargetStation(SolarSystemModel model)
        {
            if (model == null || model.Stations.Count == 0)
            {
                return null;
            }

            return model.Stations[_targetIndex % model.Stations.Count];
        }

        /// <summary>目標を次のステーションへ。</summary>
        public void CycleTarget(SolarSystemModel model)
        {
            if (model == null || model.Stations.Count == 0)
            {
                return;
            }

            _targetIndex = (_targetIndex + 1) % model.Stations.Count;
            _autopilot.Disengage();
        }

        public void SetTargetIndex(int index) => _targetIndex = System.Math.Max(0, index);

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
            _cycleTarget = _flight.FindAction("CycleTarget");
            _dockRequest = _flight.FindAction("DockRequest");
            _debugHudToggle = _flight.FindAction("DebugHudToggle");
            _scenarioNext = _flight.FindAction("ScenarioNext");
            _scenarioPrev = _flight.FindAction("ScenarioPrev");
            _debugPanel = _flight.FindAction("DebugPanel");
            _debugUp = _flight.FindAction("DebugUp");
            _debugDown = _flight.FindAction("DebugDown");
            _debugLeft = _flight.FindAction("DebugLeft");
            _debugRight = _flight.FindAction("DebugRight");
            _debugSelect = _flight.FindAction("DebugSelect");
            _debugReset = _flight.FindAction("DebugReset");
            _undock = _flight.FindAction("Undock");

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
            input.CycleTarget = IsDown(_cycleTarget);
            input.DockRequest = IsDown(_dockRequest);
            input.ToggleDebugHud = IsDown(_debugHudToggle);
            input.ScenarioNext = IsDown(_scenarioNext);
            input.ScenarioPrev = IsDown(_scenarioPrev);
            input.DebugPanelToggle = IsDown(_debugPanel);
            input.DebugUp = IsDown(_debugUp);
            input.DebugDown = IsDown(_debugDown);
            input.DebugLeft = IsDown(_debugLeft);
            input.DebugRight = IsDown(_debugRight);
            input.DebugSelect = IsDown(_debugSelect);
            input.DebugReset = IsDown(_debugReset);
            input.Undock = IsDown(_undock);

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
            // 押しっぱなしで連射しないよう、立ち上がりだけ拾う (Step 8-0)。
            DebugHudTogglePressed = input.ToggleDebugHud && !_hudHeld;
            ScenarioNextPressed = input.ScenarioNext && !_nextHeld;
            ScenarioPrevPressed = input.ScenarioPrev && !_prevHeld;
            _hudHeld = input.ToggleDebugHud;
            _nextHeld = input.ScenarioNext;
            _prevHeld = input.ScenarioPrev;

            // デバッグパネル (Step 8-0b)。
            DebugPanelPressed = input.DebugPanelToggle && !_panelHeld;
            DebugUpPressed = input.DebugUp && !_upHeld;
            DebugDownPressed = input.DebugDown && !_downHeld;
            DebugLeftPressed = input.DebugLeft && !_leftHeld;
            DebugRightPressed = input.DebugRight && !_rightHeld;
            DebugSelectPressed = input.DebugSelect && !_selectHeld;
            DebugResetPressed = input.DebugReset && !_resetHeld;
            _panelHeld = input.DebugPanelToggle;
            _upHeld = input.DebugUp;
            _downHeld = input.DebugDown;
            _leftHeld = input.DebugLeft;
            _rightHeld = input.DebugRight;
            _selectHeld = input.DebugSelect;
            _resetHeld = input.DebugReset;

            // **パネルが開いている間は船を動かさない。**
            // Space (前進) と R (ダイヤル増) をパネルが使うため。
            // 閉じれば元どおり。F4 を押さなければここは通らない。
            if (DebugPanelOpen)
            {
                input = FlightInput.None;
            }

            if (root == null || _shipTransform == null)
            {
                return;
            }

            HandleDial(root, input);
            HandleJump(root, input);

            if (input.CycleTarget && !_cycleHeld)
            {
                CycleTarget(root.Model);
                root.Audio?.PlaySound(SoundId.UiSelect);
            }

            _cycleHeld = input.CycleTarget;

            bool dockPressed = input.DockRequest && !_dockHeld;
            bool undockPressed = input.Undock && !_undockHeld;
            _dockHeld = input.DockRequest;
            _undockHeld = input.Undock;

            StepDocking(root, dockPressed, undockPressed, realDeltaSeconds);

            // 補間中は state machine が船を握る。手動もオートパイロットも受け付けない。
            if (_docking.ControlsShip)
            {
                return;
            }

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

        void StepDocking(UniverseRoot root, bool dockPressed, bool undockPressed, double dt)
        {
            SpaceStation station = TargetStation(root.Model);
            if (station == null)
            {
                return;
            }

            // **要求の判定はポート位置から測る (Step 13-3b)。**
            // 中心から測っていたときは、ポートのオフセット（Cobble で 0.19775 units）と
            // PortStandoff（0.015）ぶんの下駄が RequestRange に乗っていた。
            double distance = station.DistanceFromPort(root.Ship.Position);

            // 機首とポート正面のなす角。ポートは深宇宙側を向いているので、
            // 船はその逆を向いて寄る。
            Vec3d port = station.PortDirection;
            var facing = new Vector3((float)-port.X, (float)-port.Y, (float)-port.Z);
            float angle = Vector3.Angle(_shipTransform.forward, facing);

            DockingState before = _docking.State;
            LastAlignmentAngle = angle;
            // **要求可能距離は定義から (Step 13-1a)。** グローバル定数を読まない。
            _docking.Step(distance, root.Ship.SpeedKmPerSec, angle, dockPressed, undockPressed, dt,
                          station.RequestRangeUnits);

            if (before != DockingState.Docking && _docking.State == DockingState.Docking)
            {
                _dockFrom = root.Ship.Position;
                _autopilot.Disengage();
            }

            if (before != DockingState.Undocking && _docking.State == DockingState.Undocking)
            {
                _dockFrom = root.Ship.Position;
            }

            // ドッキング完了時にステーション名だけ保存する (Step 7)。
            if (before != DockingState.Docked && _docking.State == DockingState.Docked)
            {
                SaveFile.Save(station.Name);
            }

            switch (_docking.State)
            {
                case DockingState.Docking:
                    root.Ship.SetVelocity(Vec3d.Zero);
                    root.Ship.SetPosition(DockingSolver.Interpolate(_dockFrom, station.PortPosition, _docking.Progress));
                    AimAt(facing);
                    break;

                case DockingState.Docked:
                    root.Ship.SetVelocity(Vec3d.Zero);
                    root.Ship.SetPosition(station.PortPosition);
                    break;

                case DockingState.Undocking:
                {
                    // ポートの正面へ離脱する。
                    Vec3d away = station.AbsolutePosition
                                 + port * (station.PortStandoffKm
                                           + DockingSolver.UndockDistance(station.RequestRangeUnits));
                    root.Ship.SetVelocity(Vec3d.Zero);
                    root.Ship.SetPosition(DockingSolver.Interpolate(_dockFrom, away, _docking.Progress));
                    break;
                }
            }
        }

        void AimAt(Vector3 forward)
        {
            if (forward.sqrMagnitude > 0f)
            {
                _shipTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        void HandleAutopilotInput(UniverseRoot root, FlightInput input)
        {
            if (input.AutopilotEngage && !_apEngageHeld)
            {
                SpaceStation station = TargetStation(root.Model);
                EngageAutopilot(root, station != null
                    ? station.PortPosition
                    : root.Model.Mars.AbsolutePosition);
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

        void HandleDial(UniverseRoot root, FlightInput input)
        {
            if (input.DialUp && !_dialUpHeld)
            {
                _dial.Shift(+1);
                root.Audio?.PlaySound(SoundId.UiSelect);
            }

            _dialUpHeld = input.DialUp;

            if (input.DialDown && !_dialDownHeld)
            {
                _dial.Shift(-1);
                root.Audio?.PlaySound(SoundId.UiSelect);
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
            _docking.Reset();
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
