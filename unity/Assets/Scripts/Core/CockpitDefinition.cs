namespace SolarSystem.Core
{
    /// <summary>
    /// コックピットの定義 (Step 11-0c)。**UnityEngine 非依存。**
    ///
    /// ■ **いまはフォールバック判定に要る分しか無い。**
    /// `Scale` / `EyeLocal` / `EyeForward` / `Screens[]` / `Emissives[]` は
    /// 計画書 11-2a で作る。使う段になってから足す。
    /// **見えないコードを残さない**（Step 6 の EmissionIntensity = 4.0 と同じ轍を
    /// 踏まないため）。
    ///
    /// ■ **「実際に取り込まれているか」はここでは判定しない。**
    /// GUID からプレハブを引くのは `AssetDatabase`（Editor 専用）なので、
    /// 判定は Editor 側（`CockpitCatalog`）にある。ここが持つのは
    /// 「どの船か」という定義だけ。
    ///
    /// これは制約ではなく正しい形になっている。**このプロジェクトはシーンを
    /// Editor スクリプトで毎回まっさら生成するので、箱か実アセットかの分岐は
    /// シーン生成時に一度決まって焼き込まれる。** 実行時に GUID を解決する
    /// 必要が無い。
    /// </summary>
    public sealed class CockpitDefinition
    {
        /// <summary>箱コックピットの Id。**アセットが無いときのフォールバック。**</summary>
        public const string BoxId = "box";

        /// <summary>Hi-Rez の無料サンプル (Step 11-1 で取り込む)。</summary>
        public const string HiRezSampleId = "hirez-sample";

        public CockpitDefinition(string id, string prefabGuid, string fallbackId)
        {
            Id = id;
            PrefabGuid = prefabGuid;
            FallbackId = fallbackId;
        }

        /// <summary>定義の名前。**シーンに焼き込まれ、HUD とテストから読める。**</summary>
        public string Id { get; }

        /// <summary>
        /// プレハブの GUID。**箱は null / 空。**
        /// 実在するかは Editor 側が確かめる（ここでは分からない）。
        /// </summary>
        public string PrefabGuid { get; }

        /// <summary>取り込まれていなかったときに落ちる先の Id。箱自身は null。</summary>
        public string FallbackId { get; }

        /// <summary>プレハブを要求する定義か。箱は false。</summary>
        public bool NeedsPrefab => !string.IsNullOrEmpty(PrefabGuid);

        /// <summary>箱コックピット。**取り込みが無くてもこれで組める。**</summary>
        public static CockpitDefinition Box { get; } =
            new CockpitDefinition(BoxId, prefabGuid: null, fallbackId: null);

        public override string ToString() => Id;
    }
}
