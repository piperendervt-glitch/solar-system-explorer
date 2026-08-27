using System;
using System.IO;
using SolarSystem.Core;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 取り込む `.unitypackage` の定義 (Step 11-1a)。
    ///
    /// **削除リストをここに持つ。** 再取り込みしても同じ結果になるようにするため、
    /// 「取り込んだあと何を消すか」がコードに固定されている必要がある。
    ///
    /// メタ情報は**パッケージ自身の gzip FEXTRA から読み出したもの**で、
    /// 推測ではない。同じ値を docs/asset-sources.md にも記録している。
    /// </summary>
    public static class CockpitPackage
    {
        // ---- パッケージの識別（FEXTRA の JSON から実測） ----
        public const string Title = "Hi-Rez Spaceships Creator Free Sample";
        public const string Publisher = "Ebal Studios";
        public const string StoreId = "153363";
        public const string Version = "1.31";
        public const string UnityVersion = "2022.3.62f3";
        public const string Category = "3D Models/Vehicles/Space";

        /// <summary>パッケージが記録している元のフォルダ。**宛先はここではない。**</summary>
        public const string SourceRoot = "Assets/HiRezSpaceshipsCreatorFree";

        /// <summary>取り込み先。**`.gitignore` の内側**（EULA のため）。</summary>
        public const string DestinationRoot =
            "Assets/ThirdParty/EbalStudios/HiRezSpaceshipsCreatorFree";

        /// <summary>取り込み前に作るフォルダ。clone 直後には存在しない。</summary>
        public static readonly string[] FoldersToCreate =
        {
            "Assets/ThirdParty",
            "Assets/ThirdParty/EbalStudios",
        };

        /// <summary>エントリ数。**実測 178。** 書き換えで増減したら止める。</summary>
        public const int ExpectedEntryCount = 178;

        // ---- プレハブの GUID ----
        // .unitypackage は**フォルダ名が GUID** なので取り込む前に読める。
        // 取り込み後に GUIDToAssetPath で解決できることをテストで縛る。

        /// <summary>
        /// 内装付きコックピットのプレハブ。
        /// **定義は Core の `CockpitDefinition` 側にしかない。**
        /// ここに書き写すと二重定義になり、片方だけ直る事故が起きる。
        /// </summary>
        public static string CockpitWithInteriorGuid =>
            CockpitDefinition.HiRezSample.PrefabGuid;

        /// <summary>
        /// 取り込み後に消すもの (17 件 / 54.35 MB)。
        ///
        /// **テクスチャは消さない。** Unity はどのシーンからも参照されないアセットを
        /// ビルドに含めない（Resources/ と Addressables を除く）ので、翼・機体・
        /// エンジン用の約 97MB は exe に乗らない。消しても効くのは Editor の
        /// インポート時間と Library の容量だけで、再取り込みが要る分だけ損になる。
        /// </summary>
        public static readonly string[] DeleteAfterImport =
        {
            // 入れ子のパッケージ。Unity からは中身不明のバイナリで、URP では不要。
            "Legacy/HDRP.unitypackage",          // 53.20 MB
            "Legacy/Built-In.unitypackage",      //  0.41 MB

            // デモシーン。
            "Scenes/Examples.unity",
            "Scenes/Modules.unity",
            "Scenes/LightingSettings.lighting",

            // 例示プレハブ。使うのは Prefabs/Modules/ 側だけ。
            "Prefabs/Examples/Example1_Grey.prefab",
            "Prefabs/Examples/Example1_Red.prefab",
            "Prefabs/Examples/Example2_Grey.prefab",
            "Prefabs/Examples/Example3_Red.prefab",
            "Prefabs/Examples/Example4_Grey.prefab",
            "Prefabs/Examples/Example5_Grey.prefab",
            "Prefabs/ExamplesNoInterior/Example1NoInterior_Grey.prefab",
            "Prefabs/ExamplesNoInterior/Example1NoInterior_Red.prefab",
            "Prefabs/ExamplesNoInterior/Example2NoInterior_Grey.prefab",
            "Prefabs/ExamplesNoInterior/Example3NoInterior_Red.prefab",
            "Prefabs/ExamplesNoInterior/Example4NoInterior_Grey.prefab",
            "Prefabs/ExamplesNoInterior/Example5_NoInteriorGrey.prefab",
        };

        /// <summary>削除対象の絶対 Assets パス。</summary>
        public static string DeletePath(string relative) => DestinationRoot + "/" + relative;

        /// <summary>
        /// 既定の `.unitypackage` の場所。
        /// **リポジトリの外。** `-package` 引数で上書きできる。
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

        /// <summary>元のパスを取り込み先へ写す。</summary>
        public static string MapToDestination(string original)
        {
            string trimmed = original.Trim();
            if (!trimmed.StartsWith(SourceRoot, StringComparison.Ordinal))
            {
                return trimmed;
            }

            return DestinationRoot + trimmed.Substring(SourceRoot.Length);
        }
    }
}
