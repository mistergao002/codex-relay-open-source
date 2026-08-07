namespace CodexRelay;

public sealed class MainForm : Form
{
    private readonly ConfigStore _store;
    private readonly RetryEngine _engine;
    private readonly CodexConfigInspector _inspector;

    private readonly Label _statusTitle = new();
    private readonly Label _stateBadge = new();
    private readonly Label _attemptValue = new();
    private readonly Label _highDemandValue = new();
    private readonly Label _elapsedValue = new();
    private readonly Label _exitCodeValue = new();
    private readonly TabControl _navigation = new();
    private readonly TabPage _configPage = new("运行配置");
    private readonly TabPage _logsPage = new("日志");
    private readonly TextBox _commandBox = new();
    private readonly TextBox _workDirBox = new();
    private readonly NumericUpDown _intervalInput = new();
    private readonly NumericUpDown _maxTriesInput = new();
    private readonly TextBox _allowedUrlsBox = new();
    private readonly CheckBox _notifyCheck = new();
    private readonly CheckBox _openDashboardCheck = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _saveButton = new();
    private readonly RichTextBox _logBox = new();
    private readonly CheckBox _autoScrollCheck = new();
    private bool _allowClose;
    private bool _closeInProgress;

    public MainForm(ConfigStore store, RetryEngine engine, CodexConfigInspector inspector)
    {
        _store = store;
        _engine = engine;
        _inspector = inspector;

        Text = "Codex Relay 3.0";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 700);
        Size = new Size(1160, 820);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(241, 245, 249);

        TryUseExecutableIcon();
        BuildInterface();
        PopulateConfiguration(_store.LoadConfig());
        SynchronizeCurrentUrlAtStartup();

        _engine.LogEmitted += HandleLogEntry;
        _engine.StatusChanged += HandleStatusChanged;
        _engine.Succeeded += HandleSucceeded;
        FormClosing += HandleFormClosing;
        FormClosed += HandleFormClosed;
    }

    public int NavigationTabCount => _navigation.TabPages.Count;
    public bool LogWordWrapEnabled => _logBox.WordWrap;
    public RichTextBoxScrollBars LogScrollBars => _logBox.ScrollBars;
    public string AllowedBaseUrlsText => _allowedUrlsBox.Text;

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildNavigation(), 0, 1);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(22, 12, 22, 10)
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var titleLine = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        var productTitle = new Label
        {
            Text = "Codex Relay",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Margin = new Padding(0, 0, 10, 0)
        };
        _statusTitle.Text = "等待开始";
        _statusTitle.AutoEllipsis = true;
        _statusTitle.Font = new Font("Segoe UI", 9.5F);
        _statusTitle.ForeColor = Color.FromArgb(100, 116, 139);
        _statusTitle.Dock = DockStyle.Fill;
        _statusTitle.TextAlign = ContentAlignment.MiddleLeft;

        _stateBadge.Text = "空闲";
        _stateBadge.AutoSize = false;
        _stateBadge.TextAlign = ContentAlignment.MiddleCenter;
        _stateBadge.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        _stateBadge.ForeColor = Color.FromArgb(71, 85, 105);
        _stateBadge.BackColor = Color.FromArgb(226, 232, 240);
        _stateBadge.Size = new Size(64, 25);
        _stateBadge.Margin = new Padding(0, 1, 0, 0);

        titleLine.Controls.Add(productTitle);
        titleLine.Controls.Add(_stateBadge);
        titlePanel.Controls.Add(titleLine, 0, 0);
        titlePanel.Controls.Add(_statusTitle, 0, 1);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 4)
        };

        StylePrimaryButton(_startButton, "开始重试");
        _startButton.Click += StartButtonClick;
        StyleDangerButton(_stopButton, "停止");
        _stopButton.Enabled = false;
        _stopButton.Click += StopButtonClick;
        StyleNeutralButton(_saveButton, "保存配置");
        _saveButton.Click += SaveButtonClick;
        var viewLogsButton = new Button();
        StyleNeutralButton(viewLogsButton, "查看日志");
        viewLogsButton.Click += (_, _) => OpenLogDirectory();

        actionPanel.Controls.Add(_startButton);
        actionPanel.Controls.Add(_stopButton);
        actionPanel.Controls.Add(_saveButton);
        actionPanel.Controls.Add(viewLogsButton);

        titleRow.Controls.Add(titlePanel, 0, 0);
        titleRow.Controls.Add(actionPanel, 1, 0);

        var metrics = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 0)
        };
        metrics.Controls.Add(CreateMetricCard("尝试次数", _attemptValue));
        metrics.Controls.Add(CreateMetricCard("高负载次数", _highDemandValue));
        metrics.Controls.Add(CreateMetricCard("运行时间", _elapsedValue));
        metrics.Controls.Add(CreateMetricCard("最后退出码", _exitCodeValue));

        headerLayout.Controls.Add(titleRow, 0, 0);
        headerLayout.Controls.Add(metrics, 0, 1);
        header.Controls.Add(headerLayout);
        return header;
    }

    private Control BuildNavigation()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 12),
            BackColor = BackColor
        };

        _navigation.Dock = DockStyle.Fill;
        _navigation.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _navigation.Padding = new Point(18, 7);

        _configPage.BackColor = Color.FromArgb(248, 250, 252);
        _logsPage.BackColor = Color.FromArgb(248, 250, 252);
        _configPage.Controls.Add(BuildConfigurationPage());
        _logsPage.Controls.Add(BuildLogsPage());
        _navigation.TabPages.Add(_configPage);
        _navigation.TabPages.Add(_logsPage);

        container.Controls.Add(_navigation);
        return container;
    }

    private Control BuildConfigurationPage()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 8, 10, 8),
            Margin = Padding.Empty
        };

        GroupBox commandGroup = BuildCommandGroup();
        GroupBox directoryGroup = BuildDirectoryGroup();
        GroupBox retryGroup = BuildRetryGroup();
        GroupBox successGroup = BuildSuccessGroup();
        flow.Controls.Add(commandGroup);
        flow.Controls.Add(directoryGroup);
        flow.Controls.Add(retryGroup);
        flow.Controls.Add(successGroup);

        void ResizeGroups()
        {
            int width = Math.Max(720, flow.ClientSize.Width - 18);
            foreach (Control control in flow.Controls)
            {
                control.Width = width;
            }
        }

        flow.ClientSizeChanged += (_, _) => ResizeGroups();
        ResizeGroups();
        return flow;
    }

    private GroupBox BuildCommandGroup()
    {
        var group = CreateGroup("执行命令", 122);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10, 6, 10, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var description = new Label
        {
            Text = "命令会在下方工作目录中运行；codex exec 与其他命令采用相同的子进程流程。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _commandBox.Dock = DockStyle.Fill;
        _commandBox.Multiline = true;
        _commandBox.AcceptsReturn = true;
        _commandBox.AcceptsTab = false;
        _commandBox.ScrollBars = ScrollBars.None;
        _commandBox.WordWrap = true;
        _commandBox.Font = new Font("Consolas", 10F);
        _commandBox.BorderStyle = BorderStyle.FixedSingle;

        layout.Controls.Add(description, 0, 0);
        layout.Controls.Add(_commandBox, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private GroupBox BuildDirectoryGroup()
    {
        var group = CreateGroup("工作目录", 78);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));

        _workDirBox.Dock = DockStyle.Fill;
        _workDirBox.Font = new Font("Segoe UI", 9.5F);
        _workDirBox.Margin = new Padding(0, 4, 10, 4);
        var chooseButton = new Button
        {
            Text = "选择…",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2)
        };
        chooseButton.Click += ChooseDirectoryClick;

        layout.Controls.Add(_workDirBox, 0, 0);
        layout.Controls.Add(chooseButton, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private GroupBox BuildRetryGroup()
    {
        var group = CreateGroup("重试参数", 140);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(10, 6, 10, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _intervalInput.Minimum = 1;
        _intervalInput.Maximum = 86_400;
        _intervalInput.Width = 150;
        _intervalInput.Anchor = AnchorStyles.Left;
        _intervalInput.Margin = new Padding(0, 6, 0, 6);
        _maxTriesInput.Minimum = 0;
        _maxTriesInput.Maximum = 1_000_000;
        _maxTriesInput.Width = 150;
        _maxTriesInput.Anchor = AnchorStyles.Left;
        _maxTriesInput.Margin = new Padding(0, 6, 0, 6);

        layout.Controls.Add(CreateFieldLabel("重试间隔（秒）"), 0, 0);
        layout.Controls.Add(_intervalInput, 1, 0);
        layout.Controls.Add(CreateFieldLabel("最大次数（0 = 无限）"), 2, 0);
        layout.Controls.Add(_maxTriesInput, 3, 0);

        var urlLabel = new Label
        {
            Text = "允许的 Base URL（逗号、分号或换行分隔）",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
            TextAlign = ContentAlignment.BottomLeft
        };
        layout.Controls.Add(urlLabel, 0, 1);
        layout.SetColumnSpan(urlLabel, 4);

        var urlRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        _allowedUrlsBox.Dock = DockStyle.Fill;
        _allowedUrlsBox.Multiline = true;
        _allowedUrlsBox.ScrollBars = ScrollBars.None;
        _allowedUrlsBox.WordWrap = true;
        _allowedUrlsBox.Font = new Font("Consolas", 9.5F);
        _allowedUrlsBox.Margin = new Padding(0, 6, 10, 0);
        var syncButton = new Button
        {
            Text = "同步当前 URL",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0)
        };
        syncButton.Click += SyncCurrentUrlClick;
        urlRow.Controls.Add(_allowedUrlsBox, 0, 0);
        urlRow.Controls.Add(syncButton, 1, 0);
        layout.Controls.Add(urlRow, 0, 2);
        layout.SetColumnSpan(urlRow, 4);

        group.Controls.Add(layout);
        return group;
    }

    private GroupBox BuildSuccessGroup()
    {
        var group = CreateGroup("成功动作", 70);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 10, 12, 6),
            WrapContents = false
        };
        _notifyCheck.Text = "成功后播放提示音并闪烁窗口";
        _notifyCheck.AutoSize = true;
        _notifyCheck.Margin = new Padding(0, 4, 28, 0);
        _openDashboardCheck.Text = "成功后打开本地状态面板";
        _openDashboardCheck.AutoSize = true;
        _openDashboardCheck.Margin = new Padding(0, 4, 0, 0);
        flow.Controls.Add(_notifyCheck);
        flow.Controls.Add(_openDashboardCheck);
        group.Controls.Add(flow);
        return group;
    }

    private Control BuildLogsPage()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 4)
        };
        _autoScrollCheck.Text = "自动滚动";
        _autoScrollCheck.Checked = true;
        _autoScrollCheck.AutoSize = true;
        _autoScrollCheck.Margin = new Padding(0, 8, 22, 0);
        var clearScreenButton = new Button { Text = "清屏", AutoSize = true, Height = 30 };
        clearScreenButton.Click += (_, _) => _logBox.Clear();
        var clearHistoryButton = new Button { Text = "清空历史日志", AutoSize = true, Height = 30 };
        clearHistoryButton.Click += ClearHistoryClick;
        var openDirectoryButton = new Button { Text = "打开日志目录", AutoSize = true, Height = 30 };
        openDirectoryButton.Click += (_, _) => OpenLogDirectory();
        toolbar.Controls.Add(_autoScrollCheck);
        toolbar.Controls.Add(clearScreenButton);
        toolbar.Controls.Add(clearHistoryButton);
        toolbar.Controls.Add(openDirectoryButton);

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.WordWrap = true;
        _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _logBox.DetectUrls = false;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.FromArgb(2, 6, 23);
        _logBox.ForeColor = Color.FromArgb(203, 213, 225);
        _logBox.Font = new Font("Consolas", 9.5F);
        _logBox.HideSelection = false;

        panel.Controls.Add(_logBox);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private async void StartButtonClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            LauncherConfig config = ReadConfiguration();
            await _store.SaveConfigAsync(config);
            _navigation.SelectedTab = _logsPage;
            SetRunningControls(true);
            await _engine.RunAsync(config);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DirectoryNotFoundException or IOException)
        {
            MessageBox.Show(this, exception.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRunningControls(_engine.IsRunning);
        }
    }

    private async void StopButtonClick(object? sender, EventArgs eventArgs)
    {
        _stopButton.Enabled = false;
        await _engine.StopAsync();
        SetRunningControls(_engine.IsRunning);
    }

    private async void SaveButtonClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _store.SaveConfigAsync(ReadConfiguration());
            ShowTransientTitle("配置已保存");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChooseDirectoryClick(object? sender, EventArgs eventArgs)
    {
        using var picker = new DirectoryPickerForm(_workDirBox.Text);
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            _workDirBox.Text = picker.SelectedPath;
        }
    }

    private void SynchronizeCurrentUrlAtStartup()
    {
        if (!TrySynchronizeCurrentUrl(showFailure: false, out _))
        {
            return;
        }

        try
        {
            _store.SaveConfigAsync(ReadConfiguration()).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The synchronized value is still available in the UI. A later save or run can persist it.
        }
    }

    private void SyncCurrentUrlClick(object? sender, EventArgs eventArgs)
    {
        if (TrySynchronizeCurrentUrl(showFailure: true, out string synchronizedUrl))
        {
            ShowTransientTitle($"已同步 {synchronizedUrl}");
        }
    }

    private bool TrySynchronizeCurrentUrl(bool showFailure, out string synchronizedUrl)
    {
        synchronizedUrl = string.Empty;
        CodexConfigInfo info;
        try
        {
            info = _inspector.Inspect();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (showFailure)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "同步失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        if (!info.Found)
        {
            if (showFailure)
            {
                MessageBox.Show(
                    this,
                    $"未在 {info.ConfigPath} 找到当前 Codex base_url。",
                    "同步失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        synchronizedUrl = info.BaseUrl;
        _allowedUrlsBox.Text = synchronizedUrl;
        return true;
    }

    private void ClearHistoryClick(object? sender, EventArgs eventArgs)
    {
        DialogResult answer = MessageBox.Show(
            this,
            "只清理 codex-retry-*.log 与 latest.log；状态文件和其他历史数据会保留。继续吗？",
            "清空历史日志",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        int count = _store.ClearHistoryLogs();
        AppendLog(new LogEntry(DateTimeOffset.Now, LogLevel.Info, $"已清理 {count} 个历史运行日志。"));
    }

    private async void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || !_engine.IsRunning || _closeInProgress)
        {
            return;
        }

        eventArgs.Cancel = true;
        DialogResult answer = MessageBox.Show(
            this,
            "当前命令仍在运行。停止整个进程树并退出吗？",
            "确认退出",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _closeInProgress = true;
        await _engine.StopAsync();
        _allowClose = true;
        Close();
    }

    private void HandleFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _engine.LogEmitted -= HandleLogEntry;
        _engine.StatusChanged -= HandleStatusChanged;
        _engine.Succeeded -= HandleSucceeded;
    }

    private void HandleLogEntry(LogEntry entry)
    {
        RunOnUiThread(() => AppendLog(entry));
    }

    private void HandleStatusChanged(LauncherStatus status)
    {
        RunOnUiThread(() => ApplyStatus(status));
    }

    private void HandleSucceeded()
    {
        RunOnUiThread(() => NativeMethods.FlashWindow(Handle));
    }

    private void ApplyStatus(LauncherStatus status)
    {
        _statusTitle.Text = status.Message;
        _attemptValue.Text = status.Attempt.ToString();
        _highDemandValue.Text = status.HighDemandCount.ToString();
        _elapsedValue.Text = status.ElapsedText;
        _exitCodeValue.Text = status.LastExitCode?.ToString() ?? "-";

        (_stateBadge.Text, _stateBadge.BackColor, _stateBadge.ForeColor) = status.Status switch
        {
            "running" => ("运行中", Color.FromArgb(219, 234, 254), Color.FromArgb(29, 78, 216)),
            "success" => ("成功", Color.FromArgb(220, 252, 231), Color.FromArgb(21, 128, 61)),
            "failed" => ("失败", Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28)),
            "stopped" => ("已停止", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9)),
            _ => ("空闲", Color.FromArgb(226, 232, 240), Color.FromArgb(71, 85, 105))
        };
        SetRunningControls(status.IsRunning);
    }

    private void AppendLog(LogEntry entry)
    {
        Color color = entry.Level switch
        {
            LogLevel.Success => Color.FromArgb(74, 222, 128),
            LogLevel.Warning => Color.FromArgb(250, 204, 21),
            LogLevel.Error => Color.FromArgb(248, 113, 113),
            _ => Color.FromArgb(203, 213, 225)
        };

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = Color.FromArgb(100, 116, 139);
        _logBox.AppendText($"[{entry.Timestamp:HH:mm:ss}] ");
        _logBox.SelectionColor = color;
        _logBox.AppendText(entry.Message + Environment.NewLine);
        _logBox.SelectionColor = _logBox.ForeColor;
        TrimInMemoryLog();

        if (_autoScrollCheck.Checked)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    private void TrimInMemoryLog()
    {
        const int maximum = 1_200_000;
        const int target = 900_000;
        if (_logBox.TextLength <= maximum)
        {
            return;
        }

        int removeLength = _logBox.TextLength - target;
        string text = _logBox.Text;
        int newline = text.IndexOf('\n', removeLength);
        if (newline >= 0)
        {
            removeLength = newline + 1;
        }

        _logBox.Select(0, removeLength);
        _logBox.SelectedText = string.Empty;
    }

    private LauncherConfig ReadConfiguration() => new()
    {
        Command = _commandBox.Text.Trim(),
        WorkDir = _workDirBox.Text.Trim(),
        Interval = decimal.ToInt32(_intervalInput.Value),
        MaxTries = decimal.ToInt32(_maxTriesInput.Value),
        Notify = _notifyCheck.Checked,
        OpenDashboard = _openDashboardCheck.Checked,
        AllowedBaseUrls = _allowedUrlsBox.Text.Trim()
    };

    private void PopulateConfiguration(LauncherConfig config)
    {
        _commandBox.Text = config.Command;
        _workDirBox.Text = config.WorkDir;
        _intervalInput.Value = Math.Clamp(config.Interval, 1, 86_400);
        _maxTriesInput.Value = Math.Clamp(config.MaxTries, 0, 1_000_000);
        _notifyCheck.Checked = config.Notify;
        _openDashboardCheck.Checked = config.OpenDashboard;
        _allowedUrlsBox.Text = config.AllowedBaseUrls;
    }

    private void SetRunningControls(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _saveButton.Enabled = !running;
        _configPage.Enabled = !running;
    }

    private void OpenLogDirectory()
    {
        try
        {
            _store.OpenLogDirectory();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void ShowTransientTitle(string message)
    {
        string previous = _statusTitle.Text;
        _statusTitle.Text = message;
        await Task.Delay(2200);
        if (!_engine.IsRunning && !IsDisposed)
        {
            _statusTitle.Text = previous;
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // The form handle was destroyed during shutdown.
            }
            return;
        }

        action();
    }

    private static GroupBox CreateGroup(string text, int height) => new()
    {
        Text = text,
        Height = height,
        Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(30, 41, 59),
        BackColor = Color.White,
        Margin = new Padding(0, 0, 0, 8),
        Padding = new Padding(4)
    };

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(71, 85, 105)
    };

    private static Panel CreateMetricCard(string caption, Label valueLabel)
    {
        var card = new Panel
        {
            Width = 198,
            Height = 60,
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(13, 8, 13, 6)
        };
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5F)
        };
        valueLabel.Text = caption == "运行时间" ? "00:00:00" : caption == "最后退出码" ? "-" : "0";
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.ForeColor = Color.FromArgb(15, 23, 42);
        valueLabel.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        return card;
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        StyleButtonBase(button, text);
        button.BackColor = Color.FromArgb(37, 99, 235);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
    }

    private static void StyleDangerButton(Button button, string text)
    {
        StyleButtonBase(button, text);
        button.BackColor = Color.FromArgb(220, 38, 38);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
    }

    private static void StyleNeutralButton(Button button, string text)
    {
        StyleButtonBase(button, text);
        button.BackColor = Color.FromArgb(248, 250, 252);
        button.ForeColor = Color.FromArgb(51, 65, 85);
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
    }

    private static void StyleButtonBase(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Width = 94;
        button.Height = 36;
        button.FlatStyle = FlatStyle.Flat;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void TryUseExecutableIcon()
    {
        try
        {
            Icon? executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (executableIcon is not null)
            {
                Icon = executableIcon;
            }
        }
        catch (ArgumentException)
        {
            // The default WinForms icon is sufficient during development.
        }
    }
}
