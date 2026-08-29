using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// F4 パネルが画面内に収まることを数値で縛る (Step 8-0b 修正)。
    ///
    /// **OnGUI は batchmode で描けない** (CLAUDE.md 0-B) ので、
    /// 「見切れていない」ことを自動で確かめる手はこれしかない。
    /// 実機の見え方は exe のスクショで別途見る。
    /// </summary>
    public sealed class DebugPanelLayoutTests
    {
        // 見出し 3 行 + **分類の行 1 行 (13-3b 追補)** + 窓の投影面積比 1 行。
        const int HeaderLines = 5;
        const int BodyLines = 4; // 見出し + 天体 3 件

        static DebugPanelModel Model()
            => DebugPanelModel.Create(
                new[] { "Sun", "Earth", "Mars" }, 5.0, 1.15, 0.6, 1.5e-3, 6.0, 2.5, 1.5, 6.0, 0.4, 0.8, 1.05, 0.6, 3.0, 0.10, 0.50, 0.75, 0.35, 0.0, 0.0, 0.0,
                new Vec3d(-1.0, -1.0, -1.0), new Vec3d(1.0, 1.0, 1.0), 60.0);

        /// <summary>本番と同じ項目数を使う。ここで数を二重定義しない。</summary>
        static int RealItemCount() => Model().Items.Count;

        /// <summary>
        /// **レイアウトを決めるのは分類ごとの最大件数 (13-3b 追補)。**
        /// パネルはカーソルがいる分類だけを描くので、全項目数では決まらない。
        /// </summary>
        static int LargestCategoryCount()
        {
            DebugPanelModel m = Model();
            int max = 0;
            foreach (string c in DebugPanelModel.CategoryOrder)
            {
                if (m.CountIn(c) > max)
                {
                    max = m.CountIn(c);
                }
            }

            return max;
        }

        [Test]
        public void 項目数は55件()
        {
            // 43 - 音量 4 + 画面の発光 1 + 割り当て 1 = 41 (Step 11-3)。
            // 割り当ての 3 択は 11-3c で外した（案 A に確定）ので 40。
            // さらに**切り分けの道具 4 つ**で 44。11-4 で補助光の 2 項目で 46。
            // 13-3 コミット2 でステーションの倍率を 1 つ足して 47。
            // 13-3b で判定ビューの 5 項目を足して 52。
            // 13-3b の追補で「判定 距離」を 1 つ足して 53。
            // 13-3b でアレイのロールを 1 つ足して 54。
            // 13-3b で要求可能距離を 1 つ足して 55。
            Assert.That(RealItemCount(), Is.EqualTo(55));
        }

        [Test]
        public void 分類ごとの件数()
        {
            DebugPanelModel m = Model();

            //   分類                 件数   内訳
            //   段と天体             20     1 段だけ表示 1 + カメラ段 4
            //                               + 天体 3 x 3 + show.* 6
            //   見た目               13     惑星・太陽・フレア 10 + bloom 3
            //   コックピット         13     画面 4 + 補助光 2 + 計器の向き 1
            //                               + 目 3 + 画角 1 + 音 1 + 微振動 1
            //   ステーションと判定    9     倍率 1 + ロール 1 + 要求可能距離 1 + 判定 6
            Assert.That(m.CountIn("段と天体"), Is.EqualTo(20));
            Assert.That(m.CountIn("見た目"), Is.EqualTo(13));
            Assert.That(m.CountIn("コックピット"), Is.EqualTo(13));
            Assert.That(m.CountIn("ステーションと判定"), Is.EqualTo(9));

            Assert.That(LargestCategoryCount(), Is.EqualTo(20));
        }

        [Test]
        public void 全項目がどれかの分類に入っている()
        {
            // **到達できない項目が出ないことの本体。**
            DebugPanelModel m = Model();

            int sum = 0;
            foreach (string c in DebugPanelModel.CategoryOrder)
            {
                sum += m.CountIn(c);
            }

            Assert.That(sum, Is.EqualTo(m.Items.Count), "分類から漏れた項目がある");

            foreach (DebugItem item in m.Items)
            {
                Assert.That(DebugPanelModel.CategoryOrder, Does.Contain(item.Category),
                            "分類が一覧に無い: " + item.Id);
            }
        }

        [Test]
        public void カーソルを回すと全項目に到達する()
        {
            // **キーは増やしていない。** 上下だけで分類の境目を跨ぐので、
            // カーソルを項目数だけ進めれば全項目を 1 回ずつ通る。
            DebugPanelModel m = Model();
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < m.Items.Count; i++)
            {
                seen.Add(m.Current.Id);

                // 描かれている分類にカーソルがいること。
                Assert.That(m.VisibleItems, Does.Contain(m.Current),
                            "カーソルの項目が描かれる側に無い: " + m.Current.Id);
                Assert.That(m.CursorInCategory,
                            Is.InRange(0, m.VisibleItems.Count - 1));

                m.MoveCursor(1);
            }

            Assert.That(seen.Count, Is.EqualTo(m.Items.Count), "到達できない項目がある");
            Assert.That(m.Cursor, Is.EqualTo(0), "1 周して戻っていない");
        }

        [Test]
        public void 分類の無いidは例外()
        {
            // **項目を足したときに黙って落ちないこと。**
            Assert.Throws<System.ArgumentException>(
                () => DebugPanelModel.CategoryOf("num.そんな項目は無い"));
        }

        [Test]
        public void フルHDのフォントは14()
        {
            // **分類ごとに 1 画面にしたのでフォントが戻った (13-3b 追補)。**
            //
            // 1080p の利用可能高 = 1080 - Margin 12 * 2 = 1056 px
            // 行高 = フォント + 6 / 固定行 = 見出し 5 + 空行 1 + 天体 4 = 10
            // 高さ = Padding 8 * 2 + (固定 10 + 項目 N) * 行高
            //
            //   最大の分類 N = 20
            //   フォント 14 (行高 20): 16 + (10 + 20) * 20 = 616 <= 1056  入る
            //
            // **分類にする前は 53 項目でフォント 10（MinFontSize）だった。**
            //   16 + (9 + 53) * 16 = 1008（固定 9 行 = 見出し 4 + 空行 1 + 天体 4）
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, LargestCategoryCount(), BodyLines, 620f, 0);

            Assert.That(l.FontSize, Is.EqualTo(DebugPanelLayoutSolver.MaxFontSize),
                        "フォントの段が想定と違う");
            Assert.That(l.Windowed, Is.False, "窓表示に落ちている");
        }

        [Test]
        public void フルHDで分類に入れられる項目数の余裕()
        {
            // **どこまで増やせるかを数で残す。**
            //   16 + (10 + N) * 20 <= 1056  ->  N <= 42.8  ->  N <= 42  フォント 14
            //   16 + (10 + N) * 16 <= 1056  ->  N <= 55    フォント 10（窓表示にならない上限）
            for (int n = 1; n <= 42; n++)
            {
                DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                    1920, 1080, HeaderLines, n, BodyLines, 620f, 0);
                Assert.That(l.FontSize, Is.EqualTo(DebugPanelLayoutSolver.MaxFontSize),
                            "N = " + n + " でフォント 14 から落ちた");
            }

            DebugPanelLayout over = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, 43, BodyLines, 620f, 0);
            Assert.That(over.FontSize, Is.LessThan(DebugPanelLayoutSolver.MaxFontSize),
                        "N = 43 でもフォント 14 のまま（境目の記録が古い）");

            DebugPanelLayout edge = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, 55, BodyLines, 620f, 0);
            Assert.That(edge.Windowed, Is.False, "N = 55 で窓表示に落ちた");

            DebugPanelLayout past = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, 56, BodyLines, 620f, 0);
            Assert.That(past.Windowed, Is.True, "N = 56 でも窓表示にならない（境目の記録が古い）");
        }
        [Test]
        public void フルHDなら全項目が一度に収まる()
        {
            int items = LargestCategoryCount();
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, items, BodyLines, 620f, 0);

            Assert.That(l.Windowed, Is.False, "1080p で窓表示に落ちている");
            Assert.That(l.ItemCount, Is.EqualTo(items), "項目が欠けている");
        }

        /// <summary>
        /// **見切れないことだけを縛る。**
        /// 720p の利用可能高 = 720 - 12 * 2 = 696 px。
        /// 最大の分類 20 なら 16 + (10 + 20) * 16 = 496 <= 696 で、
        /// **分類にしたことで 720p でも窓表示に落ちなくなった**（以前は落ちていた）。
        /// </summary>
        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void 実解像度で見切れない(int w, int h)
        {
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                w, h, HeaderLines, LargestCategoryCount(), BodyLines, 620f, 0);

            Assert.That(l.Width + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(w), "横に見切れる");
            Assert.That(l.Height + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(h), "縦に見切れる");
            Assert.That(l.FontSize,
                        Is.InRange(DebugPanelLayoutSolver.MinFontSize,
                                   DebugPanelLayoutSolver.MaxFontSize));
            Assert.That(l.ItemCount, Is.GreaterThan(0), "項目が 1 つも出ない");
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void 窓表示でもカーソルは必ず見える(int w, int h)
        {
            int items = LargestCategoryCount();
            for (int cursor = 0; cursor < items; cursor++)
            {
                DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                    w, h, HeaderLines, items, BodyLines, 620f, cursor);

                Assert.That(cursor, Is.InRange(l.FirstItem, l.FirstItem + l.ItemCount - 1),
                            $"{w}x{h} でカーソル {cursor} が範囲の外");
            }
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void 内容が画面より広くても幅は画面内に収まる(int w, int h)
        {
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                w, h, HeaderLines, LargestCategoryCount(), BodyLines, 99999f, 0);

            Assert.That(l.Width + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(w), "幅がクランプされていない");
        }

        [Test]
        public void 縦に入らないときはカーソル周辺だけを出す()
        {
            // 200 項目は最小フォントでも 1080 に入らない。
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, 200, BodyLines, 620f, 120);

            Assert.That(l.Windowed, Is.True, "窓表示になっていない");
            Assert.That(l.FontSize, Is.EqualTo(DebugPanelLayoutSolver.MinFontSize));
            Assert.That(l.ItemCount, Is.LessThan(200));
            Assert.That(l.Height + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(1080), "窓表示でも縦に見切れる");
            Assert.That(120, Is.InRange(l.FirstItem, l.FirstItem + l.ItemCount - 1),
                        "カーソルが表示範囲の外にある");
        }

        [TestCase(0)]
        [TestCase(199)]
        public void 窓表示でも端のカーソルが範囲に入る(int cursor)
        {
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1280, 720, HeaderLines, 200, BodyLines, 620f, cursor);

            Assert.That(l.FirstItem, Is.GreaterThanOrEqualTo(0));
            Assert.That(l.FirstItem + l.ItemCount, Is.LessThanOrEqualTo(200));
            Assert.That(cursor, Is.InRange(l.FirstItem, l.FirstItem + l.ItemCount - 1));
        }

        [Test]
        public void 行高はフォントに追従する()
        {
            Assert.That(DebugPanelLayoutSolver.LineHeightFor(14), Is.EqualTo(20f));
            Assert.That(DebugPanelLayoutSolver.LineHeightFor(10), Is.EqualTo(16f));
        }
    }
}
