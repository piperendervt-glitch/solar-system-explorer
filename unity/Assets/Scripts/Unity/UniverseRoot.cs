using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// Core のモデルを保持し、毎フレーム 1 ステップ進める唯一の入口
    /// (docs/01-architecture.md §1-4 / 決定 D-1)。
    ///
    /// **このクラスだけが Update() を持つ。** 他のアダプタは全部ここから呼ばれる。
    /// 呼び出し順がコードに書いてあるので EditMode テストで検証できる。
    ///
    /// 毎フレームの順序:
    ///   1. 固定ステップ数を決める (UniverseClock)
    ///   2. その回数だけ絶対座標を積分する (AbsoluteMotion)
    ///   3. 原点を船の絶対位置へ据え直す (FloatingOrigin)
    ///   4. 全 ShiftableBody に原点相対座標を書き出す (OriginShiftDriver)
    ///   5. 船の Transform を原点に固定する
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UniverseRoot : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] OriginShiftDriver _shiftDriver;
        [SerializeField] Transform _shipTransform;
        [SerializeField] SolarSystemView _solarSystemView;
        [SerializeField] SunLightAimer _sunLightAimer;
        [SerializeField] ShipRig _shipRig;
        [SerializeField] InstrumentPanel _instruments;
        [SerializeField] StationViewSet _stations;
        [SerializeField] PostProcessPreset _post;
        [SerializeField] EngineAudio _engineAudio;
        [SerializeField] DebugOverlay _overlay;
        [SerializeField] ScenarioRunner _scenario;
        [SerializeField] CockpitShake _shake;
        [SerializeField] CameraStackController _stack;
        [SerializeField] SunFlareController _sunFlare;

        [Header("Step 1 の初期速度 (km/s)。既定は 0.9c を +Z へ)")]
        [SerializeField] double _initialVelocityX;
        [SerializeField] double _initialVelocityY;
        [SerializeField] double _initialVelocityZ =
            UniverseConstants.DefaultCruiseBeta * UniverseConstants.SpeedOfLightKmPerSec;

        public UniverseClock Clock { get; private set; }
        public FloatingOrigin Origin { get; private set; }
        public AbsoluteMotion Ship { get; private set; }
        public SolarSystemModel Model { get; private set; }

        public OriginShiftDriver ShiftDriver => _shiftDriver;
        public SolarSystemView SolarSystem => _solarSystemView;
        public SunLightAimer SunLight => _sunLightAimer;
        public ShipRig Rig => _shipRig;
        public InstrumentPanel Instruments => _instruments;
        public StationViewSet Stations => _stations;
        public PostProcessPreset Post => _post;
        public EngineAudio EngineAudio => _engineAudio;
        public DebugOverlay Overlay => _overlay;
        public ScenarioRunner Scenario => _scenario;
        public CockpitShake Shake => _shake;
        public SunFlareController SunFlare => _sunFlare;

        /// <summary>太陽方向の上書き (Step 8-0)。null ならモデルの計算値。</summary>
        public Vec3d? SunDirectionOverride { get; private set; }

        public void SetSunDirectionOverride(Vec3d? direction) => SunDirectionOverride = direction;

        /// <summary>時刻を差し替える (シナリオの初期状態 / Step 8-0)。</summary>
        public void SetElapsedSeconds(double seconds) => Clock?.SetElapsedSeconds(seconds);

        /// <summary>切替判定に使う 1 px あたりの角度 [rad]。</summary>
        public double RadiansPerPixel { get; private set; }

        void Awake()
        {
            Initialize();

            // シナリオ指定があればそちらを初期状態にする (Step 8-0)。
            // 引数が無ければ従来どおりセーブから始める。**通常プレイの挙動は変えない。**
            if (_scenario != null)
            {
                _scenario.Initialize(Model);
            }

            if (_scenario != null && _scenario.IsActive)
            {
                _scenario.Apply(this, _shipRig, _stack, _overlay);
                return;
            }

            ApplySavedStart();
        }

        /// <summary>
        /// セーブがあればそのステーションから、無ければ地球ステーションから始める (Step 7)。
        ///
        /// **Initialize() とは分けてある。** EditMode テストは Initialize() しか
        /// 呼ばないので、ファイル IO がテストに混ざらない。
        /// </summary>
        public void ApplySavedStart()
        {
            if (Model == null || Model.Stations == null || Model.Stations.Count == 0)
            {
                return;
            }

            StartAtStation(SaveFile.LoadStationIndex(Model));
        }

        /// <summary>指定のステーションのポートに着いた状態から始める (Step 7)。</summary>
        public void StartAtStation(int index)
        {
            if (Model == null || Model.Stations == null || Model.Stations.Count == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, Model.Stations.Count - 1);
            SpaceStation station = Model.Stations[index];

            if (_shipRig != null)
            {
                _shipRig.SetTargetIndex(index);
            }

            PlaceObserver(station.PortPosition);

            // ポート正面を向いておく。出港してすぐ操作できる向き。
            if (_shipTransform != null)
            {
                Vec3d port = station.PortDirection;
                var facing = new Vector3((float)-port.X, (float)-port.Y, (float)-port.Z);
                if (facing.sqrMagnitude > 0f)
                {
                    _shipTransform.rotation = Quaternion.LookRotation(facing, Vector3.up);
                }
            }

            StartStationName = station.Name;
        }

        /// <summary>起動時に選ばれたステーション名 (Step 7)。検証用。</summary>
        public string StartStationName { get; private set; } = string.Empty;

        /// <summary>Awake からも EditMode テストからも呼べるようにしてある。</summary>
        public void Initialize()
        {
            Clock = new UniverseClock();
            Origin = new FloatingOrigin();
            Ship = new AbsoluteMotion();
            Ship.SetVelocity(new Vec3d(_initialVelocityX, _initialVelocityY, _initialVelocityZ));

            Model = SolarSystemModel.CreateOpposition();
            RadiansPerPixel = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees,
                UniverseConstants.ReferencePixelHeight);

            if (_shiftDriver == null)
            {
                _shiftDriver = GetComponentInChildren<OriginShiftDriver>();
            }

            if (_shiftDriver != null)
            {
                _shiftDriver.CollectFromScene();
            }

            // CelestialBody は Unity がシリアライズできないので、名前で引き直す。
            if (_solarSystemView != null)
            {
                _solarSystemView.Rebind(Model);
            }

            if (_stations != null)
            {
                _stations.Rebind(Model);
            }

            // 開始時点で原点を船に合わせておく。1 フレーム目から原点相対座標が正しくなる。
            Origin.Rebase(Ship.Position);
            PushToTransforms();
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 1 フレームぶん進める。Update から呼ばれるが、テストからは
        /// 任意の dt で直接呼べる (決定 D-24: 時刻は Core が持つので Time に依存しない)。
        /// </summary>
        public void Tick(double realDeltaSeconds)
        {
            // 入力 -> 姿勢と速度。積分より先に反映する (決定 D-1: 呼び出し順はコードに書く)。
            if (_shipRig != null)
            {
                _shipRig.ApplyInput(this, realDeltaSeconds);
                HandleHarnessKeys();
            }

            int steps = Clock.Advance(realDeltaSeconds);
            for (int i = 0; i < steps; i++)
            {
                Ship.Step(Clock.FixedDeltaSeconds);
            }

            // 毎フレーム再基準化 (決定 D-3)。船 = カメラは常に厳密に原点。
            Origin.Rebase(Ship.Position);
            PushToTransforms();
        }

        void PushToTransforms()
        {
            if (_shiftDriver != null)
            {
                _shiftDriver.Apply(Origin);
            }

            // 船は定義上いつでも原点。float32 の刻みが 0 になる唯一の場所。
            if (_shipTransform != null)
            {
                _shipTransform.position = Vector3.zero;
            }

            // 天体はプロキシ殻の上に置き直す。太陽光の向きは絶対座標の差分から。
            if (_solarSystemView != null)
            {
                _solarSystemView.Apply(Ship.Position, RadiansPerPixel);
            }

            if (_stations != null)
            {
                _stations.Apply(Ship.Position);
            }

            if (_sunLightAimer != null)
            {
                _sunLightAimer.Apply(Model, Ship.Position, SunDirectionOverride);
            }

            // レンズフレアの遮蔽 (Step 9-3a)。深度を使わず角半径で判定する。
            if (_sunFlare != null)
            {
                _sunFlare.Apply(Ship.Position, Model);
            }

            if (_engineAudio != null && _shipRig != null)
            {
                _engineAudio.Apply(_shipRig.LastThrust);
            }

            if (_shake != null && _shipRig != null)
            {
                bool docking = _shipRig.Docking.State != DockingState.Free
                               && _shipRig.Docking.State != DockingState.Approaching;
                _shake.Tick(_shipRig.LastThrust, docking, Clock != null ? Clock.FixedDeltaSeconds : 0.0);
            }

            UpdateInstruments();
        }

        /// <summary>計器を 10 Hz で更新する (Step 4)。</summary>
        void UpdateInstruments()
        {
            if (_instruments == null || Model == null)
            {
                return;
            }

            // 目標は選択中のステーション (Step 5)。
            SpaceStation station = _shipRig != null ? _shipRig.TargetStation(Model) : null;
            Vec3d targetPosition = station != null ? station.AbsolutePosition : Model.Mars.AbsolutePosition;
            string targetName = station != null ? station.Name : Model.Mars.Name;

            double distance = Vec3d.Distance(targetPosition, Ship.Position);
            double eta = double.PositiveInfinity;

            if (_shipRig != null && _shipRig.Autopilot.IsEngaged)
            {
                eta = _shipRig.Autopilot.EtaSeconds;
            }
            else
            {
                // 手動なら視線方向の接近速度から出す (§5-3)。
                // 遠ざかっている / 目標を向いていないときは無限大 -> "--:--:--"。
                Vec3d toTarget = targetPosition - Ship.Position;
                double closing = Vec3d.Dot(Ship.Velocity, toTarget.Normalized);
                if (closing > 0.0)
                {
                    eta = distance / closing;
                }
            }

            _instruments.Tick(
                Clock != null ? Clock.ElapsedSeconds : 0.0,
                Ship.SpeedKmPerSec,
                distance,
                eta,
                targetName);

            // ポート正面からのずれ角 (Step 6)。整列の手がかりが無いと
            // Enter を押しても何が足りないのか分からない。
            if (station != null)
            {
                _instruments.SetAlignment(AlignmentAngleDegrees(station));
            }
        }

        /// <summary>シーン生成 (Editor) から参照を差し込むための口。</summary>
        public void Configure(
            OriginShiftDriver shiftDriver,
            Transform shipTransform,
            SolarSystemView solarSystemView = null,
            SunLightAimer sunLightAimer = null,
            ShipRig shipRig = null,
            InstrumentPanel instruments = null,
            StationViewSet stations = null,
            PostProcessPreset post = null,
            EngineAudio engineAudio = null,
            DebugOverlay overlay = null,
            ScenarioRunner scenario = null,
            CockpitShake shake = null,
            CameraStackController stack = null,
            SunFlareController sunFlare = null)
        {
            _shiftDriver = shiftDriver;
            _shipTransform = shipTransform;
            _solarSystemView = solarSystemView;
            _sunLightAimer = sunLightAimer;
            _shipRig = shipRig;
            _instruments = instruments;
            _stations = stations;
            _post = post;
            _engineAudio = engineAudio;
            _overlay = overlay;
            _scenario = scenario;
            _shake = shake;
            _stack = stack;
            _sunFlare = sunFlare;
        }

        /// <summary>F1 / F2 / F3 の処理 (Step 8-0)。</summary>
        void HandleHarnessKeys()
        {
            if (_shipRig.DebugHudTogglePressed && _overlay != null)
            {
                _overlay.Toggle();
            }

            if (_scenario == null || !_scenario.IsActive)
            {
                return;
            }

            if (_shipRig.ScenarioNextPressed)
            {
                _scenario.Step(1);
                _scenario.Apply(this, _shipRig, _stack, _overlay);
            }
            else if (_shipRig.ScenarioPrevPressed)
            {
                _scenario.Step(-1);
                _scenario.Apply(this, _shipRig, _stack, _overlay);
            }
        }

        /// <summary>機首とポート正面のなす角 [deg]。</summary>
        public double AlignmentAngleDegrees(SpaceStation station)
        {
            if (station == null || _shipTransform == null)
            {
                return 180.0;
            }

            Vec3d port = station.PortDirection;
            var facing = new Vector3((float)-port.X, (float)-port.Y, (float)-port.Z);
            return Vector3.Angle(_shipTransform.forward, facing);
        }

        /// <summary>
        /// 観測者を指定の絶対位置へ置き直して、全ビューを更新する。
        /// スクショ検証 (Editor) から使う。船の移動 (Step 3) ではない。
        /// </summary>
        public void PlaceObserver(Vec3d absolutePosition)
        {
            Ship.SetVelocity(Vec3d.Zero);
            Ship.SetPosition(absolutePosition);
            Origin.Rebase(Ship.Position);
            PushToTransforms();
        }
    }
}
