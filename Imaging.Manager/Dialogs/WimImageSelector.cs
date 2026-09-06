using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimImageSelector : UserControl
{
    private const int CaptionWidth = 76;
    private const int ColumnGap = 8;
    private const int SelectorHeight = 26;
    private const int DescriptionGap = 4;

    private readonly ComboBox? _images;
    private readonly WimImageInfo? _singleImage;
    private readonly Label _description;

    public WimImageSelector(IReadOnlyList<WimImageInfo> images, Font font)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one WIM image is required.", nameof(images));

        AutoScaleMode = AutoScaleMode.None;
        Font = font;
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        int bodyHeight = Math.Max(18, Font.Height + 4);
        int valueLeft = CaptionWidth + ColumnGap;

        Label caption = new()
        {
            Left = 0,
            Top = 2,
            Width = CaptionWidth,
            Height = bodyHeight,
            Text = "Image:"
        };

        _description = new Label
        {
            Left = valueLeft,
            Top = SelectorHeight + DescriptionGap,
            Height = bodyHeight * 2,
            AutoEllipsis = true
        };

        if (images.Count == 1)
        {
            _singleImage = images[0];
            Controls.Add(new Label
            {
                Left = valueLeft,
                Top = 2,
                Height = bodyHeight,
                AutoEllipsis = true,
                Text = _singleImage.DisplayName
            });
        }
        else
        {
            _images = new ComboBox
            {
                Left = valueLeft,
                Top = 0,
                Height = SelectorHeight,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(WimImageInfo.DisplayName)
            };
            foreach (WimImageInfo image in images)
                _images.Items.Add(image);
            _images.SelectedIndexChanged += (_, _) =>
            {
                UpdateDescription();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            _images.SelectedIndex = 0;
            Controls.Add(_images);
        }

        Controls.AddRange(new Control[] { caption, _description });
        Height = _description.Bottom;
        UpdateDescription();
    }

    public event EventHandler? SelectionChanged;

    public WimImageInfo SelectedImage => _singleImage
        ?? _images?.SelectedItem as WimImageInfo
        ?? throw new InvalidOperationException("No WIM image is selected.");

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        int valueLeft = CaptionWidth + ColumnGap;
        int valueWidth = Math.Max(0, ClientSize.Width - valueLeft);
        if (_images != null)
            _images.Width = valueWidth;

        foreach (Label label in Controls.OfType<Label>())
        {
            if (label.Left == valueLeft)
                label.Width = valueWidth;
        }
    }

    private void UpdateDescription()
    {
        WimImageInfo? image = _singleImage ?? _images?.SelectedItem as WimImageInfo;
        _description.Text = image == null || string.IsNullOrWhiteSpace(image.Description)
            ? string.Empty
            : image.Description;
    }
}
