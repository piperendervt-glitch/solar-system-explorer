using System;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// コックピットの補助光 (Step 11-4)。
    ///
    /// ■ **潰れないためだけの弱い光。**
    /// 内装は太陽の向き次第で真っ黒に落ちる。凝った演出はせず、暗い場面で
    /// 形が残る程度に持ち上げる。強さは F4 で振って人が決めた（0.35）。
    ///
    /// ■ **内装の発光は撤去した (11-4)。**
    /// 発光マテリアル `Cockpit3Grey` の強さを振っても**内装の画素が 1 つも
    /// 変わらなかった**（0.0 と 1.0 で 4 場面とも最大差 0 / 実測）。
    /// 効かない摘みは残さない。潰れ対策は補助光だけで足りている。
    ///
    /// ■ **既定値が「何もしない値」にならないようにする (11-3c の反省)。**
    /// 強度 0 や未 Bind は「消えている」と「設定し忘れた」が絵で区別できない。
    /// Bind されていなければ例外で止め、既定の強さは Core の定数から取る。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitLights : MonoBehaviour
    {
        [SerializeField] Light _fill;
        [SerializeField] float _fillIntensity = (float)CockpitDefinition.DefaultFillLightIntensity;
        [SerializeField] bool _fillEnabled = true;

        public Light Fill => _fill;

        public float FillIntensity => _fillIntensity;

        public bool FillEnabled => _fillEnabled;

        /// <summary>
        /// 組み立て時に結び付ける。**補助光が無ければ例外。**
        /// 「光が無いのにエラーも出ない」を作らない。
        /// </summary>
        public void Bind(Light fill)
            => _fill = fill != null
                ? fill
                : throw new ArgumentNullException(nameof(fill), "補助光が無い");

        void Start() => Apply();

        /// <summary>F4 から。**アセットには書き戻さない**（MPB と実行時の Light だけ）。</summary>
        public void SetFillIntensity(float intensity)
        {
            if (Mathf.Approximately(_fillIntensity, intensity))
            {
                return;
            }

            _fillIntensity = intensity;
            Apply();
        }

        /// <summary>F4 から。**消したときに効いていたことが分かる**ための口。</summary>
        public void SetFillEnabled(bool enabled)
        {
            if (_fillEnabled == enabled)
            {
                return;
            }

            _fillEnabled = enabled;
            Apply();
        }

        /// <summary>いまの値をシーンへ当てる。**出所はこのコンポーネントだけ。**</summary>
        public void Apply()
        {
            if (_fill == null)
            {
                throw new InvalidOperationException(
                    "補助光が結び付いていない (CockpitLights.Bind を呼んでいない)");
            }

            _fill.enabled = _fillEnabled;
            _fill.intensity = _fillIntensity;
        }
    }
}
