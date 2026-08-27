using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>音の用途。取り込み設定はここで分かれる。</summary>
    public enum AudioUse
    {
        /// <summary>常時鳴るループ。Compressed In Memory / Vorbis。</summary>
        Loop,

        /// <summary>単発。Decompress On Load / PCM。発音遅延を作らない。</summary>
        OneShot,
    }

    /// <summary>取り込む 1 ファイルの定義。</summary>
    public sealed class AudioAsset
    {
        public string Output;          // Assets/Audio/ 内の名前
        public string SourceRelative;  // source_assets/audio/ からの相対
        public AudioUse Use;
        public bool ForceMono;         // 2ch の単発は ON
        public bool Generated;         // 加工して作る（原本をそのままコピーしない）
    }

    /// <summary>
    /// 音素材を unity/Assets/Audio/ へ取り込み、インポート設定を当てる (Step 10-1)。
    ///
    /// **手作業を残さない。** 設定は AudioImporter からコードで当てる。
    ///
    /// **加工済みクリップはコミットする（唯一の配布物）。**
    /// source_assets/ は .gitignore の内側なので clone した環境には原本が無く、
    /// 「取り込み時に原本から生成する」案は成立しない。テクスチャと同じ契約。
    /// </summary>
    public static class AudioImportSetup
    {
        public const string AudioFolder = "Assets/Audio";

        /// <summary>
        /// 原本の置き場所。**gitignore の内側。**
        /// Application.dataPath は `<repo>/unity/Assets` なので、
        /// リポジトリ直下まで 2 つ上がる。
        /// </summary>
        public const string SourceFolder = "../../source_assets/audio";

        /// <summary>
        /// 採用素材。**全て Kenney の CC0**（docs/asset-sources.md / audio-candidates.md）。
        /// </summary>
        public static readonly AudioAsset[] Assets =
        {
            // ---- ループ 2 本。加工してから取り込む ----
            new AudioAsset
            {
                Output = "engine_loop.wav",
                SourceRelative = "kenney/sci-fi-sounds/Audio/spaceEngineLow_003.ogg",
                Use = AudioUse.Loop, ForceMono = false, Generated = true,
            },
            new AudioAsset
            {
                Output = "cockpit_loop.wav",
                SourceRelative = "kenney/sci-fi-sounds/Audio/forceField_000.ogg",
                Use = AudioUse.Loop, ForceMono = false, Generated = true,
            },

            // ---- 単発 5 本。そのままコピー ----
            new AudioAsset
            {
                Output = "dock_impact.ogg",
                SourceRelative = "kenney/impact-sounds/Audio/impactPlate_heavy_001.ogg",
                Use = AudioUse.OneShot, ForceMono = true, Generated = false,
            },
            new AudioAsset
            {
                Output = "undock.ogg",
                SourceRelative = "kenney/interface-sounds/Audio/switch_004.ogg",
                Use = AudioUse.OneShot, ForceMono = true, Generated = false,
            },
            new AudioAsset
            {
                Output = "ui_select.ogg",
                SourceRelative = "kenney/interface-sounds/Audio/select_001.ogg",
                Use = AudioUse.OneShot, ForceMono = true, Generated = false,
            },
            new AudioAsset
            {
                Output = "ui_confirm.ogg",
                SourceRelative = "kenney/interface-sounds/Audio/confirmation_003.ogg",
                Use = AudioUse.OneShot, ForceMono = false, Generated = false,
            },
            new AudioAsset
            {
                Output = "warning.ogg",
                SourceRelative = "kenney/interface-sounds/Audio/error_008.ogg",
                Use = AudioUse.OneShot, ForceMono = false, Generated = false,
            },
        };

        /// <summary>
        /// **拡張子のホワイトリスト (8-1 と同じルール)。**
        /// desktop.ini などが混入していた実績があるので、拾うほうを固定する。
        /// </summary>
        public static readonly string[] AllowedExtensions = { ".ogg", ".wav" };

        public static string AssetPath(string fileName) => AudioFolder + "/" + fileName;

        public static string SourcePath(string relative)
            => Path.GetFullPath(Path.Combine(Application.dataPath, SourceFolder, relative));

        /// <summary>取り込み対象として拾ってよいか。**除外したものは呼び手がログに出す。**</summary>
        public static bool IsAdoptable(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            string name = Path.GetFileName(fileName);
            if (name.StartsWith(".") || name.StartsWith("_"))
            {
                return false;
            }

            if (string.Equals(name, "Preview.ogg", System.StringComparison.OrdinalIgnoreCase))
            {
                return false; // パック全体の試聴用。素材ではない
            }

            string ext = Path.GetExtension(name).ToLowerInvariant();
            foreach (string allowed in AllowedExtensions)
            {
                if (ext == allowed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取り込み（コピー＋加工）とインポート設定。</summary>
        public static void Run()
        {
            Directory.CreateDirectory(AudioFolder);

            var skipped = new List<string>();
            foreach (AudioAsset asset in Assets)
            {
                if (asset.Generated)
                {
                    AudioLoopBuilder.Build(asset);
                    continue;
                }

                string source = SourcePath(asset.SourceRelative);
                if (!IsAdoptable(source))
                {
                    skipped.Add(asset.SourceRelative + " (拡張子/名前で除外)");
                    continue;
                }

                if (!File.Exists(source))
                {
                    // **既に取り込み済みなら原本は要らない。** clone した環境がこれ。
                    if (File.Exists(AssetPath(asset.Output)))
                    {
                        continue;
                    }

                    throw new FileNotFoundException(
                        $"原本が無く、取り込み済みでもない: {source}", source);
                }

                File.Copy(source, AssetPath(asset.Output), overwrite: true);
            }

            AudioLoopBuilder.ReportSkipped(skipped);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (AudioAsset asset in Assets)
            {
                Configure(asset);
            }

            AssetDatabase.SaveAssets();
            LogSummary();
        }

        /// <summary>
        /// インポート設定を当てる (計画書 10-1 の決定表)。
        ///   ループ  Compressed In Memory / Vorbis
        ///   単発    Decompress On Load / PCM
        ///   preloadAudioData は全て ON、2ch の単発は forceToMono ON
        /// </summary>
        public static void Configure(AudioAsset asset)
        {
            string path = AssetPath(asset.Output);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                throw new InvalidDataException("AudioImporter が取れない: " + path);
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = asset.Use == AudioUse.Loop
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = asset.Use == AudioUse.Loop
                ? AudioCompressionFormat.Vorbis
                : AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = asset.ForceMono;
            importer.loadInBackground = false;
            importer.ambisonic = false;
            importer.SaveAndReimport();
        }

        static void LogSummary()
        {
            var sb = new StringBuilder("[AudioImportSetup] 取り込み結果");
            foreach (AudioAsset asset in Assets)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetPath(asset.Output));
                sb.AppendLine();
                sb.Append(clip == null
                    ? $"  {asset.Output,-18} 読めない"
                    : $"  {asset.Output,-18} {clip.length,6:F3} s / {clip.frequency} Hz / " +
                      $"{clip.channels} ch / {asset.Use}");
            }

            Debug.Log(sb.ToString());
        }
    }
}
