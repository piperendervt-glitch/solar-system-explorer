using SolarSystem.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Unity
{
    /// <summary>
    /// フレアの見た目を実行時に触る (Step 9-3b)。
    ///
    /// **アセットには書き戻さない。** LensFlareDataSRP は ScriptableObject の
    /// アセットなので、要素を直接書くと Assets/Materials/SunFlare.asset が汚れる。
    /// 初回に Instantiate で実行時コピーを作り、そちらだけを書き換える。
    ///
    /// **コピーは明示的に破棄する。** ScriptableObject はシーンを切り替えても
    /// 消えないので、Dispose を呼ばないと溜まっていく (Editor 実行で特に)。
    /// 呼び元は SunFlareController.OnDestroy。
    /// </summary>
    public sealed class FlareRuntimeData
    {
        /// <summary>アセット側の要素の並び。**LensFlareBuilder と一致させること。**</summary>
        public const int GlareIndex = 0;
        public const int SpikeFirstIndex = 1;
        public const int StripeIndex = SpikeFirstIndex + PlanetAppearance.FlareSpikeElementMax;
        public const int GhostIndex = StripeIndex + 1;

        readonly LensFlareComponentSRP _flare;
        LensFlareDataSRP _copy;

        public FlareRuntimeData(LensFlareComponentSRP flare)
        {
            _flare = flare;
        }

        /// <summary>実行時コピー。まだ作っていなければ作る。</summary>
        public LensFlareDataSRP Data
        {
            get
            {
                if (_copy != null)
                {
                    return _copy;
                }

                if (_flare == null || _flare.lensFlareData == null)
                {
                    return null;
                }

                _copy = Object.Instantiate(_flare.lensFlareData);
                _copy.name = _flare.lensFlareData.name + " (runtime)";
                _flare.lensFlareData = _copy;
                return _copy;
            }
        }

        /// <summary>作ったコピーがあるか。テストから見る。</summary>
        public bool HasCopy => _copy != null;

        /// <summary>
        /// 画面上の光条の本数から、有効にする要素数を出す。
        /// **要素 1 個で光条は 2 本に見える** (筋が中心を通る両側の帯なので)。
        /// </summary>
        public static int ElementsForSpikeCount(double screenSpikes)
        {
            int elements = Mathf.RoundToInt((float)screenSpikes * 0.5f);
            return Mathf.Clamp(elements, 0, PlanetAppearance.FlareSpikeElementMax);
        }

        /// <summary>
        /// 見た目の 3 つを反映する。
        /// spikeCount は**画面上の本数**。length は本体比、thickness は筋の太さ。
        /// </summary>
        public void Apply(double spikeCount, double spikeLength, double spikeThickness,
                          double ghostIntensity)
        {
            LensFlareDataSRP data = Data;
            if (data == null || data.elements == null)
            {
                return;
            }

            int active = ElementsForSpikeCount(spikeCount);

            for (int i = 0; i < PlanetAppearance.FlareSpikeElementMax; i++)
            {
                int index = SpikeFirstIndex + i;
                if (index >= data.elements.Length)
                {
                    break;
                }

                LensFlareDataElementSRP e = data.elements[index];
                if (e == null)
                {
                    continue;
                }

                e.visible = i < active;

                // **有効な本数で角度を割り直す。** 固定角のままだと
                // 本数を減らしたときに片側へ寄る。
                if (active > 0)
                {
                    e.rotation = i * 180f / active;
                }

                e.sizeXY = new Vector2((float)spikeLength, (float)spikeThickness);
            }

            if (GhostIndex < data.elements.Length && data.elements[GhostIndex] != null)
            {
                data.elements[GhostIndex].localIntensity = (float)ghostIntensity;
            }
        }

        /// <summary>実行時コピーを捨てる。**呼ばないと溜まる。**</summary>
        public void Dispose()
        {
            if (_copy == null)
            {
                return;
            }

            if (_flare != null)
            {
                _flare.lensFlareData = null;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(_copy);
            }
            else
            {
                Object.DestroyImmediate(_copy);
            }

            _copy = null;
        }
    }
}
