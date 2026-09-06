using Shared.Shell.Theming;

namespace Imaging.Manager;

public partial class MainForm
{
    private sealed class ImagingMenuRenderer : ToolStripProfessionalRenderer
    {
        public ImagingMenuRenderer()
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using SolidBrush brush = new(ShellTheme.ContentBack);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using SolidBrush brush = new(ShellTheme.ContentBack);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using Pen pen = new(ShellTheme.ContentBorder);
            if (e.ToolStrip is MenuStrip)
            {
                int y = Math.Max(0, e.ToolStrip.Height - 1);
                e.Graphics.DrawLine(pen, 0, y, Math.Max(0, e.ToolStrip.Width - 1), y);
                return;
            }

            Rectangle border = new(
                0,
                0,
                Math.Max(0, e.ToolStrip.Width - 1),
                Math.Max(0, e.ToolStrip.Height - 1));
            e.Graphics.DrawRectangle(pen, border);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool highlighted = e.Item.Selected ||
                               (e.Item is ToolStripMenuItem menuItem && menuItem.DropDown.Visible);
            if (!highlighted)
                return;

            Rectangle fillBounds = new(
                0,
                0,
                Math.Max(1, e.Item.Width - 1),
                Math.Max(1, e.Item.Height - 1));

            // Drop-down item rendering is clipped to the item's bounds. Drawing the
            // border directly on x=0 puts half of the left pen outside that clip,
            // which makes the left edge disappear. Keep the selected fill full-size
            // but inset the drop-down border one pixel so all four sides render.
            Rectangle borderBounds = e.Item.Owner is ToolStripDropDown
                ? new Rectangle(
                    1,
                    1,
                    Math.Max(1, e.Item.Width - 3),
                    Math.Max(1, e.Item.Height - 3))
                : fillBounds;

            using SolidBrush brush = new(ShellTheme.ItemSelectedBack);
            using Pen pen = new(ShellTheme.ItemSelectedBorder);
            e.Graphics.FillRectangle(brush, fillBounds);
            e.Graphics.DrawRectangle(pen, borderBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = Math.Max(0, e.Item.Height / 2);
            int left = e.Vertical ? y : 3;
            using Pen pen = new(ShellTheme.ContentBorder);
            if (e.Vertical)
                e.Graphics.DrawLine(pen, left, 3, left, Math.Max(3, e.Item.Height - 4));
            else
                e.Graphics.DrawLine(pen, 3, y, Math.Max(3, e.Item.Width - 4), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ShellTheme.TextColor : SystemColors.GrayText;
            base.OnRenderItemText(e);
        }
    }

    private void InitializeMainMenu()
    {
        _mainMenu = new MenuStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new ImagingMenuRenderer(),
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            ShowItemToolTips = false
        };
        _mainMenu.MenuActivate += (_, _) => DeselectTilesFromNeutralInteraction();
        _mainMenu.GotFocus += (_, _) => DeselectTilesFromNeutralInteraction();
        _mainMenu.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        ToolStripMenuItem imagesMenu = new("&Images");
        _miImageInfo = CreateMenuItem("Image &Info...", async () => await ShowImageInfoAsync());
        _miMountWim = CreateMenuItem("&Mount WIM...", async () => await MountWimAsync());
        _miExportWim = CreateMenuItem("&Export WIM...", async () => await ExportWimAsync());
        _miDeleteWimImage = CreateMenuItem("&Delete WIM Image...", async () => await DeleteWimImageFromMenuAsync());
        _miSplitWim = CreateMenuItem("&Split WIM...", async () => await SplitWimFromMenuAsync());
        imagesMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _miImageInfo,
            new ToolStripSeparator(),
            _miMountWim,
            _miExportWim,
            _miDeleteWimImage,
            _miSplitWim
        });

        ToolStripMenuItem mountsMenu = new("&Mounts");
        _miCleanupMounts = CreateMenuItem("&Cleanup Mounts...", async () => await CleanupMountsAsync());
        mountsMenu.DropDownItems.Add(_miCleanupMounts);

        ToolStripMenuItem viewMenu = new("&View");
        _miRefresh = CreateMenuItem("&Refresh", async () => await RefreshViewAsync());
        _miRefresh.ShortcutKeys = Keys.F5;
        _miRefresh.ShowShortcutKeys = true;
        viewMenu.DropDownItems.Add(_miRefresh);

        _mainMenu.Items.AddRange(new ToolStripItem[] { imagesMenu, mountsMenu, viewMenu });
        ApplyMainMenuAppearance();

        Controls.Add(_mainMenu);
        MainMenuStrip = _mainMenu;
    }

    private ToolStripMenuItem CreateMenuItem(string text, Func<Task> action)
    {
        ToolStripMenuItem item = new(text);
        item.Click += async (_, _) => await RunUiActionAsync(action);
        return item;
    }

    private void ApplyMainMenuAppearance()
    {
        if (_mainMenu is null || _mainMenu.IsDisposed)
            return;

        if (_chromeFont != null)
            _mainMenu.Font = _chromeFont;

        _mainMenu.BackColor = ShellTheme.ContentBack;
        _mainMenu.ForeColor = ShellTheme.TextColor;

        foreach (ToolStripItem item in _mainMenu.Items)
            ApplyMenuItemAppearance(item);

        _mainMenu.Invalidate();
    }

    private void ApplyMenuItemAppearance(ToolStripItem item)
    {
        item.ForeColor = ShellTheme.TextColor;
        if (_chromeFont != null)
            item.Font = _chromeFont;

        if (item is not ToolStripMenuItem menuItem)
            return;

        menuItem.DropDown.Renderer = _mainMenu.Renderer;
        menuItem.DropDown.BackColor = ShellTheme.ContentBack;
        menuItem.DropDown.ForeColor = ShellTheme.TextColor;
        foreach (ToolStripItem child in menuItem.DropDownItems)
            ApplyMenuItemAppearance(child);
    }
}
