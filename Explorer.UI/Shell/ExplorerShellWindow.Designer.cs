namespace Explorer.UI.Shell;

partial class ExplorerShellWindow
{
    private System.ComponentModel.IContainer? components = null;

    private Panel _topPanel = null!;
    private Panel _bottomPanel = null!;
    private SplitContainer _splitMain = null!;

    private Button _btnBack = null!;
    private Button _btnForward = null!;
    private Button _btnUp = null!;
    private Button _btnRefresh = null!;

    private Panel _pathHost = null!;
    private Panel _addressLinkPanel = null!;
    private TextBox _txtPath = null!;

    private TreeView _tvNav = null!;
    private ListView _lvItems = null!;

    private Label _lblSelection = null!;
    private Label _lblStatus = null!;
    private TextBox _txtFileName = null!;
    private Label _lblFileType = null!;
    private ComboBox _cmbFileType = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseWindowOwnedResources();

            _toolbarGlyphFont?.Dispose();
            _toolbarGlyphFont = null;

            _addressFont?.Dispose();
            _addressFont = null;

            _addressSeparatorFont?.Dispose();
            _addressSeparatorFont = null;

            _chromeFont?.Dispose();
            _chromeFont = null;

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _topPanel = new Panel();
        _bottomPanel = new Panel();
        _splitMain = new SplitContainer();

        _btnBack = new Button();
        _btnForward = new Button();
        _btnUp = new Button();
        _btnRefresh = new Button();

        _pathHost = new Panel();
        _addressLinkPanel = new Panel();
        _txtPath = new TextBox();

        _tvNav = new TreeView();
        _lvItems = new ListView();

        _lblSelection = new Label();
        _lblStatus = new Label();
        _txtFileName = new TextBox();
        _lblFileType = new Label();
        _cmbFileType = new ComboBox();
        _btnOk = new Button();
        _btnCancel = new Button();

        ((System.ComponentModel.ISupportInitialize)_splitMain).BeginInit();
        _splitMain.Panel1.SuspendLayout();
        _splitMain.Panel2.SuspendLayout();
        _splitMain.SuspendLayout();
        SuspendLayout();

        //
        // _topPanel
        //
        _topPanel.Dock = DockStyle.Top;

        //
        // _bottomPanel
        //
        _bottomPanel.Dock = DockStyle.Bottom;

        //
        // _splitMain
        //
        _splitMain.Dock = DockStyle.Fill;

        //
        // _pathHost
        //
        _pathHost.Controls.Add(_txtPath);
        _pathHost.Controls.Add(_addressLinkPanel);

        //
        // _addressLinkPanel
        //
        _addressLinkPanel.Dock = DockStyle.None;

        //
        // _txtPath
        //
        _txtPath.BorderStyle = BorderStyle.None;
        _txtPath.Dock = DockStyle.None;
        _txtPath.AutoSize = false;

        //
        // _tvNav
        //
        _tvNav.Dock = DockStyle.Fill;
        _tvNav.HideSelection = false;
        _tvNav.FullRowSelect = true;
        _tvNav.LabelEdit = true;

        //
        // _lvItems
        //
        _lvItems.Dock = DockStyle.Fill;
        _lvItems.FullRowSelect = true;
        _lvItems.HideSelection = false;
        _lvItems.MultiSelect = false;
        _lvItems.View = View.Details;
        _lvItems.LabelEdit = true;

        //
        // _lblSelection
        //
        _lblSelection.AutoEllipsis = true;
        _lblSelection.TextAlign = ContentAlignment.MiddleLeft;

        //
        // _lblStatus
        //
        _lblStatus.AutoEllipsis = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;

        //
        // _txtFileName
        //
        _txtFileName.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        //
        // _lblFileType
        //
        _lblFileType.AutoEllipsis = true;
        _lblFileType.Text = "Files of type:";
        _lblFileType.TextAlign = ContentAlignment.MiddleLeft;

        //
        // _cmbFileType
        //
        _cmbFileType.DropDownStyle = ComboBoxStyle.DropDownList;

        //
        // _btnOk
        //
        _btnOk.Text = "OK";
        _btnOk.UseVisualStyleBackColor = true;

        //
        // _btnCancel
        //
        _btnCancel.Text = "Cancel";
        _btnCancel.UseVisualStyleBackColor = true;

        _bottomPanel.Controls.Add(_lblSelection);
        _bottomPanel.Controls.Add(_lblStatus);
        _bottomPanel.Controls.Add(_txtFileName);
        _bottomPanel.Controls.Add(_lblFileType);
        _bottomPanel.Controls.Add(_cmbFileType);
        _bottomPanel.Controls.Add(_btnOk);
        _bottomPanel.Controls.Add(_btnCancel);
        _topPanel.Controls.Add(_btnBack);
        _topPanel.Controls.Add(_btnForward);
        _topPanel.Controls.Add(_btnUp);
        _topPanel.Controls.Add(_btnRefresh);
        _topPanel.Controls.Add(_pathHost);
        _splitMain.Panel1.Controls.Add(_tvNav);
        _splitMain.Panel2.Controls.Add(_lvItems);

        Controls.Add(_splitMain);
        Controls.Add(_bottomPanel);
        Controls.Add(_topPanel);

        Name = "ExplorerShellWindow";
        Text = "File Manager";

        _splitMain.Panel1.ResumeLayout(false);
        _splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_splitMain).EndInit();
        _splitMain.ResumeLayout(false);
        ResumeLayout(false);
    }
}
