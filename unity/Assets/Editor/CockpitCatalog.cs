using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// どの `CockpitDefinition` で組むかを決める (Step 11-0c)。
    ///
    /// ■ **判定は「プレハブ GUID の存在」で行う。フォルダの有無では行わない。**
    /// `unity/Assets/ThirdParty/*` は追跡除外なので、**フォルダだけが残る**状態が
    /// 起こりうる（`.meta` を追跡していれば空フォルダが復元される。だから
    /// `ThirdParty.meta` も追跡しないことにした）。`AssetDatabase.IsValidFolder`
    /// で見ると、空フォルダを「アセットがある」と誤判定する。
    ///
    /// ■ ここが Editor 側にある理由
    /// GUID からプレハブを引くのは `AssetDatabase` で、Editor 専用だから。
    /// **これは制約ではなく正しい形。** シーンは Editor スクリプトで毎回まっさら
    /// 生成するので、分岐はシーン生成時に一度決まって焼き込まれる。
    /// 実行時に GUID を解決する必要が無い。
    /// </summary>
    public static class CockpitCatalog
    {
        /// <summary>取り込み先。**追跡除外**（EULA のため）。</summary>
        public const string ThirdPartyFolder = "Assets/ThirdParty";

        /// <summary>
        /// 既定で組もうとする定義。
        /// **取り込まれていなければ箱へ落ちる**（Resolve が面倒を見る）ので、
        /// アセットが無いクローンでもシーン生成は通る。
        ///
        /// ■ 11-2a で実アセットへ切り替えた
        /// `CockpitBuilder` が定義からプレハブを置くようになったので、要求を
        /// `HiRezSample` に戻した。**取り込まれていなければ箱へ落ちる**ので、
        /// アセットが無いクローンでもシーン生成は通る（落ちたことは
        /// `CockpitIdentity` に残る）。
        /// </summary>
        public static CockpitDefinition Requested => CockpitDefinition.HiRezSample;

        /// <summary>プレハブ GUID が実在し、プレハブとして読めるか。</summary>
        public static bool IsAvailable(CockpitDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (!definition.NeedsPrefab)
            {
                return true; // 箱はいつでも組める
            }

            string path = AssetDatabase.GUIDToAssetPath(definition.PrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // **パスが返っても実体が無いことがある。** 読めるところまで確かめる。
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        /// <summary>
        /// 実際に組む定義を決める。取り込まれていなければフォールバックへ落ちる。
        /// **落ちたことは戻り値で分かるようにする**（黙って箱にしない）。
        /// </summary>
        public static CockpitDefinition Resolve(CockpitDefinition requested, out bool fellBackToBox)
        {
            fellBackToBox = false;
            if (requested == null)
            {
                return CockpitDefinition.Box;
            }

            if (IsAvailable(requested))
            {
                return requested;
            }

            fellBackToBox = true;
            Debug.Log($"[CockpitCatalog] '{requested.Id}' が取り込まれていないので "
                      + $"'{CockpitDefinition.BoxId}' で組む。"
                      + $"取り込み手順は docs/asset-sources.md を参照。");
            return CockpitDefinition.Box;
        }
    }
}
