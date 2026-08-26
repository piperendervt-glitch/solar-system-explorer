using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 太陽の Directional Light を毎フレーム向け直す (docs/01-architecture.md §3-5)。
    ///
    /// Directional Light は**位置を持たない**。持っているのは rotation だけ。
    /// 向きは「観測者の絶対位置 − 太陽の絶対位置」を double で取ってから正規化する。
    /// 差分なので**浮動原点のシフトでは 1 ビットも変わらない**
    /// (再基準化は両方に同じオフセットを足す操作なので差分は不変)。
    /// よって ShiftableBody には登録しない。
    ///
    /// 正規化後は成分が -1..1 に収まるので float32 で十分。
    /// 単位ベクトル成分の float32 の刻みは約 6e-8 -> 角度誤差 0.012 arcsec。
    /// 太陽の角直径 1919 arcsec に対して 16 万分の 1。
    ///
    /// Update() は持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SunLightAimer : MonoBehaviour
    {
        [SerializeField] Light _light;

        [Tooltip("地球の位置での光の強さ。火星では逆二乗で 0.431 倍になる。")]
        // ACES トーンマッピングは中間調を沈める。素の 1.4 だと火星が真っ黒に近くなるので、
        // トーンマップ後に地球近傍の日向が sRGB 0.5 前後に来る値まで上げてある。
        [SerializeField] float _intensityAtEarth = 3.0f;

        public Vector3 LastDirection { get; private set; }

        public double LastRelativeIrradiance { get; private set; }

        public void Bind(Light light) => _light = light;

        public void Apply(SolarSystemModel model, Vec3d observerAbsolute)
        {
            if (_light == null || model == null)
            {
                return;
            }

            Vec3d dir = model.SunlightDirectionAt(observerAbsolute);
            var forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            LastDirection = forward;

            if (forward.sqrMagnitude > 0f)
            {
                _light.transform.rotation = Quaternion.LookRotation(forward);
            }

            double irradiance = model.RelativeIrradianceAt(observerAbsolute);
            LastRelativeIrradiance = irradiance;
            _light.intensity = _intensityAtEarth * (float)irradiance;
        }
    }
}
