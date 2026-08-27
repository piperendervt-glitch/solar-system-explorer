using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// どの `CockpitDefinition` で組まれたかをシーンに残す (Step 11-0c)。
    ///
    /// ■ なぜ要るか
    /// 計画書 11-1 の PlayMode テストは「レンダラー数が箱より多い」で差し替わりを
    /// 判定する形になっているが、**これは間接的すぎる。** メッシュの構成が変われば
    /// 箱より少ないことも起こりうるし、多くても別のアセットかもしれない。
    /// **Id が読めれば「hirez-sample で組まれた」と直接言える。**
    ///
    /// ■ 実行時に GUID を解決しないこと
    /// シーンは Editor スクリプトで毎回まっさら生成するので、箱か実アセットかの
    /// 分岐は**シーン生成時に一度決まって焼き込まれる。**
    /// ここが持つのは焼き込まれた結果の文字列だけで、`AssetDatabase` は要らない。
    ///
    /// F1 のデバッグ HUD にも出す。**実機で取り違えに気づけるようにするため。**
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitIdentity : MonoBehaviour
    {
        [SerializeField] string _definitionId = SolarSystem.Core.CockpitDefinition.BoxId;

        /// <summary>
        /// 要求された定義がプレハブを持っていたのに、取り込まれておらず
        /// 箱へ落ちたか。**「落ちたこと」を黙って隠さない。**
        /// </summary>
        [SerializeField] bool _fellBackToBox;

        [SerializeField] string _requestedId = SolarSystem.Core.CockpitDefinition.BoxId;

        /// <summary>実際に組まれた定義の Id。</summary>
        public string DefinitionId => _definitionId;

        /// <summary>組もうとした定義の Id。フォールバックしていなければ同じ。</summary>
        public string RequestedId => _requestedId;

        public bool FellBackToBox => _fellBackToBox;

        public void Bind(string definitionId, string requestedId, bool fellBackToBox)
        {
            _definitionId = definitionId;
            _requestedId = requestedId;
            _fellBackToBox = fellBackToBox;
        }

        /// <summary>HUD の 1 行。フォールバックしたときだけ理由が出る。</summary>
        public string Describe()
            => _fellBackToBox
                ? $"{_definitionId}（{_requestedId} が取り込まれていないため箱へ）"
                : _definitionId;
    }
}
