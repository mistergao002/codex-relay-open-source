namespace CodexRelay;

public sealed class DirectoryPickerForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly ListView _directoryList = new();
    private readonly Label _hintLabel = new();

    public DirectoryPickerForm(string initialPath)
    {
        Text = "选择工作目录";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 480);
        Size = new Size(820, 580);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(248, 250, 252);

        BuildInterface();
        NavigateTo(Directory.Exists(initialPath) ? initialPath : string.Empty);
    }

    public string SelectedPath { get; private set; } = string.Empty;

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var addressBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = Padding.Empty
        };
        addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));

        var upButton = new Button
        {
            Text = "↑ 上一级",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(0, 0, 8, 6)
        };
        upButton.Click += (_, _) => NavigateUp();

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 2, 8, 6);
        _pathBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                NavigateTo(_pathBox.Text);
            }
        };

        var goButton = new Button
        {
            Text = "转到",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6)
        };
        goButton.Click += (_, _) => NavigateTo(_pathBox.Text);

        addressBar.Controls.Add(upButton, 0, 0);
        addressBar.Controls.Add(_pathBox, 1, 0);
        addressBar.Controls.Add(goButton, 2, 0);

        _directoryList.Dock = DockStyle.Fill;
        _directoryList.View = View.Details;
        _directoryList.FullRowSelect = true;
        _directoryList.HideSelection = false;
        _directoryList.MultiSelect = false;
        _directoryList.BorderStyle = BorderStyle.FixedSingle;
        _directoryList.Columns.Add("文件夹", 500);
        _directoryList.Columns.Add("位置", 220);
        _directoryList.DoubleClick += (_, _) => OpenSelectedDirectory();
        _directoryList.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                OpenSelectedDirectory();
            }
        };

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = Padding.Empty
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));

        _hintLabel.Text = "双击进入文件夹，点击“选择此目录”确认。";
        _hintLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _hintLabel.Dock = DockStyle.Fill;
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 10, 0, 0)
        };
        var selectButton = new Button
        {
            Text = "选择此目录",
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 10, 0, 0),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        selectButton.FlatAppearance.BorderSize = 0;
        selectButton.Click += (_, _) => SelectCurrentDirectory();

        bottom.Controls.Add(_hintLabel, 0, 0);
        bottom.Controls.Add(cancelButton, 1, 0);
        bottom.Controls.Add(selectButton, 2, 0);

        root.Controls.Add(addressBar, 0, 0);
        root.Controls.Add(_directoryList, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);

        CancelButton = cancelButton;
        AcceptButton = selectButton;
    }

    private void NavigateUp()
    {
        string current = _pathBox.Text.Trim();
        if (current.Length == 0)
        {
            return;
        }

        DirectoryInfo? parent = Directory.GetParent(current);
        NavigateTo(parent?.FullName ?? string.Empty);
    }

    private void NavigateTo(string requestedPath)
    {
        try
        {
            _directoryList.BeginUpdate();
            _directoryList.Items.Clear();

            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                _pathBox.Text = string.Empty;
                foreach (DriveInfo drive in DriveInfo.GetDrives().Where(item => item.IsReady))
                {
                    var item = new ListViewItem(drive.Name)
                    {
                        Tag = drive.RootDirectory.FullName
                    };
                    item.SubItems.Add($"{drive.DriveType} · {FormatBytes(drive.AvailableFreeSpace)} 可用");
                    _directoryList.Items.Add(item);
                }
                _hintLabel.Text = "请选择一个磁盘或输入完整目录。";
                return;
            }

            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath.Trim()));
            if (!Directory.Exists(path))
            {
                _hintLabel.Text = "目录不存在，请检查路径。";
                return;
            }

            _pathBox.Text = path;
            foreach (string directory in Directory.EnumerateDirectories(path)
                         .OrderBy(item => Path.GetFileName(item), StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new ListViewItem(Path.GetFileName(directory))
                {
                    Tag = directory
                };
                item.SubItems.Add(directory);
                _directoryList.Items.Add(item);
            }
            _hintLabel.Text = $"当前目录包含 {_directoryList.Items.Count} 个子文件夹。";
        }
        catch (UnauthorizedAccessException)
        {
            _hintLabel.Text = "当前目录受系统保护，请选择其他位置。";
        }
        catch (IOException exception)
        {
            _hintLabel.Text = $"目录读取失败：{exception.Message}";
        }
        catch (ArgumentException)
        {
            _hintLabel.Text = "目录路径格式不正确。";
        }
        finally
        {
            _directoryList.EndUpdate();
        }
    }

    private void OpenSelectedDirectory()
    {
        if (_directoryList.SelectedItems.Count == 0 ||
            _directoryList.SelectedItems[0].Tag is not string path)
        {
            return;
        }

        NavigateTo(path);
    }

    private void SelectCurrentDirectory()
    {
        string path = _pathBox.Text.Trim();
        if (!Directory.Exists(path))
        {
            _hintLabel.Text = "请先进入一个有效目录。";
            return;
        }

        SelectedPath = Path.GetFullPath(path);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
