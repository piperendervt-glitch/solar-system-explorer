using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **どのステーション定義で組まれたかをシーンに焼く (Step 13-3b)。**
    /// `CockpitIdentity` と同じ形。
    ///
    /// ■ なぜ要るか
    /// アセット（Cobble のプレハブ）は**追跡除外**なので、clone しただけの環境には無い。
    /// そのとき `SceneBuilder` は箱で組む。**そこで実行時のモデルが Cobble の値
    /// （Scale 0.008 / PortStandoff 0.015）を持っていると、絵と数字が別のものを見る。**
    /// 組んだときの Id をここへ焼き、実行時はこれを見て定義を引く。
    ///
    /// **落ちたことを黙って隠さない。** 要求した Id も残す。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StationIdentity : MonoBehaviour
    {
        [SerializeField] string _definitionId = StationDefinition.BoxId;
        [SerializeField] string _requestedId = StationDefinition.BoxId;
        [SerializeField] bool _fellBackToBox;

        /// <summary>実際に組まれた定義の Id。</summary>
        public string DefinitionId => _definitionId;

        /// <summary>組もうとした定義の Id。フォールバックしていなければ同じ。</summary>
        public string RequestedId => _requestedId;

        /// <summary>プレハブが取り込まれておらず箱へ落ちたか。</summary>
        public bool FellBackToBox => _fellBackToBox;

        public void Bind(string definitionId, string requestedId, bool fellBackToBox)
        {
            _definitionId = definitionId;
            _requestedId = requestedId;
            _fellBackToBox = fellBackToBox;
        }

        /// <summary>焼かれた Id から定義を引く。</summary>
        public StationDefinition Resolve() => StationCatalog.ById(_definitionId);

        public string Describe()
            => _fellBackToBox
                ? $"{_definitionId} (**{_requestedId} が無いので箱へ落ちた**)"
                : _definitionId;
    }
}
