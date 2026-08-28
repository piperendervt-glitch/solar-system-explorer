using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>見た目の仕上げ (Step 6) の PlayMode 検証。</summary>
    public sealed class PolishPlayModeTests
    {
        const int Width = 1920;
        const int Height = 1080;
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        DebugOverlay _overlay;

        static string ShotDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify/shots"));

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // セーブは一時ファイルへ逃がす (Step 7)。
            // 本物の persistentDataPath を汚すと、他のテストの開始地点が変わる。
            SaveFile.OverridePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "solar-system-explorer-polish.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _overlay = Object.FindAnyObjectByType<DebugOverlay>();

            Assert.That(_root, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_stack, Is.Not.Null);
            Directory.CreateDirectory(ShotDirectory);
        }

        void AimShip(Vec3d direction) => AimShip(direction, 0f);

        /// <summary>
        /// pitchDownDegrees だけ機首を下げる。対象は画面の上側へ移動するので、
        /// 中央下の計器パネルに隠れる被写体 (太陽) を写すのに使う。
        /// </summary>
        void AimShip(Vec3d direction, float pitchDownDegrees)
        {
            var f = new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z);
            if (f.sqrMagnitude > 0f)
            {
                _rig.ShipTransform.rotation =
                    Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(pitchDownDegrees, 0f, 0f);
            }
        }

        void Settle(int frames = 12)
        {
            for (int i = 0; i < frames; i++)
            {
                _root.Tick(Dt);
            }
        }

        // ---- (b) 航海の 3 場面 ----

        [UnityTest]
        public IEnumerator 航海の3場面をスクショに撮る()
        {
            SpaceStation earth = _root.Model.Stations[0];
            SpaceStation mars = _root.Model.Stations[1];
            _rig.SetTargetIndex(1);

            // (1) 地球ステーション出港時
            _root.PlaceObserver(earth.PortPosition + earth.PortDirection * 3.0);
            AimShip((mars.PortPosition - _root.Ship.Position).Normalized);
            Settle();
            yield return null;
            Capture("6_01_depart_earth");
            Debug.Log("[Step6] (1) 出港");
            Debug.Log(_overlay.BuildText());

            // (2) 巡航中 (中間点)
            Vec3d mid = earth.PortPosition + (mars.PortPosition - earth.PortPosition) * 0.5;
            _root.PlaceObserver(mid);
            AimShip((mars.PortPosition - mid).Normalized);
            _rig.EngageAutopilot(_root, mars.PortPosition);
            Settle();
            yield return null;
            Capture("6_02_cruise");
            Debug.Log("[Step6] (2) 巡航");
            Debug.Log(_overlay.BuildText());
            _rig.Autopilot.Disengage();

            // (3) 火星到着時 (到着圏から母天体を見る)
            Vec3d arrival = mars.AbsolutePosition
                            + mars.PortDirection * UniverseConstants.ArrivalRadiusUnits;
            _root.PlaceObserver(arrival);
            AimShip((mars.Host.AbsolutePosition - arrival).Normalized);
            Settle();
            yield return null;
            Capture("6_03_arrive_mars");
            Debug.Log("[Step6] (3) 到着");
            Debug.Log(_overlay.BuildText());
        }

        // ---- (c) ポストプロセス 3 段階 ----

        [UnityTest]
        public IEnumerator ポストプロセス3段階の比較を撮る()
        {
            PostProcessPreset post = _root.Post;
            Assert.That(post, Is.Not.Null, "PostProcessPreset が無い");

            SpaceStation mars = _root.Model.Stations[1];
            _rig.SetTargetIndex(1);

            Vec3d arrival = mars.AbsolutePosition
                            + mars.PortDirection * UniverseConstants.ArrivalRadiusUnits;
            _root.PlaceObserver(arrival);

            var strengths = new[]
            {
                PostProcessStrength.Subtle, PostProcessStrength.Medium, PostProcessStrength.Strong,
            };

            // 太陽を向いた絵 (Bloom とフレアの効きが見える)。
            // 真正面だと計器パネルの真後ろに隠れるので 15 度だけ機首を下げる。
            AimShip((_root.Model.Sun.AbsolutePosition - arrival).Normalized, 15f);
            Settle();

            foreach (PostProcessStrength strength in strengths)
            {
                post.Apply(strength);
                _root.Tick(Dt);
                yield return null;

                // **Volume スタックの解決には「カメラが描くこと」が要る。**
                // 11-3c まではこれが偶然満たされていた——下端の帯の計器カメラが
                // targetTexture 付きで常時有効だったため、batchmode でも毎フレーム
                // 描かれていた。帯を撤去したら誰も描かなくなり、解決後の値が
                // 前のまま (3.0) 残った（実測）。**測定を偶然に頼らない。**
                UnityEngine.Rendering.VolumeManager.instance.Update(
                    post.Volume.transform, ~0);

                (float bloomIntensity, float bloomThreshold, float vignette) = PostProcessPreset.Values(strength);

                // Volume が本当に効いているかを毎回確かめる。
                // profile / sharedProfile の取り違えでポストが丸ごと無効になり、
                // 3 枚のスクショが同一になった不具合の回帰テスト。
                Assert.That(post.Volume, Is.Not.Null, "Volume が結び付いていない");
                Assert.That(post.Volume.sharedProfile, Is.Not.Null,
                    "sharedProfile が null (profile に入れるとシーン保存で消える)");
                Assert.That(post.Volume.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.Bloom mine),
                    Is.True, "プロファイルに Bloom が保存されていない (AddObjectToAsset 漏れ)");

                var resolved = UnityEngine.Rendering.VolumeManager.instance.stack
                    .GetComponent<UnityEngine.Rendering.Universal.Bloom>();
                Assert.That(resolved.intensity.value, Is.EqualTo(bloomIntensity).Within(1e-4f),
                    "Volume スタックに反映されていない");

                Debug.Log($"[Step6] ポスト {strength}: 自前 {mine.intensity.value} -> " +
                          $"解決後 {resolved.intensity.value} (期待 {bloomIntensity})");

                Capture($"6_post_{(int)strength + 1}_{strength}_sun");
                Debug.Log($"[Step6] ポスト {strength}: bloom {bloomIntensity} / " +
                          $"threshold {bloomThreshold} / vignette {vignette}");
            }

            // 火星を向いた絵
            AimShip((mars.Host.AbsolutePosition - arrival).Normalized);
            Settle();

            foreach (PostProcessStrength strength in strengths)
            {
                post.Apply(strength);
                _root.Tick(Dt);
                yield return null;
                Capture($"6_post_{(int)strength + 1}_{strength}_mars");
            }

            // 確定値 (Medium) へ戻す。
            post.Apply(PostProcessStrength.Medium);
            Assert.That(post.Strength, Is.EqualTo(PostProcessStrength.Medium));
        }

        // ---- (d) 星空は原点シフトで動かない ----

        [UnityTest]
        public IEnumerator 星空は浮動原点のシフトで動かず回転にだけ追従する()
        {
            SpaceStation mars = _root.Model.Stations[1];
            Vec3d a = mars.AbsolutePosition + mars.PortDirection * 25.0;
            Vec3d b = _root.Model.Earth.AbsolutePosition + new Vec3d(0.0, 1.2e4, 0.0);

            // 惑星も太陽も視界に入らない向き。
            var away = new Vec3d(0.0, 0.0, 1.0);

            _root.PlaceObserver(a);
            AimShip(away);
            _root.Tick(Dt);
            yield return null;
            double[] atA = SampleSkyRow();
            Vec3d originA = _root.Origin.Origin;

            _root.PlaceObserver(b);
            AimShip(away);
            _root.Tick(Dt);
            yield return null;
            double[] atB = SampleSkyRow();
            Vec3d originB = _root.Origin.Origin;

            double shift = Vec3d.Distance(originA, originB);
            double shiftDiff = MeanAbsDiff(atA, atB);

            // 回転させたら変わること (固まっているのではない) も確かめる。
            AimShip(new Vec3d(1.0, 0.0, 0.0));
            _root.Tick(Dt);
            yield return null;
            double[] rotated = SampleSkyRow();
            double rotDiff = MeanAbsDiff(atB, rotated);

            int changedByShift = CountChanged(atA, atB);
            int changedByRotation = CountChanged(atB, rotated);

            Debug.Log($"[Step6] 原点シフト {shift:E4} units: 平均画素差 {shiftDiff:E3} / 変化した画素 {changedByShift} 個");
            Debug.Log($"[Step6] 90 度回転     : 平均画素差 {rotDiff:E3} / 変化した画素 {changedByRotation} 個");

            Assert.That(shift, Is.GreaterThan(1.0e7), "十分大きくシフトしている");

            // 星は無限遠。原点がどれだけ動いても 1 画素も変わらないのが正しい。
            Assert.That(shiftDiff, Is.EqualTo(0.0), "原点シフトで星空が動いた");
            Assert.That(changedByShift, Is.EqualTo(0), "原点シフトで変化した画素がある");

            // 回転には追従する (固まっているのではない)。
            Assert.That(changedByRotation, Is.GreaterThan(1000), "回転しても星空が変わらない");
        }

        static int CountChanged(double[] x, double[] y)
        {
            int n = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (System.Math.Abs(x[i] - y[i]) > 0.5)
                {
                    n++;
                }
            }

            return n;
        }

        static double MeanAbsDiff(double[] x, double[] y)
        {
            double sum = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                sum += System.Math.Abs(x[i] - y[i]);
            }

            return sum / x.Length;
        }

        /// <summary>Deep 段だけを描いて中央の帯を輝度で返す。星空の比較用。</summary>
        double[] SampleSkyRow()
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            RenderTexture prevDeep = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                // Deep 段だけにする。SetOverlayEnabled(false) では Nearfield が
                // 残ってしまい、ステーションの真上にいるときに星空ではなく
                // ステーションを測ってしまう (実際にそれで誤判定した)。
                _stack.ClearStack();
                _stack.Deep.targetTexture = rt;
                _stack.Deep.Render();

                RenderTexture.active = rt;
                // 星はまばらなので 1 行では信号が弱い。中央の帯を丸ごと取る。
                const int band = 256;
                var tex = new Texture2D(Width, band, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, (Height - band) / 2, Width, band), 0, 0);
                tex.Apply();

                Color32[] pixels = tex.GetPixels32();
                var row = new double[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    row[i] = Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b));
                }

                Object.DestroyImmediate(tex);
                return row;
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                _stack.Configure();
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // ---- (0) 整列角と要求の可否 ----

        [UnityTest]
        public IEnumerator 計器に整列角が出て要求の可否が分かる()
        {
            SpaceStation mars = _root.Model.Stations[1];
            _rig.SetTargetIndex(1);

            _root.PlaceObserver(mars.AbsolutePosition + mars.PortDirection * 10.0);

            // ずれた向き -> ALIGNED が付かない
            AimShip(new Vec3d(1.0, 0.0, 0.0));
            Settle();
            yield return null;

            string misaligned = _root.Instruments.LastAlignmentText;
            Debug.Log($"[Step6] ずれた向き: ALN={misaligned} / 角度 {_rig.LastAlignmentAngle:F1} 度");
            Assert.That(misaligned, Does.Not.Contain("ALIGNED"));

            // 要求は受け付けられるが、姿勢が合わないので Docking へ進まない。
            // 何が足りないのかが 1 行で出る。
            _rig.InputOverride = new FlightInput { JumpIndex = -1, DockRequest = true };
            _root.Tick(Dt);
            _rig.InputOverride = null;
            Settle();

            Debug.Log($"[Step6] 要求NG: {_rig.Docking.LastRejection} / 状態 {_rig.Docking.State}");
            Assert.That(_rig.Docking.LastRejection, Does.Contain("ポート正面"));
            Assert.That(_rig.Docking.State, Is.EqualTo(DockingState.DockRequested),
                "要求は受理されるが、姿勢が合うまで Docking へ進まない");

            // 正面へ向ける -> 許容内
            AimShip(-mars.PortDirection);
            Settle();
            yield return null;

            string aligned = _root.Instruments.LastAlignmentText;
            Debug.Log($"[Step6] 正面: ALN={aligned} / 角度 {_rig.LastAlignmentAngle:F1} 度");

            // **11-3b で "ALIGNED" の文字は外した。** 許容内かどうかは色で出しており、
            // 文字と重複していた（外したぶん HUD の字を 2.04 倍にできた）。
            // 判定は AlignmentInTolerance で見る。
            Assert.That(aligned, Does.Not.Contain("ALIGNED"));
            Assert.That(_root.Instruments.AlignmentInTolerance, Is.True);

            // 姿勢が合ったので Docking へ進み、理由も消える。
            Assert.That(_rig.Docking.State, Is.Not.EqualTo(DockingState.DockRequested));
            Assert.That(_rig.Docking.LastRejection, Is.Empty);
            Debug.Log($"[Step6] 整列後の状態: {_rig.Docking.State}");

            Capture("6_alignment_ok");
        }

        void Capture(string name)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevDeep = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Near.targetTexture = null;
                _stack.Deep.Render();

                RenderTexture.active = rt;
                var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                png.Apply();
                File.WriteAllBytes(Path.Combine(ShotDirectory, $"{name}.png"), png.EncodeToPNG());
                Object.DestroyImmediate(png);
            }
            finally
            {
                _stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
