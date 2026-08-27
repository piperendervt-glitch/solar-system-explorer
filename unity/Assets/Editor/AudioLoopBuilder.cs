using System;
using System.Collections.Generic;
using System.IO;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// ループ 2 本を加工して .wav として書き出す (Step 10-1)。
    ///
    /// **ffmpeg ではなく C# で行う。** run_unity.ps1 だけで完結し、ffmpeg への
    /// 環境依存を CLAUDE.md の前提に持ち込まないため。加工そのものは
    /// SolarSystem.Core.AudioAnalysis の純関数で、EditMode から検証できる。
    ///
    /// **加工パラメータはここが唯一の出所。** docs/audio-candidates.md には
    /// 同じ値と加工後の実測値を記録するが、正はこちら。
    /// （以前 source_assets/audio/preview/make_forcefield_loop.sh に同じ加工が
    ///   あったが、二重管理になるので C# 化に合わせて削除した）
    ///
    /// 原本は source_assets/（gitignore の内側）にあり、AudioClip として読むには
    /// Assets/ の下に無いといけない。**一時フォルダへコピーして読み、消す。**
    /// テクスチャ（PlanetTextureSetup）と同じ手口。
    /// </summary>
    public static class AudioLoopBuilder
    {
        const string TempFolder = "Assets/Audio/_import_tmp";

        // ---- エンジン (spaceEngineLow_003) ----
        // 問題はループ端のクリックだけ（段差比 85.18 倍）。周期 5 秒は要件を満たす。
        // 末尾を先頭へクロスフェードする。

        /// <summary>エンジンのクロスフェード長 [秒]。計画書 10-1 の 50〜100 ms の上側。</summary>
        public const double EngineCrossfadeSeconds = 0.100;

        // ---- コックピット (forceField_000) ----
        // 段差は元々 0.05 倍で問題無い。問題は周期が 0.954 秒と短いこと。
        // ピッチ違いの 3 層を開始位置をずらして重ね、8 秒に伸ばす。

        public static readonly double[] CockpitPitches = { 0.97, 1.00, 1.03 };
        public static readonly double[] CockpitOffsetsSeconds = { 0.00, 0.31, 0.62 };
        public const double CockpitLengthSeconds = 8.0;
        public const double CockpitCrossfadeSeconds = 0.2;

        /// <summary>
        /// 段差比の上限。
        ///
        /// **1 前後が理想。** クロスフェードは連結点の両側を元素材の隣り合う
        /// サンプルにするので、段差は「ふつうの隣接サンプル差 1 個ぶん」に落ちる。
        /// ただし個々の差は平均のまわりにばらつくので、ちょうど 1 にはならない。
        ///
        /// **実測: エンジンは加工前 85.18 → 加工後 3.11。**
        /// 上限 8.0 は、加工前の 85.18 に対して 1 桁の余裕を残しつつ、
        /// 加工が壊れた（クロスフェードが効いていない）ことは確実に捕まえる値。
        /// 最初 3.0 と置いたが根拠が無く、実測 3.11 をわずかに割っただけだった。
        /// </summary>
        public const double SeamRatioLimit = 8.0;

        public static void ReportSkipped(IReadOnlyList<string> skipped)
        {
            if (skipped == null || skipped.Count == 0)
            {
                Debug.Log("[AudioLoopBuilder] 除外したファイル: 0 件");
                return;
            }

            Debug.Log($"[AudioLoopBuilder] 除外したファイル: {skipped.Count} 件\n  "
                      + string.Join("\n  ", skipped));
        }

        public static void Build(AudioAsset asset)
        {
            string output = AudioImportSetup.AssetPath(asset.Output);
            string source = AudioImportSetup.SourcePath(asset.SourceRelative);

            if (!File.Exists(source))
            {
                // **原本が無くても、加工済みがあればそれでよい。** clone した環境がこれ。
                if (File.Exists(output))
                {
                    return;
                }

                throw new FileNotFoundException(
                    $"原本が無く、加工済みでもない: {source}", source);
            }

            float[] samples = ReadSource(source, out int sampleRate, out int channels);
            if (channels > 1)
            {
                samples = ToMono(samples, channels);
            }

            float[] processed = asset.Output.StartsWith("engine")
                ? AudioAnalysis.CrossfadeLoop(
                    samples, (int)Math.Round(EngineCrossfadeSeconds * sampleRate))
                : AudioAnalysis.BuildLayeredLoop(
                    samples, sampleRate, CockpitPitches, CockpitOffsetsSeconds,
                    CockpitLengthSeconds, CockpitCrossfadeSeconds);

            double before = AudioAnalysis.SeamRatio(samples);
            double after = AudioAnalysis.SeamRatio(processed);

            Debug.Log(string.Format(
                "[AudioLoopBuilder] {0}\n" +
                "  加工前 {1,8:F3} s / ピーク {2:F4} / 段差比 {3,8:F2}\n" +
                "  加工後 {4,8:F3} s / ピーク {5:F4} / 段差比 {6,8:F2}  (上限 {7:F1})",
                asset.Output,
                samples.Length / (double)sampleRate, AudioAnalysis.Peak(samples), before,
                processed.Length / (double)sampleRate, AudioAnalysis.Peak(processed), after,
                SeamRatioLimit));

            if (after > SeamRatioLimit)
            {
                throw new InvalidOperationException(
                    $"{asset.Output} の段差比 {after:F2} が上限 {SeamRatioLimit:F1} を超えた");
            }

            File.WriteAllBytes(output, EncodeWav16(processed, sampleRate, 1));
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// 原本を一時フォルダへコピーして AudioClip として読む。
        /// **Decompress On Load / PCM で入れる。** GetData に圧縮のままでは通らない。
        /// </summary>
        static float[] ReadSource(string source, out int sampleRate, out int channels)
        {
            Directory.CreateDirectory(TempFolder);
            string temp = TempFolder + "/" + Path.GetFileName(source);

            try
            {
                File.Copy(source, temp, overwrite: true);
                AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);

                var importer = (AudioImporter)AssetImporter.GetAtPath(temp);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.SaveAndReimport();

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(temp);
                if (clip == null)
                {
                    throw new InvalidDataException("AudioClip として読めない: " + temp);
                }

                sampleRate = clip.frequency;
                channels = clip.channels;

                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0))
                {
                    throw new InvalidDataException("GetData に失敗した: " + temp);
                }

                return data;
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        static float[] ToMono(float[] interleaved, int channels)
        {
            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[i * channels + c];
                }

                mono[i] = sum / channels;
            }

            return mono;
        }

        /// <summary>
        /// 16bit PCM の WAV を読み戻す。
        ///
        /// **本番のインポート設定 (Compressed In Memory) では AudioClip.GetData が
        /// 通らない。** 検証はコミットされる .wav そのものを読んで行う。
        /// インポート設定に依存しないぶん、こちらのほうが確か。
        /// </summary>
        public static float[] DecodeWav16(byte[] bytes, out int sampleRate, out int channels)
        {
            if (bytes == null || bytes.Length < 44)
            {
                throw new InvalidDataException("WAV として短すぎる");
            }

            channels = BitConverter.ToInt16(bytes, 22);
            sampleRate = BitConverter.ToInt32(bytes, 24);

            // data チャンクを探す。fmt の後ろに別のチャンクが挟まることがある。
            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                int size = BitConverter.ToInt32(bytes, offset + 4);
                if (id == "data")
                {
                    int count = size / 2;
                    var samples = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        samples[i] = BitConverter.ToInt16(bytes, offset + 8 + i * 2) / (float)short.MaxValue;
                    }

                    return samples;
                }

                offset += 8 + size + (size & 1);
            }

            throw new InvalidDataException("data チャンクが見つからない");
        }

        /// <summary>16bit PCM の WAV に符号化する。</summary>
        public static byte[] EncodeWav16(float[] samples, int sampleRate, int channels)
        {
            int dataBytes = samples.Length * 2;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);                                   // fmt チャンクの長さ
                w.Write((short)1);                             // PCM
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);            // byte / 秒
                w.Write((short)(channels * 2));                // ブロック境界
                w.Write((short)16);                            // bit / サンプル
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);

                for (int i = 0; i < samples.Length; i++)
                {
                    float v = samples[i];
                    if (v > 1f) { v = 1f; }
                    if (v < -1f) { v = -1f; }
                    w.Write((short)Math.Round(v * short.MaxValue));
                }

                w.Flush();
                return stream.ToArray();
            }
        }
    }
}
