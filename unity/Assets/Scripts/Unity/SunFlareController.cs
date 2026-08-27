using SolarSystem.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 太陽のレンズフレアの強度を遮蔽率で落とす (Step 9-3a)。
    ///
    /// **SRP の occlusion / attenuationByLightShape は使わない。**
    /// 深度バッファ参照だが、惑星は Transparent / ZWrite off で深度を書かないため
    /// 原理的に効かない (docs/02-demo2-plan.md 9-3)。
    /// 代わりに Core の FlareOcclusion で角半径と角距離から解析的に求める。
    ///
    /// **視錐台外の判定は SRP に任せる。** 昼側・明暗境界で強度が 0 になることは実測済み。
    ///
    /// Update() を持たない。UniverseRoot.Tick から呼ばれる (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SunFlareController : MonoBehaviour
    {
        /// <summary>遮蔽なしのときの強度。Step 6 の値を据え置く。</summary>
        public const float BaseIntensity = 0.6f;

        [SerializeField] LensFlareComponentSRP _flare;
        [SerializeField] float _baseIntensity = BaseIntensity;

        /// <summary>直近の遮蔽率 0.0〜1.0。デバッグ表示とテストが読む。</summary>
        public float LastOcclusion { get; private set; }

        /// <summary>直近に適用した強度。</summary>
        public float LastIntensity { get; private set; }

        public float Base => _baseIntensity;

        public LensFlareComponentSRP Flare => _flare;

        public void Bind(LensFlareComponentSRP flare)
        {
            _flare = flare;
            if (_flare != null)
            {
                // SRP 側の遮蔽は効かないので明示的に切っておく。
                _flare.useOcclusion = false;
                _flare.attenuationByLightShape = false;
            }
        }

        /// <summary>1 フレームぶん更新する。</summary>
        public void Apply(Vec3d observerAbsolute, SolarSystemModel model)
        {
            double occlusion = FlareOcclusion.Occlusion(observerAbsolute, model);
            LastOcclusion = (float)occlusion;
            LastIntensity = _baseIntensity * (1f - LastOcclusion);

            if (_flare != null)
            {
                _flare.intensity = LastIntensity;
            }
        }
    }
}
