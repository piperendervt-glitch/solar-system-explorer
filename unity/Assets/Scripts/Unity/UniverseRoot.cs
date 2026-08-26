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

        /// <summary>切替判定に使う 1 px あたりの角度 [rad]。</summary>
        public double RadiansPerPixel { get; private set; }

        void Awake()
        {
            Initialize();
        }

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

            if (_sunLightAimer != null)
            {
                _sunLightAimer.Apply(Model, Ship.Position);
            }
        }

        /// <summary>シーン生成 (Editor) から参照を差し込むための口。</summary>
        public void Configure(
            OriginShiftDriver shiftDriver,
            Transform shipTransform,
            SolarSystemView solarSystemView = null,
            SunLightAimer sunLightAimer = null)
        {
            _shiftDriver = shiftDriver;
            _shipTransform = shipTransform;
            _solarSystemView = solarSystemView;
            _sunLightAimer = sunLightAimer;
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
