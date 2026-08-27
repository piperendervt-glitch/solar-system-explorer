namespace SolarSystem.Core
{
    /// <summary>
    /// F4 デバッグパネルの寸法を決める (Step 8-0b 修正)。
    ///
    /// **UnityEngine 非依存。** 文字幅は Unity 側で計測して渡してもらう。
    /// こうしておくと「1920x1080 と 1280x720 で収まる」ことを
    /// EditMode テストで縛れる。OnGUI は batchmode で描けないので
    /// (CLAUDE.md 0-B)、数値で縛る以外に自動化の手が無い。
    /// </summary>
    public sealed class DebugPanelLayout
    {
        public int FontSize;
        public float LineHeight;
        public float Width;
        public float Height;

        /// <summary>表示する項目の先頭 index。</summary>
        public int FirstItem;

        /// <summary>表示する項目の件数。</summary>
        public int ItemCount;

        /// <summary>全項目が入りきらず、カーソル周辺だけを出しているか。</summary>
        public bool Windowed;
    }

    public static class DebugPanelLayoutSolver
    {
        public const int MaxFontSize = 14;
        public const int MinFontSize = 10;

        /// <summary>画面の縁からの余白 [px]。</summary>
        public const float Margin = 12f;

        /// <summary>背景板の内側の余白 [px]。</summary>
        public const float Padding = 8f;

        /// <summary>
        /// 行高 = フォント + 6。
        /// **+3 だと日本語のグリフが縦に切れる** (1920x1080 の実機で確認)。
        /// 欧文の行送りでは足りない。
        /// </summary>
        public static float LineHeightFor(int fontSize) => fontSize + 6f;

        /// <summary>
        /// 寸法を決める。
        ///
        /// widthAtBaseFont は **MaxFontSize での**最長行の幅 [px]。
        /// 幅はフォントサイズに比例するとみなして換算する。
        /// </summary>
        public static DebugPanelLayout Solve(
            int screenWidth, int screenHeight,
            int headerLines, int itemCount, int bodyLines,
            float widthAtBaseFont, int cursor)
        {
            if (screenWidth <= 0) { screenWidth = 1; }
            if (screenHeight <= 0) { screenHeight = 1; }
            if (itemCount < 0) { itemCount = 0; }

            float availableW = screenWidth - Margin * 2f;
            float availableH = screenHeight - Margin * 2f;

            // 項目の間に空行を 1 本入れて天体表を離す。
            int fixedLines = headerLines + 1 + bodyLines;

            for (int font = MaxFontSize; font >= MinFontSize; font--)
            {
                float lh = LineHeightFor(font);
                float h = Padding * 2f + (fixedLines + itemCount) * lh;
                if (h <= availableH)
                {
                    return Build(font, lh, itemCount, availableW, availableH,
                                 widthAtBaseFont, fixedLines, 0, itemCount, false);
                }
            }

            // 最小フォントでも入らない。カーソル周辺だけを出す。
            // 上下 2 行を「... 他 N 件」の目印に使う。
            int minFont = MinFontSize;
            float minLh = LineHeightFor(minFont);
            float room = availableH - Padding * 2f - fixedLines * minLh;
            int fits = (int)(room / minLh);
            int shown = fits - 2;
            if (shown < 1) { shown = 1; }
            if (shown > itemCount) { shown = itemCount; }

            int first = cursor - shown / 2;
            if (first < 0) { first = 0; }
            if (first + shown > itemCount) { first = itemCount - shown; }
            if (first < 0) { first = 0; }

            return Build(minFont, minLh, itemCount, availableW, availableH,
                         widthAtBaseFont, fixedLines + 2, first, shown, true);
        }

        static DebugPanelLayout Build(
            int font, float lineHeight, int totalItems,
            float availableW, float availableH, float widthAtBaseFont,
            int fixedLines, int first, int shown, bool windowed)
        {
            float width = widthAtBaseFont * font / MaxFontSize + Padding * 2f;
            if (width > availableW) { width = availableW; }
            if (width < 1f) { width = 1f; }

            float height = Padding * 2f + (fixedLines + shown) * lineHeight;
            if (height > availableH) { height = availableH; }

            return new DebugPanelLayout
            {
                FontSize = font,
                LineHeight = lineHeight,
                Width = width,
                Height = height,
                FirstItem = first,
                ItemCount = shown,
                Windowed = windowed,
            };
        }
    }
}
