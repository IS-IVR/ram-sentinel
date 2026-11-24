using System.Diagnostics;
using RamSentinel.App.Platform;
using RamSentinel.Core;

namespace RamSentinel.App.UI;

internal sealed class MainForm : Form
{
    private readonly WindowsMemoryProvider _memoryProvider = new();
    private readonly MemoryPressureEvaluator _pressureEvaluator = new(TimeSpan.FromSeconds(20));
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10_000 };
    private readonly NotifyIcon _notifyIcon;
    private readonly Label _statusLabel = new();
    private readonly Label _totalLabel = new();
    private readonly Label _usedLabel = new();
    private readonly Label _availableLabel = new();
    private readonly ListView _processList = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private DateTimeOffset _lastProcessRefresh = DateTimeOffset.MinValue;
    private bool _allowExit;
    private bool _updatingStartupCheckBox;

    public MainForm()
    {
        Text = "RAM Sentinel";
        Width = 760;
        Height = 510;
        MinimumSize = new Size(650, 430);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("פתח חלון", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("פתח את מנהל המשימות", null, (_, _) => OpenTaskManager());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("יציאה", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RAM Sentinel - ניטור זיכרון",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();

        Shown += (_, _) =>
        {
            _updatingStartupCheckBox = true;
            _startWithWindowsCheckBox.Checked = StartupManager.IsEnabled();
            _updatingStartupCheckBox = false;
            HideToTray();
            RefreshSnapshot();
        };
    }

    private void BuildInterface()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 112, Padding = new Padding(16) };
        var title = new Label
        {
            Text = "RAM Sentinel",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight
        };
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 28;
        _statusLabel.Text = "ממתין לקריאת נתוני זיכרון...";
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(_statusLabel);
        header.Controls.Add(title);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 76,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(10, 4, 10, 4)
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        metrics.Controls.Add(CreateMetric("סה״כ RAM", _totalLabel), 0, 0);
        metrics.Controls.Add(CreateMetric("בשימוש", _usedLabel), 1, 0);
        metrics.Controls.Add(CreateMetric("זמין", _availableLabel), 2, 0);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.GridLines = true;
        _processList.HideSelection = false;
        _processList.Columns.Add("תהליך", 280);
        _processList.Columns.Add("Working Set", 150);
        _processList.Columns.Add("זיכרון פרטי", 150);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12, 8, 12, 8)
        };

        _startWithWindowsCheckBox.Text = "הפעל עם הכניסה ל-Windows";
        _startWithWindowsCheckBox.AutoSize = true;
        _startWithWindowsCheckBox.Margin = new Padding(8, 8, 18, 0);
        _startWithWindowsCheckBox.CheckedChanged += (_, _) => UpdateStartupSetting();

        var taskManagerButton = new Button { Text = "פתח מנהל המשימות", AutoSize = true };
        taskManagerButton.Click += (_, _) => OpenTaskManager();
        var hideButton = new Button { Text = "העבר לאזור ההתראות", AutoSize = true };
        hideButton.Click += (_, _) => HideToTray();

        footer.Controls.Add(_startWithWindowsCheckBox);
        footer.Controls.Add(taskManagerButton);
        footer.Controls.Add(hideButton);

        Controls.Add(_processList);
        Controls.Add(footer);
        Controls.Add(metrics);
        Controls.Add(header);
    }

    private static Control CreateMetric(string caption, Label valueLabel)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(4) };
        var title = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText
        };
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        valueLabel.TextAlign = ContentAlignment.MiddleRight;
        valueLabel.Text = "—";
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(title);
        return panel;
    }

    private void RefreshSnapshot()
    {
        try
        {
            MemorySnapshot snapshot = _memoryProvider.Read();
            _totalLabel.Text = FormatBytes(snapshot.TotalPhysicalBytes);
            _usedLabel.Text = FormatBytes(snapshot.UsedPhysicalBytes);
            _availableLabel.Text = FormatBytes(snapshot.AvailablePhysicalBytes);

            MemoryPressureLevel level = _pressureEvaluator.Evaluate(snapshot, snapshot.CapturedAt);
            _statusLabel.Text = level switch
            {
                MemoryPressureLevel.Normal => $"מצב תקין · שימוש בזיכרון: {snapshot.MemoryLoadPercent}%",
                MemoryPressureLevel.Warning => $"נרשם שימוש גבוה · {snapshot.MemoryLoadPercent}% · ממתין לאימות שהמצב מתמשך",
                MemoryPressureLevel.High => $"לחץ זיכרון מתמשך · {snapshot.MemoryLoadPercent}% · בדוק אילו יישומים פתוחים",
                _ => "מצב זיכרון לא ידוע"
            };

            _notifyIcon.Text = $"RAM Sentinel - {snapshot.MemoryLoadPercent}% בשימוש";

            if (DateTimeOffset.Now - _lastProcessRefresh >= TimeSpan.FromSeconds(30))
            {
                RefreshProcessList();
                _lastProcessRefresh = DateTimeOffset.Now;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"לא ניתן לקרוא נתוני זיכרון: {ex.Message}";
        }
    }

    private void RefreshProcessList()
    {
        var rows = new List<(string Name, long WorkingSet, long PrivateBytes)>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    rows.Add((process.ProcessName, process.WorkingSet64, process.PrivateMemorySize64));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // A process can exit or become inaccessible while the list is being collected.
                }
            }
        }

        _processList.BeginUpdate();
        try
        {
            _processList.Items.Clear();
            foreach (var row in rows.OrderByDescending(x => x.WorkingSet).Take(15))
            {
                var item = new ListViewItem(row.Name);
                item.SubItems.Add(FormatBytes((ulong)Math.Max(0, row.WorkingSet)));
                item.SubItems.Add(FormatBytes((ulong)Math.Max(0, row.PrivateBytes)));
                _processList.Items.Add(item);
            }
        }
        finally
        {
            _processList.EndUpdate();
        }
    }

    private void UpdateStartupSetting()
    {
        if (_updatingStartupCheckBox)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(_startWithWindowsCheckBox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RAM Sentinel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updatingStartupCheckBox = true;
            _startWithWindowsCheckBox.Checked = StartupManager.IsEnabled();
            _updatingStartupCheckBox = false;
        }
    }

    private void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"לא ניתן לפתוח את מנהל המשימות: {ex.Message}", "RAM Sentinel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshSnapshot();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _timer.Dispose();
        base.OnFormClosing(e);
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.0} {units[unit]}";
    }
}
