namespace Explorer.UI.Layout
{
    public sealed class ExplorerLayoutMetricsPx
    {
        public int MinimumHeight { get; init; }
        public int InitialWidth { get; init; }
        public int InitialHeight { get; init; }

        public int TopBarHeight { get; init; }
        public Padding TopBarPadding { get; init; }

        public int ToolbarButtonSize { get; init; }
        public int ToolbarButtonGap { get; init; }

        public int AddressHostLeft { get; init; }
        public int AddressHostTop { get; init; }
        public int AddressHostHeight { get; init; }
        public int AddressHostRightGap { get; init; }

        public int AddressInnerLeft { get; init; }
        public int AddressInnerRight { get; init; }
        public int AddressTextHeight { get; init; }

        public int BottomBarHeight { get; init; }
        public int BottomBarBrowseHeight { get; init; }
        public Padding BottomBarPadding { get; init; }
        public Padding BottomBarBrowsePadding { get; init; }

        public int StatusLabelLeft { get; init; }
        public int StatusLabelTop { get; init; }
        public int StatusLabelBrowseTop { get; init; }
        public int StatusLabelWidth { get; init; }
        public int StatusLabelHeight { get; init; }

        public int FileNameLeft { get; init; }
        public int FileNameTop { get; init; }
        public int FileNameWidth { get; init; }
        public int FileNameHeight { get; init; }

        public int DialogButtonWidth { get; init; }
        public int DialogButtonHeight { get; init; }
        public int DialogButtonRight { get; init; }
        public int DialogButtonGap { get; init; }
        public int DialogButtonTop { get; init; }

        public int NameColumnWidth { get; init; }
        public int ThisPcNameColumnWidth { get; init; }
        public int TypeColumnWidth { get; init; }
        public int ThisPcTypeColumnWidth { get; init; }
        public int SizeColumnWidth { get; init; }
        public int DateColumnWidth { get; init; }

        public Size SmallImageSize { get; init; }

        public float ToolbarGlyphFontSize { get; init; }
        public float AddressFontSize { get; init; }
        public float AddressSeparatorFontSize { get; init; }
        public float ChromeFontSize { get; init; }
        public int ToolbarGlyphTopPaddingPx { get; init; }

        public static ExplorerLayoutMetricsPx FromDip(
            ExplorerLayoutMetrics dip,
            Func<int, int> scale,
            Func<float, float> scaleFontPointToPx)
        {
            int toolbarButtonSize = scale(dip.ToolbarButtonSizeDip);
            int topBarHeight = toolbarButtonSize + dip.TopBarOuterPadTopPx + dip.TopBarOuterPadBottomPx;

            return new ExplorerLayoutMetricsPx
            {
                MinimumHeight = scale(dip.MinimumHeightDip),
                InitialWidth = scale(dip.InitialWidthDip),
                InitialHeight = scale(dip.InitialHeightDip),

                TopBarHeight = topBarHeight,
                TopBarPadding = new Padding(
                    dip.TopBarOuterPadLeftPx,
                    dip.TopBarOuterPadTopPx,
                    dip.TopBarOuterPadRightPx,
                    dip.TopBarOuterPadBottomPx),

                ToolbarButtonSize = toolbarButtonSize,
                ToolbarButtonGap = dip.ToolbarButtonGapPx,

                AddressHostLeft = dip.TopBarOuterPadLeftPx + (toolbarButtonSize * 4) + (dip.ToolbarButtonGapPx * 3) + dip.AddressHostLeftGapPx,
                AddressHostTop = dip.TopBarOuterPadTopPx,
                AddressHostHeight = toolbarButtonSize,
                AddressHostRightGap = dip.AddressHostRightGapPx,

                AddressInnerLeft = dip.AddressInnerLeftPx,
                AddressInnerRight = dip.AddressInnerRightPx,
                AddressTextHeight = scale(dip.AddressTextHeightDip),

                BottomBarHeight = scale(dip.BottomBarHeightDip),
                BottomBarBrowseHeight = scale(dip.BottomBarBrowseHeightDip),
                BottomBarPadding = new Padding(
                    dip.BottomBarPadLeftPx,
                    dip.BottomBarPadTopPx,
                    dip.BottomBarPadRightPx,
                    dip.BottomBarPadBottomPx),
                    BottomBarBrowsePadding = new Padding(
                        dip.BottomBarBrowsePadLeftPx,
                        dip.BottomBarBrowsePadTopPx,
                        dip.BottomBarBrowsePadRightPx,
                        dip.BottomBarBrowsePadBottomPx),

                StatusLabelLeft = dip.StatusLabelLeftPx,
                StatusLabelTop = scale(dip.StatusLabelTopDip),
                StatusLabelBrowseTop = scale(dip.StatusLabelBrowseTopDip),
                StatusLabelWidth = scale(dip.StatusLabelWidthDip),
                StatusLabelHeight = scale(dip.StatusLabelHeightDip),

                FileNameLeft = scale(dip.FileNameLeftDip),
                FileNameTop = scale(dip.FileNameTopDip),
                FileNameWidth = scale(dip.FileNameWidthDip),
                FileNameHeight = scale(dip.FileNameHeightDip),

                DialogButtonWidth = scale(dip.DialogButtonWidthDip),
                DialogButtonHeight = scale(dip.DialogButtonHeightDip),
                DialogButtonRight = dip.DialogButtonRightPx,
                DialogButtonGap = dip.DialogButtonGapPx,
                DialogButtonTop = scale(dip.DialogButtonTopDip),

                NameColumnWidth = scale(dip.NameColumnWidthDip),
                ThisPcNameColumnWidth = scale(dip.ThisPcNameColumnWidthDip),
                TypeColumnWidth = scale(dip.TypeColumnWidthDip),
                ThisPcTypeColumnWidth = scale(dip.ThisPcTypeColumnWidthDip),
                SizeColumnWidth = scale(dip.SizeColumnWidthDip),
                DateColumnWidth = scale(dip.DateColumnWidthDip),

                SmallImageSize = new Size(scale(dip.SmallIconSizeDip), scale(dip.SmallIconSizeDip)),

                ToolbarGlyphFontSize = scaleFontPointToPx(dip.ToolbarGlyphFontSizePt),
                AddressFontSize = scaleFontPointToPx(dip.AddressFontSizePt),
                AddressSeparatorFontSize = scaleFontPointToPx(dip.AddressSeparatorFontSizePt),
                ChromeFontSize = scaleFontPointToPx(dip.ChromeFontSizePt),
                ToolbarGlyphTopPaddingPx = dip.ToolbarGlyphTopPaddingPx
            };
        }
    }
}