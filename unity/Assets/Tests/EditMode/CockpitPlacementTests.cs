using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// コックピットの配置・スケール・視点 (Step 11-2)。
    ///
    /// **箱と実アセットを同じ手順で組めることがこの Step の主張**なので、
    /// テストも両方の定義で同じ組み立てを通す。
    /// </summary>
    public sealed class CockpitPlacementTests
    {
        Transform _ship;

        [SetUp]
        public void SetUp()
        {
            _ship = new GameObject("TestShip").transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ship != null)
            {
                Object.DestroyImmediate(_ship.gameObject);
            }
        }

        static GameObject HiRezPrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(CockpitDefinition.HiRezSample.PrefabGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void RequireImported()
        {
            if (HiRezPrefab() == null)
            {
                Assert.Inconclusive("まだ取り込まれていない（clone 直後は正常）。"
                                    + "run_unity.ps1 -Method SolarSetup.ImportCockpit で取り込む。");
            }
        }

        // ---- 定義 (11-2a) ----

        [Test]
        public void 箱は倍率1で目が原点()
        {
            Assert.That(CockpitDefinition.Box.Scale, Is.EqualTo(1.0));
            Assert.That(CockpitDefinition.Box.EyeLocal, Is.Null,
                        "箱は原点が目の位置。定義側に値を持たない");
        }

        [Test]
        public void メートル単位のアセットは倍率1のまま()
        {
            // コックピットは 1000 倍の描画空間にあり、そこでは 1 m = 1 unit。
            // 実寸で作られたアセットは倍率を掛けずに置ける (11-2c)。
            Assert.That(CockpitDefinition.HiRezSample.Scale, Is.EqualTo(1.0));
        }

        [Test]
        public void 箱の機首はZプラス()
        {
            Assert.That(CockpitDefinition.Box.EyeForward.Z, Is.EqualTo(1.0));
        }

        [Test]
        public void アセットの前と上が実測どおり()
        {
            // **実測 (11-2c)。**
            //   前: 座席 (0, -0.074, -1.436) から見て操縦桿 (0, 0.060, -0.887) と
            //       計器の画面 (±0.205, 0.148, -0.422) が +Z 側に並ぶ
            //   上: 窓 (0, 0.429, -1.515) が座席の 0.50 m 上。操縦桿も台より握りが上
            Assert.That(CockpitDefinition.HiRezSample.EyeForward.Z, Is.EqualTo(1.0));
            Assert.That(CockpitDefinition.HiRezSample.EyeUp.Y, Is.EqualTo(1.0));
        }

        [Test]
        public void 前と上が反平行でも姿勢が決まる()
        {
            // **実機で踏んだ不具合の再発防止。**
            // `FromToRotation(前方, Z+)` は前方が Z+ と反平行のとき回転軸が一意に
            // 決まらず、Unity が X 軸を選ぶと**前後と上下が同時に反転する。**
            // 前方と上方の 2 軸で決めれば縮退しない。
            var reversed = new CockpitDefinition(
                "reversed-for-test", null, null,
                eyeForward: new Vec3d(0.0, 0.0, -1.0),
                eyeUp: new Vec3d(0.0, 1.0, 0.0));

            CockpitBuilder.Result built = CockpitBuilder.Build(_ship, 9, reversed);

            // 上が保たれたまま、前だけが反転していること。
            Assert.That(Vector3.Dot(built.Identity.transform.up, Vector3.up),
                        Is.GreaterThan(0.99f), "上下が反転している");
        }

        [Test]
        public void 機首は船の前方へ回して合わせる()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            // **回すのはプレハブ。カメラは船の前を向いたまま。**
            // カメラを回すと船の後ろを向いてしまい、外の景色が見えない。
            Transform instance = hirez.Identity.transform.GetChild(0);
            Vec3d f = CockpitDefinition.HiRezSample.EyeForward;
            Vector3 assetForward = instance.localRotation
                                   * new Vector3((float)f.X, (float)f.Y, (float)f.Z);

            Assert.That(Vector3.Dot(assetForward.normalized, Vector3.forward),
                        Is.EqualTo(1.0f).Within(1e-4f),
                        "アセットの機首が船の前方を向いていない");

            Assert.That(Quaternion.Angle(hirez.CockpitCamera.transform.localRotation,
                                         Quaternion.identity),
                        Is.LessThan(1e-3f), "カメラが回っている");
        }

        // ---- 寸法と目の初期値 (11-2b) ----

        [Test]
        public void 目の初期値がプレハブの寸法の中にある()
        {
            RequireImported();

            Bounds? bounds = CockpitBoundsSolver.LocalBounds(HiRezPrefab());
            Assert.That(bounds, Is.Not.Null, "レンダラーが無い");

            Vec3d eye = CockpitDefinition.HiRezSample.EyeLocal
                        ?? CockpitBoundsSolver.SuggestEye(HiRezPrefab(), bounds.Value, CockpitDefinition.HiRezSample);

            var point = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
            Assert.That(bounds.Value.Contains(point), Is.True,
                        $"目の位置 {point} がコックピットの外にある: {bounds.Value}");
        }

        [Test]
        public void 寸法はメートルの桁に収まっている()
        {
            RequireImported();

            Bounds b = CockpitBoundsSolver.LocalBounds(HiRezPrefab()).Value;

            // **人が乗る大きさかどうかの粗い検査。** 桁が違えば単位を取り違えている。
            // 実測: 1.608 x 1.631 x 5.766 m。
            Assert.That(b.size.x, Is.InRange(0.5f, 20f));
            Assert.That(b.size.y, Is.InRange(0.5f, 20f));
            Assert.That(b.size.z, Is.InRange(0.5f, 20f));

            Debug.Log($"  [Step11-2b] コックピットの寸法 {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3} m"
                      + $" / 目の初期値 {CockpitBoundsSolver.SuggestEye(HiRezPrefab(), b, CockpitDefinition.HiRezSample)}");
        }

        // ---- 組み立て (11-2a / 11-2d) ----

        [Test]
        public void 箱でも実アセットでも同じ手順で組める()
        {
            RequireImported();

            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);
            AssertBuilt(box, CockpitDefinition.BoxId);

            var ship2 = new GameObject("TestShip2").transform;
            try
            {
                CockpitBuilder.Result hirez =
                    CockpitBuilder.Build(ship2, 9, CockpitDefinition.HiRezSample);
                AssertBuilt(hirez, CockpitDefinition.HiRezSampleId);

                // **差し替わっていることを直接見る。** レンダラー数ではなく Id で。
                Assert.That(hirez.Identity.FellBackToBox, Is.False);

                int boxRenderers = box.Identity.GetComponentsInChildren<Renderer>(true).Length;
                int hirezRenderers = hirez.Identity.GetComponentsInChildren<Renderer>(true).Length;
                Assert.That(hirezRenderers, Is.GreaterThan(boxRenderers));

                Debug.Log($"  [Step11-2a] レンダラー数 箱 {boxRenderers} / hirez {hirezRenderers}");
            }
            finally
            {
                Object.DestroyImmediate(ship2.gameObject);
            }
        }

        static void AssertBuilt(CockpitBuilder.Result result, string expectedId)
        {
            Assert.That(result.Identity, Is.Not.Null, "識別子が付いていない");
            Assert.That(result.Identity.DefinitionId, Is.EqualTo(expectedId));
            Assert.That(result.CockpitCamera, Is.Not.Null, "カメラが無い");
            Assert.That(result.Panel, Is.Not.Null, "計器が無い");
            Assert.That(result.ShakeRig, Is.Not.Null, "微振動の親が無い");
            Assert.That(result.Metrics, Is.Not.Null, "窓の計測器が無い");
        }

        [Test]
        public void 窓はマテリアル名で引く()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            // **子の順番に依存しない。** 実測ではガラスのレンダラーは 1 枚
            // (Cockpit3Grey_Glass)。並べ替えても同じ結果になることを、
            // 「名前で引いている」ことをもって示す。
            Assert.That(hirez.Metrics.Glass.Count, Is.EqualTo(1),
                        "ガラスのレンダラー数が変わっている");

            Renderer glass = hirez.Metrics.Glass[0];
            Assert.That(glass.sharedMaterials.Any(
                            m => m != null && m.name.Contains(CockpitMetrics.GlassMaterialKeyword)),
                        Is.True);
        }

        [Test]
        public void 箱には窓が無いので測れないと出る()
        {
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);

            Assert.That(box.Metrics.Glass.Count, Is.EqualTo(0));
            Assert.That(box.Metrics.Describe(), Does.Contain("---"),
                        "測れないのに数字を出している");
        }

        [Test]
        public void プレハブはリンクのまま置く()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            // **`Object.Instantiate` で置くと階層がシーンへ展開され、アセットを持たない
            // クローンでも中身が復元されてしまう。** EULA が再配布を禁じている。
            Transform instance = hirez.Identity.transform.GetChild(0);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(instance.gameObject), Is.True,
                        "プレハブのリンクが切れている（シーンへ展開されている）");
        }

        [Test]
        public void レイヤは子まで行き渡る()
        {
            RequireImported();

            const int layer = 9;
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, layer, CockpitDefinition.HiRezSample);

            // **Overlay のカメラは culling mask でこの層だけを描く。**
            // 1 つでも既定層のままだと、その部品だけ他の段に描かれる。
            Renderer[] renderers = hirez.Identity.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));

            string[] strays = renderers
                .Where(r => r.gameObject.layer != layer)
                .Select(r => r.name)
                .ToArray();

            Assert.That(strays, Is.Empty, "層が違う部品がある: " + string.Join(", ", strays));
        }

        // ---- 姿勢 (11-2c)。**実機でしか気づけなかったので数値で縛る。** ----

        [Test]
        public void 箱もアセットも上が上を向く()
        {
            // **上下の反転は実機で見るまで分からなかった。**
            // 船が無回転のとき、コックピットの上はワールドの +Y 側にある。
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);
            AssertUpright(box, "box");

            if (HiRezPrefab() == null)
            {
                Assert.Inconclusive("hirez は未取り込み（箱の分は確認済み）");
            }

            var ship2 = new GameObject("TestShip2").transform;
            try
            {
                AssertUpright(CockpitBuilder.Build(ship2, 9, CockpitDefinition.HiRezSample),
                              "hirez-sample");
            }
            finally
            {
                Object.DestroyImmediate(ship2.gameObject);
            }
        }

        static void AssertUpright(CockpitBuilder.Result result, string label)
        {
            float dot = Vector3.Dot(result.Identity.transform.up, Vector3.up);
            Assert.That(dot, Is.GreaterThan(0.0f),
                        $"{label}: コックピットの上が下を向いている (dot {dot:F3})");
        }

        [Test]
        public void 箱もアセットも計器が目の前にある()
        {
            // **前後の反転は実機で見るまで分からなかった。**
            // 反転を捕まえるには「目に対して前後が非対称なもの」を見る必要がある。
            //
            // **窓では捕まらない。** Hi-Rez のキャノピーは操縦者を包む形で、
            // 実測では中心が目の 0.079 m **後ろ**、前へ 1.22 m / 後ろへ 1.38 m 広がる。
            // ヨー 180 度で反転しても「窓が前にも広がっている」は成り立ってしまう。
            //
            // **計器は前にしか無い**ので、反転すれば必ず後ろへ回る。
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);
            Transform panel = FindByName(box.Identity.transform, "InstrumentSurface");
            Assert.That(panel, Is.Not.Null, "箱に計器面が無い");
            Assert.That(LocalZ(box.CockpitCamera, panel.position), Is.GreaterThan(0f),
                        "box: 計器面が目より後ろにある");

            if (HiRezPrefab() == null)
            {
                Assert.Inconclusive("hirez は未取り込み（箱の分は確認済み）");
            }

            var ship2 = new GameObject("TestShip2").transform;
            try
            {
                CockpitBuilder.Result hirez =
                    CockpitBuilder.Build(ship2, 9, CockpitDefinition.HiRezSample);

                Renderer[] screens = hirez.Identity.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name.Contains("Screen-"))
                    .ToArray();

                Assert.That(screens.Length, Is.GreaterThan(0), "計器の画面が見つからない");

                foreach (Renderer screen in screens)
                {
                    float z = LocalZ(hirez.CockpitCamera, screen.bounds.center);
                    Assert.That(z, Is.GreaterThan(0f),
                                $"hirez-sample: {screen.name} が目より後ろにある (z {z:F3})");
                }
            }
            finally
            {
                Object.DestroyImmediate(ship2.gameObject);
            }
        }

        [Test]
        public void 窓が目より前へも広がっている()
        {
            RequireImported();

            // 目がキャノピーの中にあること。**前後の反転は捕まえられない**
            // （上のテストの担当）が、目が窓の外や後ろへ出た場合はここで落ちる。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            Assert.That(hirez.Metrics.Glass.Count, Is.GreaterThan(0), "窓が無い");

            Bounds glass = hirez.Metrics.Glass[0].bounds;
            Transform cam = hirez.CockpitCamera.transform;

            float front = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? glass.min.x : glass.max.x,
                    (i & 2) == 0 ? glass.min.y : glass.max.y,
                    (i & 4) == 0 ? glass.min.z : glass.max.z);

                front = Mathf.Max(front, cam.InverseTransformPoint(corner).z);
            }

            Assert.That(front, Is.GreaterThan(0f), $"窓が目より前に無い (前端 z {front:F3})");
        }

        static float LocalZ(Camera camera, Vector3 worldPoint)
            => camera.transform.InverseTransformPoint(worldPoint).z;

        static Transform FindByName(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
