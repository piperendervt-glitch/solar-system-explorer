using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 巡航中の微振動 (Step 8-0)。
    ///
    /// **並進ではなく回転で揺らす。** 並進はカメラからの距離で効き方が変わり、
    /// 無限遠 (星空・惑星) は 1 画素も動かない。実測:
    /// 0.0003 unit (= 0.3 mm) の並進は窓枠 (2 units 先) で 0.14 px、星空で 0 px。
    /// 回転なら視界全体が同じだけ動くので、振幅を角度で決められる。
    ///   1.5e-3 rad = 0.086 度 = 約 1.4 px (RadiansPerPixel 1.069e-3)
    ///
    /// Update() を持たない。UniverseRoot.Tick から呼ばれる (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitShake : MonoBehaviour
    {
        /// <summary>スラスト最大での回転振幅 [rad]。exe の体感で調整する。</summary>
        public const float MaxAmplitudeRadians = 1.5e-3f;

        /// <summary>ノイズの進む速さ [Hz 相当]。</summary>
        public const float NoiseSpeed = 11f;

        [SerializeField] Transform _rig;
        [SerializeField] float _maxAmplitudeRadians = MaxAmplitudeRadians;

        double _phase;

        /// <summary>直近に適用した回転振幅 [rad]。テストと計器の検証用。</summary>
        public float LastAmplitudeRadians { get; private set; }

        /// <summary>直近の回転 [度]。</summary>
        public Vector3 LastEulerDegrees { get; private set; }

        public void Bind(Transform rig) => _rig = rig;

        /// <summary>デバッグパネルから振幅を差し替える (Step 8-0b)。</summary>
        public void SetMaxAmplitude(float radians) => _maxAmplitudeRadians = radians;

        public float MaxAmplitude => _maxAmplitudeRadians;

        /// <summary>
        /// 振幅を決める純関数。テストから直接叩ける。
        /// ドッキング中は 0 (計画書 §8-0)。
        /// </summary>
        public static float AmplitudeRadians(float thrust01, bool docking, float maxAmplitude)
        {
            if (docking)
            {
                return 0f;
            }

            return maxAmplitude * Mathf.Clamp01(Mathf.Abs(thrust01));
        }

        /// <summary>1 フレーム進める。dt は実時間 [秒]。</summary>
        public void Tick(float thrust01, bool docking, double deltaSeconds)
        {
            float amplitude = AmplitudeRadians(thrust01, docking, _maxAmplitudeRadians);
            LastAmplitudeRadians = amplitude;

            if (_rig == null)
            {
                LastEulerDegrees = Vector3.zero;
                return;
            }

            if (amplitude <= 0f)
            {
                _rig.localRotation = Quaternion.identity;
                LastEulerDegrees = Vector3.zero;
                return;
            }

            _phase += deltaSeconds * NoiseSpeed;
            var t = (float)_phase;

            // 3 軸それぞれ別のノイズ列。-1..1 に写す。
            float pitch = Mathf.PerlinNoise(t, 0.17f) * 2f - 1f;
            float yaw = Mathf.PerlinNoise(0.41f, t) * 2f - 1f;
            float roll = Mathf.PerlinNoise(t * 0.7f, t * 0.7f + 3.3f) * 2f - 1f;

            float degrees = amplitude * Mathf.Rad2Deg;
            var euler = new Vector3(pitch * degrees, yaw * degrees, roll * degrees);
            LastEulerDegrees = euler;
            _rig.localRotation = Quaternion.Euler(euler);
        }
    }
}
