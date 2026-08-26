using System.Collections;
using System.IO;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// スタンドアロン exe からスクショを 1 枚撮って終了する (Step 7)。
    ///
    /// コマンドライン引数が無ければ何もしない。通常起動には一切影響しない。
    ///
    ///   SolarSystemExplorer.exe -captureShot C:\path\shot.png -captureFrames 180
    ///
    /// CaptureScreenshot ではなく CaptureScreenshotAsTexture を使う。
    /// 前者は書き出しが非同期で、いつ終わるか分からないまま Quit すると
    /// ファイルが空のまま残る。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StandaloneCapture : MonoBehaviour
    {
        public const string PathArg = "-captureShot";
        public const string FramesArg = "-captureFrames";

        /// <summary>既定の待ちフレーム数。シーンが落ち着くまで待つ。</summary>
        public const int DefaultFrames = 180;

        IEnumerator Start()
        {
            string path = ArgValue(PathArg);
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            int frames = DefaultFrames;
            string framesText = ArgValue(FramesArg);
            if (!string.IsNullOrEmpty(framesText) && int.TryParse(framesText, out int parsed) && parsed > 0)
            {
                frames = parsed;
            }

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            yield return new WaitForEndOfFrame();

            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log($"[StandaloneCapture] {path} ({texture.width}x{texture.height})");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[StandaloneCapture] 書けなかった: {e.Message}");
            }
            finally
            {
                Destroy(texture);
            }

            yield return null;
            Application.Quit(0);
        }

        /// <summary>--name value 形式の引数を読む。無ければ null。</summary>
        public static string ArgValue(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, System.StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
