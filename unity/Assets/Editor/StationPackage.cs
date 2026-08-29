using System;
using System.IO;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **ステーションアセットの識別と取り込み先 (Step 13-1b / 13-2)。**
    /// `CockpitPackage` と対。
    ///
    /// ■ 構造が Hi-Rez と違う
    /// このパッケージは**入れ子**になっている。外側のトップレベルが Built-in 版で、
    /// `URP/URP.unitypackage`（639.97 MB）と `HDRP/HDRP.unitypackage`（571.11 MB）が
    /// **中に .unitypackage ファイルとして入っている。**
    /// 入れ子の URP は 68 ファイル 2451.39 MB の**完全な一式**で、テクスチャも
    /// FBX もプレハブも重複して持つ（棚卸し: `verify/cobble-package-contents.txt`）。
    ///
    /// ■ **経路 C: 入れ子の URP だけを取り出して取り込む**
    /// 外側（Built-in）も HDRP も `Assets/` に一切書かない。
    /// 3 案のうち `Assets/ThirdParty` に入る量が最小で、URP マテリアルが最初から付く。
    /// </summary>
    public static class StationPackage
    {
        // ---- パッケージの識別（FEXTRA の JSON から実測） ----
        public const string Title = "Space Station Free 3D Asset HDRP URP Built-In";
        public const string Publisher = "Cobble Games";
        public const string StoreId = "188734";
        public const string Version = "1.2";
        public const string UnityVersion = "2019.4.22f1";
        public const string Category = "3D Models/Vehicles/Space";
        public const string PublishDate = "14 Dec 2021";

        /// <summary>入れ子の URP パッケージのパス（外側の中での位置）。</summary>
        public const string NestedUrpPath = "Assets/Cobble Games/Spaceship/URP/URP.unitypackage";

        /// <summary>入れ子の URP が記録している元のルート。</summary>
        public const string SourceRoot = "Assets/Cobble Games/Spaceship";

        /// <summary>取り込み先。**追跡除外**。</summary>
        public const string DestinationRoot = "Assets/ThirdParty/CobbleGames/SpaceStationFree";

        /// <summary>外側の実ファイル数（棚卸しの実測）。</summary>
        public const int OuterEntryCount = 68;

        /// <summary>入れ子 URP の実ファイル数（棚卸しの実測）。</summary>
        public const int NestedEntryCount = 68;

        /// <summary>
        /// **取り込まないもの。** 参照関係を実測してから決めた
        /// （誰が誰を参照しているかは `verify/cobble-package-contents.txt` の元になった走査）。
        ///
        /// **テクスチャは消さない。** 参照されないアセットは exe に乗らないので、
        /// 消しても効くのはインポート時間と Library の容量だけで、しかも消すと
        /// URP 版のマテリアルが参照を失う（Demo 3 と同じ判断）。
        /// </summary>
        public static readonly string[] ExcludeFromImport =
        {
            // デモシーン。**どこからも参照されていない**（参照の頂点）。
            "Scenes/SampleScene.unity",

            // **組み込みシェーダ (fileID 108) を使う唯一のマテリアル。**
            // 参照元は SampleScene だけ。残すと「ThirdParty の全マテリアルが URP」を
            // 文字どおり通せない（Hi-Rez の SpaceBG.mat と同じ形）。
            "hdri.mat",

            // 上の hdri.mat だけが参照している HDRI（18.33 MB）。
            "hdr.png",

            // このプロジェクトは Assets/Settings/PC_RPAsset.asset を使う。
            // どこからも参照されておらず、置く理由が無い。
            "Prefabs/UniversalRenderPipelineAsset.asset",

            // 上の RP アセットだけが参照している Renderer。
            "Prefabs/UniversalRenderPipelineAsset_Renderer.asset",
        };

        /// <summary>
        /// **Emission を持つマテリアル（実測）。** 4 件しかない。
        /// 13-4 の窓・航法灯はこの制約の中で作ることになる。
        /// </summary>
        public static readonly string[] EmissiveMaterials =
        {
            "module6", "module7", "module10", "module11",
        };

        // ---- 取り込み後の実測 (Step 13-2 / 2026-08-29) ----

        /// <summary>ステーションのプレハブ GUID。**取り込み済みかの判定はこれで行う**
        /// （フォルダの有無で見ない。追跡除外なので空フォルダが残りうる）。</summary>
        public const string StationPrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        /// <summary>取り込み後のファイル数（.meta を含む / 実測）。</summary>
        public const int ImportedFileCount = 130;

        /// <summary>取り込み後のバイト数（.meta を含む / 実測）。</summary>
        public const long ImportedBytes = 2551339973L;

        /// <summary>
        /// **サイズの上限。実測の +15%。**
        /// 「いつの間にか別の版や HDRP 一式が入っていた」を捕まえるための線で、
        /// **良し悪しの線ではない。** 意図して増やしたときは実測を採り直して上げる。
        /// </summary>
        public const long MaxImportedBytes = (long)(ImportedBytes * 1.15);

        /// <summary>マテリアルの件数（実測）。**全件が URP/Lit。**</summary>
        public const int MaterialCount = 15;

        /// <summary>取り込み先の絶対 Assets パス。</summary>
        public static string DestinationPath(string relative) => DestinationRoot + "/" + relative;

        /// <summary>
        /// 既定の `.unitypackage` の場所。**リポジトリの外。**
        /// `-package` 引数で上書きできる。
        /// </summary>
        public static string DefaultPackagePath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Unity", "Asset Store-5.x", Publisher,
                                    "3D ModelsVehiclesSpace", Title + ".unitypackage");
            }
        }

        /// <summary>
        /// 元のパスを取り込み先へ写す。
        /// **pathname は 2 行あることがある**（1 行目がパス、2 行目は "00"）。
        /// 1 行目だけを写し、**残りはそのまま付け直す**（Unity が書いた形を壊さない）。
        /// </summary>
        public static string MapToDestination(string original)
        {
            int newline = original.IndexOfAny(NewlineChars);
            string head = newline >= 0 ? original.Substring(0, newline) : original;
            string tail = newline >= 0 ? original.Substring(newline) : string.Empty;

            string trimmed = head.Trim();
            if (!trimmed.StartsWith(SourceRoot, StringComparison.Ordinal))
            {
                return trimmed + tail;
            }

            return DestinationRoot + trimmed.Substring(SourceRoot.Length) + tail;
        }

        /// <summary>CR / LF。**エスケープを書かずに持つ**。</summary>
        static readonly char[] NewlineChars = { (char)13, (char)10 };
    }
}
