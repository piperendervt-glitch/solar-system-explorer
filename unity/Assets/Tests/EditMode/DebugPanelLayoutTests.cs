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
        const int HeaderLines = 3;
        const int BodyLines = 4; // 見出し + 天体 3 件

        /// <summary>本番と同じ項目数を使う。ここで数を二重定義しない。</summary>
        static int RealItemCount()
        {
            DebugPanelModel m = DebugPanelModel.Create(
                new[] { "Sun", "Earth", "Mars" }, 5.0, 1.15, 0.6, 1.5e-3);
            return m.Items.Count;
        }

        [Test]
        public void 項目数は23件()
        {
            Assert.That(RealItemCount(), Is.EqualTo(23));
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void 実解像度で全項目が画面内に収まる(int w, int h)
        {
            int items = RealItemCount();
            DebugPanelLayout l = DebugPanelLayoutSolver.Solve(
                w, h, HeaderLines, items, BodyLines, 620f, 0);

            Assert.That(l.Windowed, Is.False, "全部入るはずなのに窓表示になっている");
            Assert.That(l.ItemCount, Is.EqualTo(items), "項目が欠けている");
            Assert.That(l.Width + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(w), "横に見切れる");
            Assert.That(l.Height + DebugPanelLayoutSolver.Margin * 2f,
                        Is.LessThanOrEqualTo(h), "縦に見切れる");
            Assert.That(l.FontSize,
                        Is.InRange(DebugPanelLayoutSolver.MinFontSize,
                                   DebugPanelLayoutSolver.MaxFontSize));
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
