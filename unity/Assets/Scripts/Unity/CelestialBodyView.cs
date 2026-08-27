using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 天体 1 個の見た目 (docs/01-architecture.md §3-3 / 決定 D-19)。
    ///
    /// 光点とメッシュを子として両方持ち、有効/無効とアルファで切り替える。
    /// 動的な生成・破棄はしない (切替時にフレーム落ちが出るため)。
    ///
    /// 位置は「プロキシ殻」— 真の方向のまま、配置半径だけ対数圧縮した殻に載せる。
    /// スケール係数 s = r_proxy / d を掛けるので**角直径は真の値と厳密に一致**する。
    /// これが「切替時に見た目が飛ばない」ことの根拠。
    ///
    /// Update() を持たない。呼ぶのは SolarSystemView (さらに上は UniverseRoot) だけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CelestialBodyView : MonoBehaviour
    {
        // CelestialBody は Core の素の C# クラスなので Unity はシリアライズできない。
        // シーンには名前だけ残し、起動時に SolarSystemModel から引き直す (Rebind)。
        [SerializeField] string _bodyName;

        [SerializeField] Transform _point;
        [SerializeField] Transform _mesh;
        [SerializeField] Transform _realMesh;
        [SerializeField] Renderer _pointRenderer;
        [SerializeField] Renderer _meshRenderer;
        [SerializeField] Renderer _realMeshRenderer;

        // ---- Step 8-3 / 8-4 で足した Transform ----
        // **1 つの Transform に 2 つの駆動を乗せない。**
        //   Spin      : localRotation (自転)
        //   Mesh      : localScale     (見かけの大きさ)
        //   RealAnchor: position       (実スケールのワールド位置)
        [SerializeField] Transform _spin;
        [SerializeField] Transform _cloudSpin;
        [SerializeField] Transform _cloudMesh;

        /// <summary>コロナのビルボード (Step 9-2)。太陽のみ。</summary>
        [SerializeField] Transform _corona;
        [SerializeField] Renderer _coronaRenderer;
        [SerializeField] Renderer _cloudRenderer;
        [SerializeField] Transform _realAnchor;
        [SerializeField] Transform _realSpin;
        [SerializeField] Transform _realCloudSpin;
        [SerializeField] Transform _realCloudMesh;
        [SerializeField] Renderer _realCloudRenderer;

        /// <summary>直近の自転角 [度]。テストが読む。</summary>
        public float LastSpinDegrees { get; private set; }

        /// <summary>直近の雲の回転角 [度]。</summary>
        public float LastCloudSpinDegrees { get; private set; }

        public Transform Spin => _spin;
        public Transform Mesh => _mesh;
        public Transform CloudMesh => _cloudMesh;
        public Transform Corona => _corona;
        public Renderer CoronaRenderer => _coronaRenderer;

        /// <summary>
        /// コロナの半径倍率。**F4 のパネルが実行時に書き換える。**
        /// 既定は Core の定数から取り、ここに数値を二重定義しない。
        /// </summary>
        public float CoronaRadiusScale { get; set; } = (float)PlanetAppearance.CoronaRadiusScale;
        public Renderer CloudRenderer => _cloudRenderer;
        public Renderer PointRenderer => _pointRenderer;
        public Renderer MeshRenderer => _meshRenderer;
        public Renderer RealMeshRenderer => _realMeshRenderer;
        public Renderer RealCloudRenderer => _realCloudRenderer;

        readonly BodyLodSolver _lod = new BodyLodSolver();

        MaterialPropertyBlock _block;

        public CelestialBody Body { get; private set; }

        public BodyLodSolver Lod => _lod;

        /// <summary>直近の角直径 [px]。診断・スクショ検証用。</summary>
        public double LastAngularPixels { get; private set; }

        /// <summary>直近の真の距離 [units]。</summary>
        public double LastDistance { get; private set; }

        /// <summary>実スケールへの引き渡し率 0..1 (Step 3b)。</summary>
        public double RealScaleBlend { get; private set; }

        /// <summary>この天体が引き渡しの対象か。SolarSystemView が毎フレーム決める。</summary>
        public bool IsHandoffTarget { get; private set; }

        public void SetHandoffTarget(bool value) => IsHandoffTarget = value;

        public string BodyName => _bodyName;

        public void Bind(CelestialBody body, Transform point, Transform mesh, Transform realMesh)
        {
            BindCore(body, point, mesh, realMesh);
        }

        /// <summary>Step 8-3 / 8-4 の Transform も含めて結び付ける。</summary>
        public void BindAll(CelestialBody body, Transform point, Transform mesh, Transform realMesh,
                            Transform spin, Transform realAnchor, Transform realSpin,
                            Transform cloudSpin, Transform cloudMesh,
                            Transform realCloudSpin, Transform realCloudMesh,
                            Transform corona = null)
        {
            BindCore(body, point, mesh, realMesh);
            _spin = spin;
            _realAnchor = realAnchor;
            _realSpin = realSpin;
            _cloudSpin = cloudSpin;
            _cloudMesh = cloudMesh;
            _realCloudSpin = realCloudSpin;
            _realCloudMesh = realCloudMesh;
            _cloudRenderer = cloudMesh != null ? cloudMesh.GetComponent<Renderer>() : null;
            _realCloudRenderer = realCloudMesh != null ? realCloudMesh.GetComponent<Renderer>() : null;
            _corona = corona;
            _coronaRenderer = corona != null ? corona.GetComponent<Renderer>() : null;
        }

        void BindCore(CelestialBody body, Transform point, Transform mesh, Transform realMesh)
        {
            Body = body;
            _bodyName = body != null ? body.Name : null;
            _point = point;
            _mesh = mesh;
            _realMesh = realMesh;
            _pointRenderer = point != null ? point.GetComponent<Renderer>() : null;
            _meshRenderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
            _realMeshRenderer = realMesh != null ? realMesh.GetComponent<Renderer>() : null;
        }

        /// <summary>シーン読み込み後に Core のモデルから天体データを引き直す。</summary>
        public void Rebind(SolarSystemModel model)
        {
            if (model == null || string.IsNullOrEmpty(_bodyName))
            {
                return;
            }

            for (int i = 0; i < model.Bodies.Count; i++)
            {
                if (model.Bodies[i].Name == _bodyName)
                {
                    Body = model.Bodies[i];
                    return;
                }
            }

            Debug.LogWarning($"[CelestialBodyView] モデルに '{_bodyName}' が無い。");
        }

        /// <summary>
        /// 観測者の絶対位置から、プロキシ殻上の配置・スケール・切替を更新する。
        /// </summary>
        public void Apply(Vec3d observerAbsolute, double radiansPerPixel)
            => Apply(observerAbsolute, radiansPerPixel, 0.0);

        /// <summary>elapsedSeconds は自転角の導出に使う (Step 8-4)。</summary>
        public void Apply(Vec3d observerAbsolute, double radiansPerPixel, double elapsedSeconds)
        {
            if (Body == null)
            {
                return;
            }

            // 引き渡し対象でなければ常にプロキシ殻のまま (太陽・地球など)。
            RealScaleBlend = IsHandoffTarget
                ? RealScaleHandoff.Blend(Body.DistanceFrom(observerAbsolute))
                : 0.0;

            double distance = Body.DistanceFrom(observerAbsolute);
            LastDistance = distance;

            Vec3d dir = Body.DirectionFrom(observerAbsolute);
            double shellRadius = DeepProxyProjection.ShellRadius(distance);
            double scale = DeepProxyProjection.ScaleFactor(distance);

            transform.localPosition = new Vector3(
                (float)(dir.X * shellRadius),
                (float)(dir.Y * shellRadius),
                (float)(dir.Z * shellRadius));

            // 殻の中心 (観測者) を向く。光点のビルボードとメッシュの向きを揃えるため。
            transform.localRotation = Quaternion.LookRotation(
                new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z));

            double pixels = AngularSizeSolver.AngularDiameterPixels(Body.RadiusKm, distance, radiansPerPixel);
            LastAngularPixels = pixels;
            _lod.Update(pixels);

            // ---- 自転 (Step 8-4) ----
            // **Spin に載せる。** Mesh は localScale を毎フレーム上書きするので競合しない。
            double spin = BodyRotation.AngleDegrees(elapsedSeconds, Body.RotationPeriodHours);
            double cloudSpin = BodyRotation.AngleDegrees(elapsedSeconds, BodyRotation.EarthCloudPeriodHours);
            LastSpinDegrees = (float)spin;
            LastCloudSpinDegrees = (float)cloudSpin;

            var spinRotation = Quaternion.Euler(0f, (float)spin, 0f);
            var cloudRotation = Quaternion.Euler(0f, (float)cloudSpin, 0f);

            // **world 指定で入れる (Step 8-5)。**
            // 殻側の Spin / CloudSpin は root の子で、root には光点のビルボード用の
            // LookRotation(dir) が乗っている。localRotation で入れると親の回転が混ざり、
            // 実スケール側 (親が恒等) と世界回転が食い違う。
            // 実測: 引き渡し帯の中央で地表・雲とも 90.00 度ずれていた。
            //
            // **RealAnchor の恒等化を先に済ませる。** world 指定は代入時点の親の
            // 回転から localRotation を逆算するので、あとから親を回すと崩れる。
            if (_realAnchor != null)
            {
                _realAnchor.rotation = Quaternion.identity;
            }

            if (_spin != null) { _spin.rotation = spinRotation; }
            if (_realSpin != null) { _realSpin.rotation = spinRotation; }
            if (_cloudSpin != null) { _cloudSpin.rotation = cloudRotation; }
            if (_realCloudSpin != null) { _realCloudSpin.rotation = cloudRotation; }

            // ---- メッシュ: 真の角直径に一致するスケール ----
            // Unity の Sphere プリミティブは直径 1。半径 r の球にするには localScale = 2r。
            if (_mesh != null)
            {
                float meshRadius = (float)(Body.RadiusKm * scale);
                _mesh.localScale = Vector3.one * (meshRadius * 2f);
                _mesh.gameObject.SetActive(_lod.MeshActive && RealScaleBlend < 1.0);

                // 雲はプロキシ殻にも付ける。付けないと引き渡し帯 (5e4 units で
                // 円盤 263 px) で雲が湧いて出る。
                if (_cloudMesh != null)
                {
                    _cloudMesh.localScale =
                        Vector3.one * (meshRadius * 2f * SolarSystem.Unity.CloudLayer.RadiusScale);
                    _cloudMesh.gameObject.SetActive(_mesh.gameObject.activeSelf);
                }
            }

            // ---- コロナ (Step 9-2) ----
            // **本体の見かけの大きさに追従させる。** 殻のスケール係数が
            // そのまま乗るので、距離が変わっても本体との比は一定になる。
            //
            // **フェードは持たない。** 太陽は引き渡し対象ではないので
            // RealScaleBlend は常に 0、つまり「光点 α + 殻 α」は構造的に
            // 常に 1.0 で、掛けても何も起きない。
            // コロナは太陽が描かれている限り常に出る。
            if (_corona != null)
            {
                float coronaRadius = (float)(Body.RadiusKm * scale) * CoronaRadiusScale;
                _corona.localScale = Vector3.one * (coronaRadius * 2f);
            }

            // ---- 光点: 最小 px でクランプ ----
            if (_point != null)
            {
                double pointPixels = System.Math.Max(pixels, UniverseConstants.MinPointPixels);
                double pointAngular = pointPixels * radiansPerPixel;
                // 殻の上で pointAngular の角直径になる半径
                float pointRadius = (float)(shellRadius * System.Math.Tan(pointAngular * 0.5));
                _point.localScale = Vector3.one * (pointRadius * 2f);
                _point.gameObject.SetActive(_lod.PointActive && RealScaleBlend < 1.0);
            }

            // ---- 実スケール: 真の距離・真の大きさで置く (Step 3b) ----
            if (_realMesh != null)
            {
                bool active = RealScaleBlend > 0.0;
                if (!active)
                {
                    _realMesh.gameObject.SetActive(false);
                    if (_realCloudMesh != null)
                    {
                        _realCloudMesh.gameObject.SetActive(false);
                    }
                }
                else
                {
                    // 親 (この Transform) はプロキシ殻の上にいるので、
                    // 実スケール側はワールド座標で直接置く。観測者は常に原点。
                    // 位置は RealAnchor が持つ。RealMesh は localScale だけ。
                    Transform anchor = _realAnchor != null ? _realAnchor : _realMesh;
                    anchor.position = new Vector3(
                        (float)(dir.X * distance),
                        (float)(dir.Y * distance),
                        (float)(dir.Z * distance));
                    if (_realAnchor == null)
                    {
                        _realMesh.rotation = Quaternion.identity;
                    }

                    // **スケールを入れてから活性化する。**
                    // 逆にすると、活性化した最初のフレームだけ前回のスケール
                    // (初期値 1) で描かれ得る。雲は半径比が 1.006 でなく 1.000 に見える。
                    _realMesh.localScale = Vector3.one * (float)(Body.RadiusKm * 2.0);
                    _realMesh.gameObject.SetActive(true);

                    if (_realCloudMesh != null)
                    {
                        _realCloudMesh.localScale = Vector3.one *
                            (float)(Body.RadiusKm * 2.0 * SolarSystem.Unity.CloudLayer.RadiusScale);
                        _realCloudMesh.gameObject.SetActive(true);
                    }
                }
            }

            ApplyColors();
        }

        void ApplyColors()
        {
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            var baseColor = new Color(
                (float)Body.Color.R,
                (float)Body.Color.G,
                (float)Body.Color.B,
                1f);

            // プロキシ側は実スケールが立ち上がるぶんだけ薄くする。合計は常に 1。
            float proxyShare = (float)(1.0 - RealScaleBlend);

            SetAlpha(_meshRenderer, baseColor, (float)_lod.Blend * proxyShare);
            SetAlpha(_pointRenderer, baseColor, (float)(1.0 - _lod.Blend) * proxyShare);
            SetAlpha(_realMeshRenderer, baseColor, (float)RealScaleBlend);

            // **雲も同じアルファ契約に乗せる (Step 8-3)。**
            // 地表だけがクロスフェードして雲が残ると、引き渡し帯で雲だけ浮く。
            SetAlpha(_cloudRenderer, Color.white, (float)_lod.Blend * proxyShare);
            SetAlpha(_realCloudRenderer, Color.white, (float)RealScaleBlend);
        }

        void SetAlpha(Renderer renderer, Color baseColor, float alpha)
        {
            if (renderer == null || !renderer.gameObject.activeSelf)
            {
                return;
            }

            renderer.GetPropertyBlock(_block);
            var c = baseColor;
            c.a = alpha;
            _block.SetColor(ShaderIds.BaseColor, c);
            renderer.SetPropertyBlock(_block);
        }

        static class ShaderIds
        {
            public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        }
    }
}
