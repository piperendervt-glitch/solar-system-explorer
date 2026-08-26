using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// Windows 向けスタンドアロンビルド (Step 7)。
    ///
    ///   run_unity.ps1 -Method SolarSetup.Build
    ///
    /// 出力は リポジトリ直下の build/。build/ は .gitignore 済み。
    /// GUI は使わない。BuildPipeline.BuildPlayer をそのまま呼ぶだけ。
    /// </summary>
    public static class PlayerBuilder
    {
        public const string OutputDirectory = "build";
        public const string ExecutableName = "SolarSystemExplorer.exe";
        public const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>リポジトリ直下の build/ の絶対パス。</summary>
        public static string OutputRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../..", OutputDirectory));

        public static string ExecutablePath => Path.Combine(OutputRoot, ExecutableName);

        public static void Build()
        {
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenePath))))
            {
                throw new FileNotFoundException(
                    $"シーンが無い: {ScenePath} — 先に SolarSetup.Run を実行すること");
            }

            Directory.CreateDirectory(OutputRoot);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ExecutablePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[PlayerBuilder] {summary.result} / {summary.totalSize / (1024 * 1024)} MB / " +
                      $"{summary.totalTime.TotalSeconds:F1} 秒 -> {ExecutablePath}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException(
                    $"ビルド失敗: {summary.result} / エラー {summary.totalErrors} 件");
            }
        }
    }
}
