namespace Explorer.UI.Layout
{
    public sealed class ExplorerLayoutMetrics
    {
        // Form
        public int MinimumHeightDip { get; init; } = 540;
        public int InitialWidthDip { get; init; } = 755;
        public int InitialHeightDip { get; init; } = 480;

        // Top bar / toolbar chrome
        public int TopBarOuterPadLeftPx { get; init; } = 6;
        public int TopBarOuterPadTopPx { get; init; } = 6;
        public int TopBarOuterPadRightPx { get; init; } = 6;
        public int TopBarOuterPadBottomPx { get; init; } = 6;

        public int ToolbarButtonSizeDip { get; init; } = 30;
        public int ToolbarButtonGapPx { get; init; } = 6;

        // Address bar host is based on top bar height
        public int AddressHostLeftGapPx { get; init; } = 6;
        public int AddressHostRightGapPx { get; init; } = 12;
        public int AddressInnerLeftPx { get; init; } = 6;
        public int AddressInnerRightPx { get; init; } = 6;
        public int AddressTextHeightDip { get; init; } = 18;

        // Main split
        public int NavPaneWidthDip { get; init; } = 160;

        // Bottom bar
        public int BottomBarHeightDip { get; init; } = 76;
        public int BottomBarBrowseHeightDip { get; init; } = 30;
        public int BottomBarPadLeftPx { get; init; } = 6;
        public int BottomBarPadTopPx { get; init; } = 4;
        public int BottomBarPadRightPx { get; init; } = 6;
        public int BottomBarPadBottomPx { get; init; } = 6;

        public int BottomBarBrowsePadLeftPx { get; init; } = 6;
        public int BottomBarBrowsePadTopPx { get; init; } = 2;
        public int BottomBarBrowsePadRightPx { get; init; } = 6;
        public int BottomBarBrowsePadBottomPx { get; init; } = 2;

        public int StatusLabelLeftPx { get; init; } = 8;
        public int StatusLabelTopDip { get; init; } = 12;
        public int StatusLabelBrowseTopDip { get; init; } = 7;
        public int StatusLabelWidthDip { get; init; } = 260;
        public int StatusLabelHeightDip { get; init; } = 20;

        public int FileNameLeftDip { get; init; } = 275;
        public int FileNameTopDip { get; init; } = 8;
        public int FileNameWidthDip { get; init; } = 300;
        public int FileNameHeightDip { get; init; } = 24;

        public int DialogButtonWidthDip { get; init; } = 90;
        public int DialogButtonHeightDip { get; init; } = 26;
        public int DialogButtonRightPx { get; init; } = 8;
        public int DialogButtonGapPx { get; init; } = 8;
        public int DialogButtonTopDip { get; init; } = 8;

        // List columns
        public int NameColumnWidthDip { get; init; } = 215;
        public int ThisPcNameColumnWidthDip { get; init; } = 160;
        public int TypeColumnWidthDip { get; init; } = 120;
        public int ThisPcTypeColumnWidthDip { get; init; } = 80;
        public int SizeColumnWidthDip { get; init; } = 95;
        public int DateColumnWidthDip { get; init; } = 140;

        // Images / fonts
        public int SmallIconSizeDip { get; init; } = 16;
        public float ToolbarGlyphFontSizePt { get; init; } = 10f;
        public float AddressFontSizePt { get; init; } = 11f;
        public float AddressSeparatorFontSizePt { get; init; } = 16f;
        public float ChromeFontSizePt { get; init; } = 9f;

        // Fixed px visual nudges that should not scale
        public int ToolbarGlyphTopPaddingPx { get; init; } = 2;
    }
}
