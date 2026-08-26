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
        [SerializeField] Renderer _pointRenderer;
        [SerializeField] Renderer _meshRenderer;

        readonly BodyLodSolver _lod = new BodyLodSolver();

        MaterialPropertyBlock _block;

        public CelestialBody Body { get; private set; }

        public BodyLodSolver Lod => _lod;

        /// <summary>直近の角直径 [px]。診断・スクショ検証用。</summary>
        public double LastAngularPixels { get; private set; }

        /// <summary>直近の真の距離 [units]。</summary>
        public double LastDistance { get; private set; }

        public string BodyName => _bodyName;

        public void Bind(CelestialBody body, Transform point, Transform mesh)
        {
            Body = body;
            _bodyName = body != null ? body.Name : null;
            _point = point;
            _mesh = mesh;
            _pointRenderer = point != null ? point.GetComponent<Renderer>() : null;
            _meshRenderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
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
        {
            if (Body == null)
            {
                return;
            }

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

            // ---- メッシュ: 真の角直径に一致するスケール ----
            // Unity の Sphere プリミティブは直径 1。半径 r の球にするには localScale = 2r。
            if (_mesh != null)
            {
                float meshRadius = (float)(Body.RadiusKm * scale);
                _mesh.localScale = Vector3.one * (meshRadius * 2f);
                _mesh.gameObject.SetActive(_lod.MeshActive);
            }

            // ---- 光点: 最小 px でクランプ ----
            if (_point != null)
            {
                double pointPixels = System.Math.Max(pixels, UniverseConstants.MinPointPixels);
                double pointAngular = pointPixels * radiansPerPixel;
                // 殻の上で pointAngular の角直径になる半径
                float pointRadius = (float)(shellRadius * System.Math.Tan(pointAngular * 0.5));
                _point.localScale = Vector3.one * (pointRadius * 2f);
                _point.gameObject.SetActive(_lod.PointActive);
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

            if (_meshRenderer != null && _lod.MeshActive)
            {
                _meshRenderer.GetPropertyBlock(_block);
                var c = baseColor;
                c.a = (float)_lod.Blend;
                _block.SetColor(ShaderIds.BaseColor, c);
                _meshRenderer.SetPropertyBlock(_block);
            }

            if (_pointRenderer != null && _lod.PointActive)
            {
                _pointRenderer.GetPropertyBlock(_block);
                var c = baseColor;
                c.a = (float)(1.0 - _lod.Blend);
                _block.SetColor(ShaderIds.BaseColor, c);
                _pointRenderer.SetPropertyBlock(_block);
            }
        }

        static class ShaderIds
        {
            public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        }
    }
}
