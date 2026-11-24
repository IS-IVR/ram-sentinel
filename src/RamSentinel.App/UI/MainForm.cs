using System.Diagnostics;
using RamSentinel.App.Platform;
using RamSentinel.Core;

namespace RamSentinel.App.UI;

internal sealed class MainForm : Form
{
    private readonly WindowsMemoryProvider _memoryProvider = new();
    private readonly WindowsDiskMetricsProvider _diskMetricsProvider = new();
    private readonly StartupInventoryProvider _startupInventoryProvider = new();
    private readonly WindowsCpuUsageProvider _cpuUsageProvider = new();
    private readonly WindowsMemoryPagingProvider _pagingMetricsProvider = new();
    private readonly DiagnosticsEngine _diagnosticsEngine = new();
    private readonly PerformanceSampleHistory _diagnosticHistory = new();
    private readonly DiagnosticsSummaryView _diagnosticsView = new();
    private readonly Dictionary<int, ProcessCpuSample> _processCpuSamples = new();
    private IReadOnlyList<ProcessPerformanceSample> _lastProcessSamples = Array.Empty<ProcessPerformanceSample>();
    private DateTimeOffset _lastDiagnosticSampleAt = DateTimeOffset.MinValue;
    private double? _latestCpuPercent;
    private static readonly TimeSpan DiagnosticSampleInterval = TimeSpan.FromSeconds(30);
    private readonly int _logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30_000 };
    private readonly NotifyIcon _notifyIcon;
    private readonly Label _statusLabel = new();
    private readonly Label _totalLabel = new();
    private readonly Label _usedLabel = new();
    private readonly Label _availableLabel = new();
    private readonly Label _cacheLabel = new();
    private readonly Label _commitLabel = new();
    private readonly Label _cpuLabel = new();
    private readonly Label _diskLabel = new();
    private readonly Label _systemDriveSpaceLabel = new();
    private readonly Label _explanationLabel = new();
    private readonly ListView _processList = new();
    private readonly ListView _startupList = new();
    private readonly TabControl _mainTabs = new();
    private DateTimeOffset _lastStartupRefresh = DateTimeOffset.MinValue;
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly Button _closeSelectedButton = new();
    private DateTimeOffset _lastProcessRefresh = DateTimeOffset.MinValue;
    private bool _allowExit;
    private bool _updatingStartupCheckBox;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "svchost", "dwm", "explorer"
    };

    public MainForm()
    {
        Text = "RAM Sentinel";
        Width = 940;
        Height = 620;
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("פתח חלון", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("רענן מדדים", null, (_, _) => RefreshAll());
        trayMenu.Items.Add("פתח את מנהל המשימות", null, (_, _) => OpenTaskManager());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("יציאה", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RAM Sentinel - ניטור ביצועי זיכרון",
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
            _timer.Interval = 10_000;
            RefreshAll();
        };
    }

    private void BuildInterface()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(16, 10, 16, 6) };
        var title = new Label
        {
            Text = "RAM Sentinel",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight
        };

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 27;
        _statusLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _statusLabel.Text = "ניטור מקומי פעיל. לא מתבצעים שינויים אוטומטיים.";
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;

        _explanationLabel.Dock = DockStyle.Top;
        _explanationLabel.Height = 24;
        _explanationLabel.Text = "האבחון קורא מדדים בלבד. לא מתבצעים שינויים אוטומטיים במחשב.";
        _explanationLabel.ForeColor = SystemColors.GrayText;
        _explanationLabel.TextAlign = ContentAlignment.MiddleRight;

        header.Controls.Add(_explanationLabel);
        header.Controls.Add(_statusLabel);
        header.Controls.Add(title);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 82,
            ColumnCount = 8,
            RowCount = 1,
            Padding = new Padding(6, 3, 6, 3)
        };
        for (int i = 0; i < 8; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5F));
        }

        metrics.Controls.Add(CreateMetric("RAM כולל", _totalLabel), 0, 0);
        metrics.Controls.Add(CreateMetric("בשימוש", _usedLabel), 1, 0);
        metrics.Controls.Add(CreateMetric("RAM זמין", _availableLabel), 2, 0);
        metrics.Controls.Add(CreateMetric("Cache", _cacheLabel), 3, 0);
        metrics.Controls.Add(CreateMetric("Commit", _commitLabel), 4, 0);
        metrics.Controls.Add(CreateMetric("CPU", _cpuLabel), 5, 0);
        metrics.Controls.Add(CreateMetric("דיסק R/W", _diskLabel), 6, 0);
        metrics.Controls.Add(CreateMetric("שטח פנוי", _systemDriveSpaceLabel), 7, 0);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.GridLines = true;
        _processList.HideSelection = false;
        _processList.MultiSelect = false;
        _processList.Columns.Add("תהליך", 220);
        _processList.Columns.Add("PID", 65);
        _processList.Columns.Add("CPU", 75);
        _processList.Columns.Add("RAM פיזי", 125);
        _processList.Columns.Add("זיכרון פרטי", 125);

        _startupList.Dock = DockStyle.Fill;
        _startupList.View = View.Details;
        _startupList.FullRowSelect = true;
        _startupList.GridLines = true;
        _startupList.HideSelection = false;
        _startupList.Columns.Add("שם", 180);
        _startupList.Columns.Add("היקף", 105);
        _startupList.Columns.Add("מקור", 270);
        _startupList.Columns.Add("יעד", 90);
        _startupList.Columns.Add("פקודת הפעלה", 420);

        var processMenu = new ContextMenuStrip();
        processMenu.Items.Add("בקש סגירה רגילה של התוכנה...", null, (_, _) => CloseSelectedApplication());
        _processList.ContextMenuStrip = processMenu;
        _processList.SelectedIndexChanged += (_, _) =>
        {
            _closeSelectedButton.Enabled = _processList.SelectedItems.Count == 1;
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12, 8, 12, 8)
        };

        _startWithWindowsCheckBox.Text = "הפעל עם הכניסה ל-Windows";
        _startWithWindowsCheckBox.AutoSize = true;
        _startWithWindowsCheckBox.Margin = new Padding(8, 8, 18, 0);
        _startWithWindowsCheckBox.CheckedChanged += (_, _) => UpdateStartupSetting();

        _closeSelectedButton.Text = "סגור תוכנה נבחרת";
        _closeSelectedButton.AutoSize = true;
        _closeSelectedButton.Enabled = false;
        _closeSelectedButton.Click += (_, _) => CloseSelectedApplication();

        var taskManagerButton = new Button { Text = "פתח מנהל המשימות", AutoSize = true };
        taskManagerButton.Click += (_, _) => OpenTaskManager();
        var hideButton = new Button { Text = "העבר לאזור ההתראות", AutoSize = true };
        hideButton.Click += (_, _) => HideToTray();

        footer.Controls.Add(_startWithWindowsCheckBox);
        footer.Controls.Add(_closeSelectedButton);
        footer.Controls.Add(taskManagerButton);
        footer.Controls.Add(hideButton);

        var processesTab = new TabPage("תהליכים");
        processesTab.Controls.Add(_processList);

        var startupTab = new TabPage("תוכנות הפעלה");
        var startupToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 4)
        };
        var startupInfo = new Label
        {
            Text = "הרשימה לקריאה בלבד. השפעת תוכנות ההפעלה עדיין לא נמדדה.",
            AutoSize = true,
            Margin = new Padding(12, 7, 12, 0),
            ForeColor = SystemColors.GrayText
        };
        var refreshStartupButton = new Button { Text = "רענן רשימה", AutoSize = true };
        refreshStartupButton.Click += (_, _) => RefreshStartupList();
        var openStartupSettingsButton = new Button { Text = "פתח הגדרות Windows", AutoSize = true };
        openStartupSettingsButton.Click += (_, _) => OpenStartupSettings();
        startupToolbar.Controls.Add(startupInfo);
        startupToolbar.Controls.Add(refreshStartupButton);
        startupToolbar.Controls.Add(openStartupSettingsButton);
        startupTab.Controls.Add(_startupList);
        startupTab.Controls.Add(startupToolbar);

        var diagnosticsTab = new TabPage("אבחון");
        diagnosticsTab.Controls.Add(_diagnosticsView);

        _mainTabs.Dock = DockStyle.Fill;
        _mainTabs.TabPages.Add(diagnosticsTab);
        _mainTabs.TabPages.Add(processesTab);
        _mainTabs.TabPages.Add(startupTab);
        _mainTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_mainTabs.SelectedTab == startupTab &&
                DateTimeOffset.Now - _lastStartupRefresh > TimeSpan.FromMinutes(5))
            {
                RefreshStartupList();
            }
        };

        Controls.Add(_mainTabs);
        Controls.Add(footer);
        Controls.Add(metrics);
        Controls.Add(header);
    }

    private static Control CreateMetric(string caption, Label valueLabel)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), Margin = new Padding(3) };
        var title = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText
        };
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        valueLabel.TextAlign = ContentAlignment.MiddleRight;
        valueLabel.Text = "—";
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(title);
        return panel;
    }

    private void RefreshAll()
    {
        RefreshSnapshot();

        if (_mainTabs.SelectedTab?.Text == "תוכנות הפעלה")
        {
            RefreshStartupList();
        }
    }

    private void RefreshStartupList()
    {
        try
        {
            IReadOnlyList<StartupEntry> entries = _startupInventoryProvider.ReadAll();
            _startupList.BeginUpdate();
            try
            {
                _startupList.Items.Clear();
                foreach (StartupEntry entry in entries)
                {
                    var item = new ListViewItem(entry.Name);
                    item.SubItems.Add(entry.Scope);
                    item.SubItems.Add(entry.Source);
                    item.SubItems.Add(entry.TargetStatus);
                    item.SubItems.Add(entry.Command);
                    _startupList.Items.Add(item);
                }
            }
            finally
            {
                _startupList.EndUpdate();
            }

            _lastStartupRefresh = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"לא ניתן לקרוא את רשימת תוכנות ההפעלה: {ex.Message}",
                "RAM Sentinel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startup") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"לא ניתן לפתוח את הגדרות ההפעלה של Windows: {ex.Message}",
                "RAM Sentinel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RefreshSnapshot()
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            MemorySnapshot snapshot = _memoryProvider.Read();
            DiskMetricsSnapshot disk = _diskMetricsProvider.Read();

            bool diagnosticSampleDue =
                _lastDiagnosticSampleAt == DateTimeOffset.MinValue ||
                now - _lastDiagnosticSampleAt >= DiagnosticSampleInterval;

            if (diagnosticSampleDue)
            {
                try
                {
                    _latestCpuPercent = _cpuUsageProvider.ReadPercent();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    _latestCpuPercent = null;
                }

                if (_lastProcessRefresh == DateTimeOffset.MinValue ||
                    now - _lastProcessRefresh >= DiagnosticSampleInterval)
                {
                    try
                    {
                        _lastProcessSamples = RefreshProcessList();
                    }
                    catch (Exception ex) when (
                        ex is InvalidOperationException or
                        System.ComponentModel.Win32Exception or
                        NotSupportedException)
                    {
                        _lastProcessSamples = Array.Empty<ProcessPerformanceSample>();
                    }

                    _lastProcessRefresh = now;
                }

                PagingMetricsSnapshot paging = _pagingMetricsProvider.Read();

                var sample = new SystemPerformanceSample(
                    CapturedAt: now,
                    Memory: snapshot,
                    CpuPercent: _latestCpuPercent,
                    Disk: disk,
                    Processes: _lastProcessSamples,
                    Paging: paging);

                if (_diagnosticHistory.Add(sample))
                {
                    _lastDiagnosticSampleAt = now;
                    DiagnosticReport report = _diagnosticsEngine.Evaluate(
                        _diagnosticHistory.GetSnapshot(),
                        now);

                    _diagnosticsView.Render(report);

                    _statusLabel.Text = report.IsCollecting
                        ? "ניטור פעיל · אוספים מדידות לאורך זמן"
                        : report.PrimaryFinding is null
                            ? "ניטור פעיל · לא זוהה צוואר בקבוק מתמשך"
                            : $"ניטור פעיל · {report.Findings.Count} ממצאים לבדיקה";
                }
            }

            _cpuLabel.Text = _latestCpuPercent.HasValue
                ? $"{_latestCpuPercent.Value:0}%"
                : "לא זמין";

            _diskLabel.Text = disk.ReadLatencyMilliseconds.HasValue || disk.WriteLatencyMilliseconds.HasValue
                ? $"R {FormatMilliseconds(disk.ReadLatencyMilliseconds)} / W {FormatMilliseconds(disk.WriteLatencyMilliseconds)}"
                : "לא זמין";

            _systemDriveSpaceLabel.Text = disk.SystemDriveFreeBytes.HasValue && disk.SystemDriveTotalBytes.HasValue
                ? $"{FormatBytes(disk.SystemDriveFreeBytes.Value)} פנוי"
                : "לא זמין";

            _totalLabel.Text = FormatBytes(snapshot.TotalPhysicalBytes);
            _usedLabel.Text = FormatBytes(snapshot.UsedPhysicalBytes);
            _availableLabel.Text = $"{FormatBytes(snapshot.AvailablePhysicalBytes)} ({snapshot.AvailablePercent:0}%)";
            _cacheLabel.Text = FormatBytes(snapshot.SystemCacheBytes);
            _commitLabel.Text = snapshot.CommitLimitBytes > 0
                ? $"{FormatBytes(snapshot.CommitUsedBytes)} / {FormatBytes(snapshot.CommitLimitBytes)}"
                : "לא זמין";

            _notifyIcon.Text =
                $"RAM {snapshot.MemoryLoadPercent}% | CPU {(_latestCpuPercent.HasValue ? $"{_latestCpuPercent.Value:0}%" : "...")}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"לא ניתן לקרוא נתוני ביצועים: {ex.Message}";
        }
    }

    private IReadOnlyList<ProcessPerformanceSample> RefreshProcessList()
    {
        DateTimeOffset sampledAt = DateTimeOffset.UtcNow;
        var rows = new List<ProcessRow>();
        var activeProcessIds = new HashSet<int>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    int processId = process.Id;
                    activeProcessIds.Add(processId);
                    string processName = process.ProcessName;
                    long workingSet = process.WorkingSet64;
                    long privateBytes = process.PrivateMemorySize64;

                    DateTime? startedAtUtc = null;
                    double? cpuPercent = null;
                    try
                    {
                        startedAtUtc = process.StartTime.ToUniversalTime();
                        TimeSpan totalCpuTime = process.TotalProcessorTime;

                        if (_processCpuSamples.TryGetValue(processId, out ProcessCpuSample? previous) &&
                            previous is not null &&
                            previous.StartTimeUtc == startedAtUtc.Value)
                        {
                            cpuPercent = CpuUsageCalculator.Calculate(
                                previous.TotalCpuTime,
                                totalCpuTime,
                                sampledAt - previous.SampledAt,
                                _logicalProcessorCount);
                        }

                        _processCpuSamples[processId] = new ProcessCpuSample(
                            startedAtUtc.Value,
                            totalCpuTime,
                            sampledAt);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        // Some protected processes do not expose timing information.
                    }

                    rows.Add(new ProcessRow(
                        processId,
                        processName,
                        workingSet,
                        privateBytes,
                        cpuPercent,
                        startedAtUtc));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // A process can exit or become inaccessible during sampling.
                }
            }
        }

        // Remove samples for exited processes so the dictionary remains bounded.
        foreach (int processId in _processCpuSamples.Keys.Where(id => !activeProcessIds.Contains(id)).ToArray())
        {
            _processCpuSamples.Remove(processId);
        }

        // Show both memory-heavy and CPU-heavy processes. A small but busy process
        // must not disappear simply because its RAM use is modest.
        List<ProcessRow> visibleRows = rows
            .OrderByDescending(x => x.WorkingSetBytes)
            .Take(15)
            .Concat(rows.OrderByDescending(x => x.CpuPercent ?? 0d).Take(10))
            .GroupBy(x => x.ProcessId)
            .Select(group => group.First())
            .OrderByDescending(x => x.CpuPercent ?? 0d)
            .ThenByDescending(x => x.WorkingSetBytes)
            .Take(20)
            .ToList();

        _processList.BeginUpdate();
        try
        {
            _processList.Items.Clear();
            foreach (ProcessRow row in visibleRows)
            {
                var item = new ListViewItem(row.Name) { Tag = row };
                item.SubItems.Add(row.ProcessId.ToString());
                item.SubItems.Add(row.CpuPercent.HasValue ? $"{row.CpuPercent.Value:0.0}%" : "מודד...");
                item.SubItems.Add(FormatBytes((ulong)Math.Max(0, row.WorkingSetBytes)));
                item.SubItems.Add(FormatBytes((ulong)Math.Max(0, row.PrivateBytes)));
                _processList.Items.Add(item);
            }
        }
        finally
        {
            _processList.EndUpdate();
        }

        _closeSelectedButton.Enabled = _processList.SelectedItems.Count == 1;

        return rows.Select(row => new ProcessPerformanceSample(
            ProcessId: row.ProcessId,
            Name: row.Name,
            CpuPercent: row.CpuPercent,
            WorkingSetBytes: (ulong)Math.Max(0, row.WorkingSetBytes),
            PrivateBytes: (ulong)Math.Max(0, row.PrivateBytes),
            StartedAtUtc: row.StartTimeUtc)).ToArray();
    }

    private void CloseSelectedApplication()
    {
        if (_processList.SelectedItems.Count != 1 ||
            _processList.SelectedItems[0].Tag is not ProcessRow row)
        {
            return;
        }

        if (row.ProcessId == Environment.ProcessId ||
            ProtectedProcessNames.Contains(row.Name))
        {
            MessageBox.Show(
                this,
                "RAM Sentinel לא מאפשרת לבקש סגירה של תהליך מערכת, מעטפת Windows או של התוכנה עצמה.",
                "פעולה חסומה",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (row.StartTimeUtc is null)
        {
            MessageBox.Show(
                this,
                "לא ניתן לאמת את זהות התהליך ואת זמן ההפעלה שלו. לא תישלח בקשת סגירה.",
                "לא ניתן לבצע פעולה בטוחה",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"לבקש מהתוכנה הבאה להיסגר בדרך הרגילה?\n\n{row.Name} (PID {row.ProcessId})\n\nהתוכנה עשויה להציג בקשה לשמירת מסמכים. RAM Sentinel לא תסיים את התהליך בכוח.",
            "אישור סגירת תוכנה",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(row.ProcessId);

            if (!string.Equals(process.ProcessName, row.Name, StringComparison.OrdinalIgnoreCase) ||
                process.StartTime.ToUniversalTime() != row.StartTimeUtc.Value)
            {
                MessageBox.Show(
                    this,
                    "התהליך השתנה מאז עדכון הרשימה. רענן את הנתונים ונסה שוב.",
                    "הפעולה בוטלה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                RefreshAll();
                return;
            }

            if (process.MainWindowHandle == IntPtr.Zero)
            {
                MessageBox.Show(
                    this,
                    "לתהליך אין חלון ראשי שאפשר לבקש ממנו להיסגר בצורה רגילה. לא בוצעה פעולה.",
                    "לא ניתן לסגור בצורה בטוחה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (process.CloseMainWindow())
            {
                _statusLabel.Text = $"נשלחה בקשת סגירה רגילה ל-{row.Name}. ייתכן שתופיע בקשה לשמירת עבודה.";
            }
            else
            {
                _statusLabel.Text = $"לא ניתן לשלוח בקשת סגירה ל-{row.Name}. התהליך נשאר פעיל.";
            }

            _timer.Stop();
            _timer.Start();
        }
        catch (ArgumentException)
        {
            _statusLabel.Text = "התהליך כבר אינו פועל. הרשימה תתרענן.";
            RefreshAll();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"לא ניתן לבקש סגירה רגילה של התוכנה: {ex.Message}",
                "RAM Sentinel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
        _timer.Interval = 10_000;
        RefreshAll();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        // Keep background monitoring deliberately quiet on slower PCs.
        _timer.Interval = 30_000;
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _timer.Stop();
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _diskMetricsProvider.Dispose();
        _pagingMetricsProvider.Dispose();
        base.OnFormClosing(e);
    }

    private static string FormatMilliseconds(double? milliseconds) =>
        milliseconds.HasValue ? $"{milliseconds.Value:0.0}ms" : "—";

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

    private sealed record ProcessCpuSample(
        DateTime StartTimeUtc,
        TimeSpan TotalCpuTime,
        DateTimeOffset SampledAt);

    private sealed record ProcessRow(
        int ProcessId,
        string Name,
        long WorkingSetBytes,
        long PrivateBytes,
        double? CpuPercent,
        DateTime? StartTimeUtc);
}
