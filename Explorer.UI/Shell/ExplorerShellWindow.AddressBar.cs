using Shared.Shell.Theming;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private readonly List<Control> _addressLinkControls = new();
    private string _currentAddressText = string.Empty;
    private bool _isAddressTextMode;
    private string? _lastAddressLinksText;
    private Size _lastAddressLinksSize;
    private int _lastAddressLinksDpi;
    private Font? _lastAddressLinksFont;
    private Font? _lastAddressLinksSeparatorFont;
    private Color _lastAddressLinksBackColor;
    private Color _lastAddressLinksTextColor;
    private Color _lastAddressLinksHoverColor;

    private sealed record AddressBarSegment(string Text, string Path, bool IsCurrent);
    private sealed record AddressBarElement(
        string Text,
        string? Path,
        bool IsSeparator,
        bool IsCurrent,
        bool IsEllipsis,
        int PreferredWidth);

    private static readonly AddressBarBreadcrumbMetrics AddressBreadcrumbDip = AddressBarBreadcrumbMetrics.Default();

    private const TextFormatFlags AddressTextMeasureFlags =
        TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding |
        TextFormatFlags.NoPrefix;

    private const TextFormatFlags AddressTextPaintFlags =
        TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPrefix;

    private void EnterAddressTextMode()
    {
        if (_isAddressTextMode)
            return;

        _isAddressTextMode = true;

        _txtPath.Text = _currentAddressText;
        _addressLinkPanel.Visible = false;
        _txtPath.Visible = true;
        _txtPath.BringToFront();
        _txtPath.Focus();
        _txtPath.SelectAll();
    }

    private void ExitAddressTextMode()
    {
        if (!_isAddressTextMode)
            return;

        _isAddressTextMode = false;

        if (!string.Equals(_txtPath.Text, _currentAddressText, StringComparison.Ordinal))
            _txtPath.Text = _currentAddressText;

        _txtPath.Visible = false;
        _addressLinkPanel.Visible = true;
        _addressLinkPanel.BringToFront();
        RenderAddressLinks();
    }

    private void AddressHost_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_isAddressTextMode)
            EnterAddressTextMode();
    }

    private void AddressLinkPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_isAddressTextMode)
            EnterAddressTextMode();
    }

    private void TxtPath_Leave(object? sender, EventArgs e)
    {
        ExitAddressTextMode();
    }

    private void AddressLinkPanel_Resize(object? sender, EventArgs e)
    {
        if (!_isAddressTextMode)
            RenderAddressLinks();
    }

    private void SetAddressLinkModeColors()
    {
        _addressLinkPanel.BackColor = ShellTheme.ContentBack;
        _addressLinkPanel.ForeColor = ShellTheme.TextColor;
    }

    private void RenderAddressLinks()
    {
        if (_addressLinkPanel.IsDisposed || _isAddressTextMode)
            return;

        Size clientSize = _addressLinkPanel.ClientSize;
        Font? linkFont = _addressFont ?? _txtPath.Font;
        Font? separatorFont = _addressSeparatorFont ?? linkFont;

        bool cacheMatches =
            string.Equals(_lastAddressLinksText, _currentAddressText, StringComparison.Ordinal) &&
            _lastAddressLinksSize == clientSize &&
            _lastAddressLinksDpi == DeviceDpi &&
            ReferenceEquals(_lastAddressLinksFont, linkFont) &&
            ReferenceEquals(_lastAddressLinksSeparatorFont, separatorFont) &&
            _lastAddressLinksBackColor == ShellTheme.ContentBack &&
            _lastAddressLinksTextColor == ShellTheme.TextColor &&
            _lastAddressLinksHoverColor == ShellTheme.ItemHoverBack;

        if (cacheMatches && (_addressLinkControls.Count > 0 || string.IsNullOrWhiteSpace(_currentAddressText)))
            return;

        _addressLinkPanel.SuspendLayout();
        try
        {
            ClearAddressLinkControls();

            _lastAddressLinksText = _currentAddressText;
            _lastAddressLinksSize = clientSize;
            _lastAddressLinksDpi = DeviceDpi;
            _lastAddressLinksFont = linkFont;
            _lastAddressLinksSeparatorFont = separatorFont;
            _lastAddressLinksBackColor = ShellTheme.ContentBack;
            _lastAddressLinksTextColor = ShellTheme.TextColor;
            _lastAddressLinksHoverColor = ShellTheme.ItemHoverBack;

            if (string.IsNullOrWhiteSpace(_currentAddressText))
                return;

            IReadOnlyList<AddressBarElement> elements = BuildAddressBarElements(_currentAddressText);
            if (elements.Count == 0)
                return;

            int availableWidth = Math.Max(0, clientSize.Width);
            int availableHeight = Math.Max(0, clientSize.Height);
            if (availableWidth <= 0 || availableHeight <= 0)
                return;

            List<AddressBarElement> visible = SelectRightFittingAddressElements(elements, availableWidth);
            if (visible.Count == 0)
                return;

            int x = 0;
            int height = availableHeight;

            foreach (AddressBarElement element in visible)
            {
                int width = element.PreferredWidth;
                if (width <= 0)
                    continue;

                Control control = CreateAddressElementControl(element, width, height);
                control.SetBounds(x, 0, width, height);
                _addressLinkPanel.Controls.Add(control);
                _addressLinkControls.Add(control);

                x += width;
            }
        }
        finally
        {
            _addressLinkPanel.ResumeLayout(false);
        }
    }

    private IReadOnlyList<AddressBarElement> BuildAddressBarElements(string addressText)
    {
        IReadOnlyList<AddressBarSegment> segments = BuildAddressBarSegments(addressText);
        if (segments.Count == 0)
            return Array.Empty<AddressBarElement>();

        List<AddressBarElement> elements = new(segments.Count * 2 - 1);

        for (int index = 0; index < segments.Count; index++)
        {
            AddressBarSegment segment = segments[index];
            elements.Add(new AddressBarElement(
                segment.Text,
                segment.Path,
                IsSeparator: false,
                IsCurrent: segment.IsCurrent,
                IsEllipsis: false,
                PreferredWidth: MeasureAddressElementWidth(segment.Text, isSeparator: false)));

            if (index < segments.Count - 1)
            {
                const string separatorText = "›";
                elements.Add(new AddressBarElement(
                    separatorText,
                    null,
                    IsSeparator: true,
                    IsCurrent: false,
                    IsEllipsis: false,
                    PreferredWidth: MeasureAddressElementWidth(separatorText, isSeparator: true)));
            }
        }

        return elements;
    }

    private IReadOnlyList<AddressBarSegment> BuildAddressBarSegments(string addressText)
    {
        string trimmed = addressText.Trim();
        if (trimmed.Length == 0)
            return Array.Empty<AddressBarSegment>();

        if (string.Equals(trimmed, "This PC", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new AddressBarSegment("This PC", ExplorerShellWindowPresenter.ThisPcPath, IsCurrent: true)
            };
        }

        string? root;
        try
        {
            root = Path.GetPathRoot(trimmed);
        }
        catch
        {
            root = null;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return new[]
            {
                new AddressBarSegment(trimmed, trimmed, IsCurrent: true)
            };
        }

        string normalizedRoot = EnsureTrailingDirectorySeparator(root);
        string normalizedAddress = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootWithoutSlash = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool rootIsCurrent = string.Equals(normalizedAddress, rootWithoutSlash, StringComparison.OrdinalIgnoreCase);

        List<AddressBarSegment> segments = new()
        {
            new AddressBarSegment(GetAddressDriveDisplayName(normalizedRoot), normalizedRoot, rootIsCurrent)
        };

        if (rootIsCurrent)
            return segments;

        string relative = trimmed.Length > normalizedRoot.Length
            ? trimmed[normalizedRoot.Length..]
            : string.Empty;

        string currentPath = normalizedRoot;
        string[] parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        for (int index = 0; index < parts.Length; index++)
        {
            currentPath = Path.Combine(currentPath, parts[index]);
            bool isCurrent = index == parts.Length - 1;
            segments.Add(new AddressBarSegment(parts[index], currentPath, isCurrent));
        }

        return segments;
    }

    private List<AddressBarElement> SelectRightFittingAddressElements(
        IReadOnlyList<AddressBarElement> elements,
        int availableWidth)
    {
        int start = 0;
        int totalWidth = elements.Sum(static element => element.PreferredWidth);

        if (totalWidth <= availableWidth)
            return elements.ToList();

        AddressBarElement ellipsisElement = CreateAddressEllipsisElement();
        int availablePathWidth = Math.Max(0, availableWidth - ellipsisElement.PreferredWidth);

        while (start < elements.Count - 1 && totalWidth > availablePathWidth)
        {
            totalWidth -= elements[start].PreferredWidth;
            start++;
        }

        while (start < elements.Count - 1 && elements[start].IsSeparator)
        {
            totalWidth -= elements[start].PreferredWidth;
            start++;
        }

        List<AddressBarElement> visible = elements.Skip(start).ToList();
        if (visible.Count == 0 && elements.Count > 0)
        {
            visible.Add(elements[^1]);
            totalWidth = elements[^1].PreferredWidth;
        }

        while (visible.Count > 1 && totalWidth + ellipsisElement.PreferredWidth > availableWidth)
        {
            totalWidth -= visible[0].PreferredWidth;
            visible.RemoveAt(0);

            while (visible.Count > 1 && visible[0].IsSeparator)
            {
                totalWidth -= visible[0].PreferredWidth;
                visible.RemoveAt(0);
            }
        }

        visible.Insert(0, ellipsisElement);
        return visible;
    }
 
    private AddressBarElement CreateAddressEllipsisElement()
    {
        const string ellipsisText = "...";
        return new AddressBarElement(
            ellipsisText,
            null,
            IsSeparator: true,
            IsCurrent: false,
            IsEllipsis: true,
            PreferredWidth: MeasureAddressElementWidth(ellipsisText, isSeparator: true, isEllipsis: true));
    }

    private Control CreateAddressElementControl(AddressBarElement element, int width, int height)
    {
        AddressBarBreadcrumbMetricsPx metrics = GetAddressBreadcrumbMetricsPx();
        AddressBreadcrumbItem item = new()
        {
            Text = element.Text,
            TargetPath = element.Path,
            IsSeparator = element.IsSeparator,
            IsCurrent = element.IsCurrent,
            IsEllipsis = element.IsEllipsis,
            BackColor = ShellTheme.ContentBack,
            Cursor = element.Path != null ? Cursors.Hand : Cursors.Default,
            TextFont = GetAddressElementFont(element),
            LinkPadX = metrics.LinkPadX,
            SeparatorPadX = metrics.SeparatorPadX,
            VisualOuterPadX = metrics.VisualOuterPadX,
            SeparatorTextOffsetY = metrics.SeparatorTextOffsetY,
            NormalBackColor = ShellTheme.ContentBack,
            NormalTextColor = ShellTheme.TextColor,
            HoverBackColor = ShellTheme.ItemHoverBack,
            Margin = Padding.Empty
        };

        item.NavigateRequested += _presenter.NavigateToPath;
        item.TextModeRequested += (_, _) => EnterAddressTextMode();

        return item;
    }

    private Font GetAddressElementFont(AddressBarElement element)
    {
        if (element.IsSeparator && !element.IsEllipsis)
            return _addressSeparatorFont ?? _addressFont ?? _txtPath.Font;

        return _addressFont ?? _txtPath.Font;
    }

    private int MeasureAddressElementWidth(string text, bool isSeparator, bool isEllipsis = false)
    {
        AddressBarBreadcrumbMetricsPx metrics = GetAddressBreadcrumbMetricsPx();
        Font font = isSeparator && !isEllipsis
            ? _addressSeparatorFont ?? _addressFont ?? _txtPath.Font
            : _addressFont ?? _txtPath.Font;
        int textWidth = MeasureAddressTextWidth(text, font);
        int horizontalPadding = isSeparator
            ? metrics.SeparatorPadX * 2
            : metrics.LinkPadX * 2;
        int minimumWidth = isSeparator
            ? metrics.MinimumSeparatorWidth
            : metrics.MinimumLinkWidth;

        return Math.Max(minimumWidth, textWidth + horizontalPadding);
    }

    private AddressBarBreadcrumbMetricsPx GetAddressBreadcrumbMetricsPx()
    {
        return AddressBarBreadcrumbMetricsPx.FromDip(AddressBreadcrumbDip, ScaleDip);
    }

    private static int MeasureAddressTextWidth(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Match the taskbar button text measurement model. The extra bleed
        // accounts for glyph overhang and DPI rounding without forcing every
        // breadcrumb segment to use a large fixed padding value.
        Size measured = TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            AddressTextMeasureFlags);

        return measured.Width;
    }

    private readonly struct AddressBarBreadcrumbMetrics
    {
        public readonly int LinkPadX;
        public readonly int SeparatorPadX;
        public readonly int VisualOuterPadX;
        public readonly int HighlightPadTop;
        public readonly int HighlightPadBottom;
        public readonly int MinimumLinkWidth;
        public readonly int MinimumSeparatorWidth;
        public readonly int SeparatorTextOffsetY;

        private AddressBarBreadcrumbMetrics(
            int linkPadX,
            int separatorPadX,
            int visualOuterPadX,
            int highlightPadTop,
            int highlightPadBottom,
            int minimumLinkWidth,
            int minimumSeparatorWidth,
            int separatorTextOffsetY)
        {
            LinkPadX = linkPadX;
            SeparatorPadX = separatorPadX;
            VisualOuterPadX = visualOuterPadX;
            HighlightPadTop = highlightPadTop;
            HighlightPadBottom = highlightPadBottom;
            MinimumLinkWidth = minimumLinkWidth;
            MinimumSeparatorWidth = minimumSeparatorWidth;
            SeparatorTextOffsetY = separatorTextOffsetY;
        }

        public static AddressBarBreadcrumbMetrics Default()
        {
            return new AddressBarBreadcrumbMetrics(
                linkPadX: 4,
                separatorPadX: 7,
                visualOuterPadX: 0,
                highlightPadTop: 2,
                highlightPadBottom: 2,
                minimumLinkWidth: 20,
                minimumSeparatorWidth: 14,
                separatorTextOffsetY: -3);
        }
    }

    private readonly struct AddressBarBreadcrumbMetricsPx
    {
        public readonly int LinkPadX;
        public readonly int SeparatorPadX;
        public readonly int VisualOuterPadX;
        public readonly int HighlightPadTop;
        public readonly int HighlightPadBottom;
        public readonly int MinimumLinkWidth;
        public readonly int MinimumSeparatorWidth;
        public readonly int SeparatorTextOffsetY;

        private AddressBarBreadcrumbMetricsPx(
            int linkPadX,
            int separatorPadX,
            int visualOuterPadX,
            int highlightPadTop,
            int highlightPadBottom,
            int minimumLinkWidth,
            int minimumSeparatorWidth,
            int separatorTextOffsetY)
        {
            LinkPadX = linkPadX;
            SeparatorPadX = separatorPadX;
            VisualOuterPadX = visualOuterPadX;
            HighlightPadTop = highlightPadTop;
            HighlightPadBottom = highlightPadBottom;
            MinimumLinkWidth = minimumLinkWidth;
            MinimumSeparatorWidth = minimumSeparatorWidth;
            SeparatorTextOffsetY = separatorTextOffsetY;
        }

        public static AddressBarBreadcrumbMetricsPx FromDip(
            AddressBarBreadcrumbMetrics metrics,
            Func<int, int> scale)
        {
            return new AddressBarBreadcrumbMetricsPx(
                linkPadX: scale(metrics.LinkPadX),
                separatorPadX: scale(metrics.SeparatorPadX),
                visualOuterPadX: scale(metrics.VisualOuterPadX),

                // Highlight inset is intentionally fixed physical pixels so the
                // hover rectangle keeps a crisp address-bar edge at every DPI.
                highlightPadTop: metrics.HighlightPadTop,
                highlightPadBottom: metrics.HighlightPadBottom,

                minimumLinkWidth: scale(metrics.MinimumLinkWidth),
                minimumSeparatorWidth: scale(metrics.MinimumSeparatorWidth),

                // This tracks the scaled separator font. At 100% -3 becomes -3;
                // at 200% it becomes -6.
                separatorTextOffsetY: scale(metrics.SeparatorTextOffsetY));
        }
    }

    private sealed class AddressBreadcrumbItem : Control
    {
        private bool _hover;
        private bool _pressed;
        private Font? _textFont;

        public string? TargetPath { get; set; }
        public bool IsSeparator { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsEllipsis { get; set; }
        public bool IsClickable => TargetPath != null;
        private bool IsHoverable => IsClickable;

        public int LinkPadX { get; set; }
        public int SeparatorPadX { get; set; }
        public int VisualOuterPadX { get; set; }
        public int SeparatorTextOffsetY { get; set; }

        public Color NormalBackColor { get; set; } = SystemColors.Window;
        public Color NormalTextColor { get; set; } = SystemColors.WindowText;
        public Color HoverBackColor { get; set; } = SystemColors.ControlLight;

        public Font? TextFont
        {
            get => _textFont;
            set
            {
                if (ReferenceEquals(_textFont, value))
                    return;

                _textFont = value;
                Invalidate();
            }
        }

        public event Action<string>? NavigateRequested;
        public event EventHandler? TextModeRequested;
        public event MouseEventHandler? BreadcrumbRightClick;

        protected override bool ShowFocusCues => false;

        public AddressBreadcrumbItem()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            TabStop = false;
            Margin = Padding.Empty;
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress default background painting; OnPaint fills the full control.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(NormalBackColor);

            Rectangle chrome = Rectangle.FromLTRB(
                ClientRectangle.Left + VisualOuterPadX,
                ClientRectangle.Top,
                ClientRectangle.Right - VisualOuterPadX,
                ClientRectangle.Bottom);

            if (chrome.Width <= 0 || chrome.Height <= 0)
                return;

            if (IsHoverable && (_pressed || _hover))
            {
                using SolidBrush brush = new(HoverBackColor);
                e.Graphics.FillRectangle(brush, chrome);
            }

            int horizontalPadding = (IsSeparator || IsEllipsis) ? SeparatorPadX : LinkPadX;
            Rectangle textRect = Rectangle.FromLTRB(
                chrome.Left + horizontalPadding,
                chrome.Top,
                chrome.Right - horizontalPadding,
                chrome.Bottom);

            if (textRect.Width <= 0 || textRect.Height <= 0)
                textRect = chrome;

            if (IsSeparator && !IsEllipsis && SeparatorTextOffsetY != 0)
                textRect.Offset(0, SeparatorTextOffsetY);

            TextFormatFlags flags = AddressTextPaintFlags;
            if (IsSeparator || IsEllipsis)
                flags |= TextFormatFlags.HorizontalCenter;

            Color textColor = NormalTextColor;
            Font textFont = TextFont ?? Font;
            TextRenderer.DrawText(e.Graphics, Text, textFont, textRect, textColor, flags);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (!IsHoverable)
                return;

            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (!_hover && !_pressed)
                return;

            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                if (IsClickable)
                {
                    _pressed = true;
                    Invalidate();
                }
                else if (!IsCurrent)
                {
                    TextModeRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (e.Button == MouseButtons.Right && TargetPath != null && !IsSeparator && !IsEllipsis)
                BreadcrumbRightClick?.Invoke(this, e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            bool wasPressed = _pressed;
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }

            if (e.Button != MouseButtons.Left || !wasPressed || !IsClickable || IsCurrent)
                return;

            if (ClientRectangle.Contains(e.Location) && TargetPath is { } targetPath)
                NavigateRequested?.Invoke(targetPath);
        }
    }

    private string GetAddressDriveDisplayName(string driveRoot)
    {
        TreeNode? driveNode = FindDriveRootTreeNode(driveRoot);
        if (!string.IsNullOrWhiteSpace(driveNode?.Text))
            return driveNode.Text;

        return driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private void ResetAddressLinkRenderCache()
    {
        _lastAddressLinksText = null;
        _lastAddressLinksSize = Size.Empty;
        _lastAddressLinksDpi = 0;
        _lastAddressLinksFont = null;
        _lastAddressLinksSeparatorFont = null;
        _lastAddressLinksBackColor = Color.Empty;
        _lastAddressLinksTextColor = Color.Empty;
        _lastAddressLinksHoverColor = Color.Empty;
    }

    private void ClearAddressLinkControls()
    {
        if (_addressLinkControls.Count == 0)
            return;

        foreach (Control control in _addressLinkControls)
        {
            _addressLinkPanel.Controls.Remove(control);
            control.Dispose();
        }

        _addressLinkControls.Clear();
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
