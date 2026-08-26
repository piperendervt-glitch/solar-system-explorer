using System.IO;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// セーブファイルの置き場と読み書き (Step 7)。
    ///
    /// 実体は Core の <see cref="SaveCodec"/>。ここは「どこに置くか」と
    /// 「ファイル IO で落ちないこと」だけを引き受ける。
    ///
    /// **読み書きのどちらも例外を投げない。** 読めなければ null を返し、
    /// 呼び手 (SaveResolver) が地球ステーションへ倒す。
    /// </summary>
    public static class SaveFile
    {
        public const string FileName = "solar-system-explorer.save.json";

        /// <summary>テストから差し替えるための口。null なら既定の場所。</summary>
        public static string OverridePath { get; set; }

        public static string Path =>
            !string.IsNullOrEmpty(OverridePath)
                ? OverridePath
                : System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists()
        {
            try
            {
                return File.Exists(Path);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>読めた JSON。無い / 読めないなら null。</summary>
        public static string ReadRaw()
        {
            try
            {
                return File.Exists(Path) ? File.ReadAllText(Path) : null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveFile] 読めなかった: {e.Message}");
                return null;
            }
        }

        public static void Save(string stationName)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(Path, SaveCodec.Serialize(stationName));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveFile] 書けなかった: {e.Message}");
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveFile] 消せなかった: {e.Message}");
            }
        }

        /// <summary>保存されたステーション番号。無い / 壊れていれば地球 (0)。</summary>
        public static int LoadStationIndex(SolarSystemModel model) =>
            SaveResolver.Resolve(ReadRaw(), model);
    }
}
