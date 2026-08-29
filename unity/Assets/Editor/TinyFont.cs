using System.Collections.Generic;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **画像に文字を焼くための 5x7 ビットマップフォント (Step 13-3)。**
    ///
    /// ■ なぜ自前で持つか
    /// 目盛と軸のラベルは**画像そのものに入っていないと意味がない**
    /// （画像だけ見て何メートルか分かること、が要件）。
    /// OnGUI は `Camera.Render()` → RenderTexture の経路に写らない（§0-B）ので、
    /// テキストは CPU で画素に書くしかない。
    ///
    /// **持っているのは目盛に要る字だけ。** 足りない字は空白になる（例外にしない。
    /// ラベルが 1 文字欠けても絵は読めるが、例外で撮影が止まると絵が無くなる）。
    /// </summary>
    public static class TinyFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>字と字の間 [px]（倍率 1 のとき）。</summary>
        public const int Advance = 6;

        static readonly Dictionary<char, string[]> Glyphs = Build();

        public static int MeasureWidth(string text, int scale)
            => text == null ? 0 : text.Length * Advance * scale;

        public static int MeasureHeight(int scale) => GlyphHeight * scale;

        /// <summary>
        /// `pixels`（幅 `width`）へ文字列を描く。原点は**左下**。
        /// はみ出す画素は捨てる（配列外で落とさない）。
        /// </summary>
        public static void Draw(Color32[] pixels, int width, int height,
                                int x, int y, string text, int scale, Color32 color)
        {
            if (string.IsNullOrEmpty(text) || scale < 1)
            {
                return;
            }

            int cursor = x;
            foreach (char c in text)
            {
                DrawGlyph(pixels, width, height, cursor, y, c, scale, color);
                cursor += Advance * scale;
            }
        }

        static void DrawGlyph(Color32[] pixels, int width, int height,
                              int x, int y, char c, int scale, Color32 color)
        {
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out string[] rows)
                && !Glyphs.TryGetValue(c, out rows))
            {
                return;
            }

            for (int row = 0; row < GlyphHeight; row++)
            {
                string line = rows[row];

                // rows[0] が上端なので、y は下から数える。
                for (int col = 0; col < GlyphWidth && col < line.Length; col++)
                {
                    if (line[col] == ' ')
                    {
                        continue;
                    }

                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = x + col * scale + sx;
                            int py = y + (GlyphHeight - 1 - row) * scale + sy;
                            if (px < 0 || px >= width || py < 0 || py >= height)
                            {
                                continue;
                            }

                            pixels[py * width + px] = color;
                        }
                    }
                }
            }
        }

        static Dictionary<char, string[]> Build()
        {
            var g = new Dictionary<char, string[]>();

            void Add(char c, string r0, string r1, string r2, string r3,
                     string r4, string r5, string r6)
                => g[c] = new[] { r0, r1, r2, r3, r4, r5, r6 };

            Add(' ', "     ", "     ", "     ", "     ", "     ", "     ", "     ");
            Add('0', " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### ");
            Add('1', "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### ");
            Add('2', " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####");
            Add('3', "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### ");
            Add('4', "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # ");
            Add('5', "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### ");
            Add('6', "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### ");
            Add('7', "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   ");
            Add('8', " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### ");
            Add('9', " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  ");
            Add('.', "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  ");
            Add(',', "     ", "     ", "     ", "     ", " ##  ", " ##  ", "  #  ");
            Add('+', "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     ");
            Add('-', "     ", "     ", "     ", "#####", "     ", "     ", "     ");
            Add('/', "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    ");
            Add('(', "   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # ");
            Add(')', " #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   ");
            Add(':', "     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     ");
            Add('_', "     ", "     ", "     ", "     ", "     ", "     ", "#####");

            Add('A', " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #");
            Add('B', "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### ");
            Add('C', " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### ");
            Add('D', "###  ", "#  # ", "#   #", "#   #", "#   #", "#  # ", "###  ");
            Add('E', "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####");
            Add('F', "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    ");
            Add('G', " ### ", "#   #", "#    ", "#  ##", "#   #", "#   #", " ### ");
            Add('H', "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #");
            Add('I', " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### ");
            Add('J', "    #", "    #", "    #", "    #", "#   #", "#   #", " ### ");
            Add('K', "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #");
            Add('L', "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####");
            Add('M', "#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #");
            Add('N', "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #");
            Add('O', " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### ");
            Add('P', "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    ");
            Add('Q', " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #");
            Add('R', "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #");
            Add('S', " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### ");
            Add('T', "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ");
            Add('U', "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### ");
            Add('V', "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  ");
            Add('W', "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #");
            Add('X', "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #");
            Add('Y', "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  ");
            Add('Z', "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####");

            return g;
        }
    }
}
