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
        // 見出し 3 行 + 窓の投影面積比 1 行 (Step 11-2b)。
        const int HeaderLines = 4;
        const int BodyLines = 4; // 見出し + 天体 3 件

        /// <summary>本番と同じ項目数を使う。ここで数を二重定義しない。</summary>
        static int RealItemCount()
        {
            DebugPanelModel m = DebugPanelModel.Create(
                new[] { "Sun", "Earth", "Mars" }, 5.0, 1.15, 0.6, 1.5e-3, 6.0, 2.5, 1.5, 6.0, 0.4, 0.8, 1.05, 0.6, 3.0, 0.10, 0.50, 0.75, 0.35, 0.0, 0.0, 0.0,
                new Vec3d(-1.0, -1.0, -1.0), new Vec3d(1.0, 1.0, 1.0), 60.0);
            return m.Items.Count;
        }

        [Test]
        public void 項目数は53件()
        {
            // 43 - 音量 4 + 画面の発光 1 + 割り当て 1 = 41 (Step 11-3)。
            // 割り当ての 3 択は 11-3c で外した（案 A に確定）ので 40。
            // さらに**切り分けの道具 4 つ**（テスト柄 / RT 直接表示 / RT 表示の面 /
            // 計器の向き）で 44。11-4 で補助光の 2 項目（ON/OFF・強さ）を足して 46。
            // **13-3 コミット2 でステーションの倍率を 1 つ足して 47。**
            // **13-3b で判定ビューの 5 項目（Scale / 視点 / 目印 3）を足して 52。**
            // **13-3b の追補で「判定 距離」を 1 つ足して 53。**
            //
            // **1080p の段: フォント 14 は 43 項目まで / 13 は 45 まで / 12 は 48 まで。**
            // 道具 3 つを外せば 43 = フォント 14 に戻る（判断は人間）。
            Assert.That(RealItemCount(), Is.EqualTo(53));
        }

        [Test]
        public void フルHDのフォントは10()
        {
            // **項目を増やすとフォントが落ちる。** 1080p の利用可能高 1056 px に対し、
            // 行数 = 見出し 4 + 項目 N + 空行 1 + 天体 4。フォント 14 (行高 20) で
            // 収まるのは N <= 43 まで、13 (行高 19) なら N <= 45。
            //
            // **いまは 53 項目なので 10。**
            // 1080p の利用可能高 1056 px / 行高 = フォント + 6 / 固定 9 行
            //   フォント 11 (行高 17): (9 + 53) * 17 + 16 = 1070 > 1056  入らない
            //   フォント 10 (行高 16): (9 + 53) * 16 + 16 = 1008 <= 1056 入る
            // **10 は MinFontSize。次に項目を足すと窓表示に落ちる。**
            // **期待値を書き換えたのは意図した変更。**
            // 道具を外して 41 に戻れば 14 に戻る。**期待値を書き換えたのは意図した変更。**
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, RealItemCount(), BodyLines, 620f, 0);

            Assert.That(l.FontSize, Is.EqualTo(10), "フォントの段が想定と違う");
            Assert.That(l.Windowed, Is.False, "窓表示に落ちている");
        }

        [Test]
        public void フルHDなら全項目が一度に収まる()
        {
            int items = RealItemCount();
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                1920, 1080, HeaderLines, items, BodyLines, 620f, 0);

            Assert.That(l.Windowed, Is.False, "1080p で窓表示に落ちている");
            Assert.That(l.ItemCount, Is.EqualTo(items), "項目が欠けている");
        }

        /// <summary>
        /// **720p では窓表示に落ちてよい (Step 10-2 で 38 項目になった)。**
        /// 見出し 3 + 項目 38 + 空行 1 + 天体表 4 = 46 行あり、
        /// 最小フォント (行高 16) でも 736 + 余白 16 = 752 px で、
        /// 使える 696 px に入らない。**見切れないことだけを縛る。**
        /// </summary>
        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void 実解像度で見切れない(int w, int h)
        {
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                w, h, HeaderLines, RealItemCount(), BodyLines, 620f, 0);

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
            int items = RealItemCount();
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
                w, h, HeaderLines, RealItemCount(), BodyLines, 99999f, 0);

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
