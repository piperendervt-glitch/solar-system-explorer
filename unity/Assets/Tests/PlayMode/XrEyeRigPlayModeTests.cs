using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// **頭姿勢の段配布 (Step 12-2)。**
    ///
    /// XR はトラッキング位置も眼間も**メートルで**カメラに書き込む。段のスケールを
    /// 知らないので、配布しないと外 3 段では 1000 倍の量が効いてしまう
    /// （12-1b の実測: 眼間が 4 段とも 6.24e-2 units = 外 3 段では 62.4 m 相当）。
    ///
    /// `XrEyeRig` は各段のカメラを、段のスケールを持つ親の下へ移す。
    /// **期待値は 12-1 の `StereoGeometry.CameraLocal` から出す**（新しい式を書かない）。
    ///
    /// **判定は位置で行う。** 角度で判定すると Δθ = baseline/D の小角近似が入り、
    /// 0.3 m のとき計器では 10 % 外れる（12-1）。位置なら近似は入らない。
    /// </summary>
    public sealed class XrEyeRigPlayModeTests
    {
        /// <summary>頭の移動 [m]。**2 点ではなく 4 点で単調性まで見る。**</summary>
        static readonly float[] HeadMeters = { 0f, 0.1f, 0.2f, 0.3f };

        CameraStackController _stack;
        IReadOnlyList<XrEyeRig.Anchor> _anchors;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _stack = Object.FindAnyObjectByType<CameraStackController>();
            Assert.That(_stack, Is.Not.Null, "カメラスタックが無い");

            _anchors = XrEyeRig.Build(_stack);
            Assert.That(_anchors.Count, Is.EqualTo(4), "4 段ぶんの取り付けができていない");
        }

        [Test]
        public void 段ごとのスケールが親に入る()
        {
            foreach (XrEyeRig.Anchor anchor in _anchors)
            {
                double expected = anchor.Stage == "Cockpit"
                    ? StereoGeometry.CockpitStageScale
                    : StereoGeometry.WorldStageScale;

                Assert.That(anchor.Scale, Is.EqualTo(expected), anchor.Stage);

                Vector3 scale = anchor.Transform.localScale;
                Assert.That(scale.x, Is.EqualTo((float)expected).Within(1e-9f), anchor.Stage + " x");
                Assert.That(scale.y, Is.EqualTo(scale.x), anchor.Stage + " 一様でない");
                Assert.That(scale.z, Is.EqualTo(scale.x), anchor.Stage + " 一様でない");
            }
        }

        [Test]
        public void 頭が原点ならカメラの位置は配布前と同じ()
        {
            // **配布そのものが絵を動かしてはいけない。**
            // 頭が原点のとき、カメラのワールド姿勢は取り付け前と変わらないこと。
            XrEyeRig.SimulateHead(_anchors, Vector3.zero);

            foreach (XrEyeRig.Anchor anchor in _anchors)
            {
                Vector3 now = XrEyeRig.PositionInParent(anchor);
                Assert.That((now - anchor.RestPosition).magnitude, Is.LessThan(1e-6f),
                            anchor.Stage + " が動いている");
            }
        }

        [Test]
        public void 取り付けは冪等()
        {
            List<XrEyeRig.Anchor> again = XrEyeRig.Build(_stack);

            Assert.That(again.Count, Is.EqualTo(_anchors.Count));
            for (int i = 0; i < again.Count; i++)
            {
                Assert.That(again[i].Transform, Is.SameAs(_anchors[i].Transform),
                            again[i].Stage + " の取り付けが作り直されている");
            }
        }

        [Test]
        public void 頭移動の数表()
        {
            // 期待値（親から見た移動量 [units]）:
            //
            //   頭 [m]   コックピット (s=1.0)   外 3 段 (s=0.001)
            //   0.0      0.0                    0.0
            //   0.1      0.1                    1e-4
            //   0.2      0.2                    2e-4
            //   0.3      0.3                    3e-4
            foreach (XrEyeRig.Anchor anchor in _anchors)
            {
                var moved = new List<float>();

                foreach (float meters in HeadMeters)
                {
                    XrEyeRig.SimulateHead(_anchors, new Vector3(meters, 0f, 0f));

                    Vector3 delta = XrEyeRig.PositionInParent(anchor) - anchor.RestPosition;

                    StereoGeometry.StagePose expected = StereoGeometry.CameraLocal(
                        new StereoGeometry.HeadPose(
                            new Vec3d(meters, 0.0, 0.0),
                            new Vec3d(0.0, 0.0, 1.0),
                            new Vec3d(0.0, 1.0, 0.0)),
                        anchor.Scale);

                    string label = $"{anchor.Stage} / 頭 {meters} m";
                    Assert.That(delta.x, Is.EqualTo((float)expected.Position.X).Within(1e-7f), label);
                    Assert.That(delta.y, Is.EqualTo(0f).Within(1e-7f), label + " y");
                    Assert.That(delta.z, Is.EqualTo(0f).Within(1e-7f), label + " z");

                    moved.Add(delta.x);
                }

                // **単調性。** 2 標本では「向きが合っている」ことしか言えない。
                for (int i = 1; i < moved.Count; i++)
                {
                    Assert.That(moved[i], Is.GreaterThan(moved[i - 1]),
                                $"{anchor.Stage}: 単調に増えていない ({moved[i - 1]} -> {moved[i]})");
                }

                // 等間隔（0.1 m 刻みなので差も一定）。
                float step = moved[1] - moved[0];
                for (int i = 2; i < moved.Count; i++)
                {
                    Assert.That(moved[i] - moved[i - 1], Is.EqualTo(step).Within(step * 1e-3f),
                                anchor.Stage + ": 刻みが一定でない");
                }
            }

            XrEyeRig.SimulateHead(_anchors, Vector3.zero);
        }

        [Test]
        public void 外3段とコックピット段で1000倍違う()
        {
            // **同じ頭の動きが、段によって 1000 倍違う量になる。**
            // これが「見かけの角度が段をまたいで一致する」ことの正体。
            XrEyeRig.SimulateHead(_anchors, new Vector3(0.3f, 0f, 0f));

            float cockpit = 0f;
            var outers = new List<float>();

            foreach (XrEyeRig.Anchor anchor in _anchors)
            {
                float delta = (XrEyeRig.PositionInParent(anchor) - anchor.RestPosition).x;
                if (anchor.Stage == "Cockpit")
                {
                    cockpit = delta;
                }
                else
                {
                    outers.Add(delta);
                }
            }

            Assert.That(cockpit, Is.EqualTo(0.3f).Within(1e-6f), "コックピット段");
            foreach (float outer in outers)
            {
                Assert.That(outer, Is.EqualTo(3e-4f).Within(1e-9f), "外 3 段");
                Assert.That(cockpit / outer, Is.EqualTo(1000f).Within(1f), "比が 1000 でない");
            }

            XrEyeRig.SimulateHead(_anchors, Vector3.zero);
        }
    }
}
