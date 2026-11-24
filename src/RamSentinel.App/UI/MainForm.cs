using System.Diagnostics;
using RamSentinel.App.Platform;
using RamSentinel.Core;

namespace RamSentinel.App.UI;

internal sealed class MainForm : Form
{
    private readonly WindowsMemoryProvider _memoryProvider = new();
    private readonly MemoryPressureEvaluator _pressureEvaluator = new(TimeSpan.FromSeconds(30));
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10_000 };
    private readonly NotifyIcon _notifyIcon;
    private readonly Label _statusLabel = new();
    private readonly Label _totalLabel = new();
    private readonly Label _usedLabel = new();
    private readonly Label _availableLabel = new();
    private readonly Label _cacheLabel = new();
    private readonly Label _commitLabel = new();
    private readonly Label _explanationLabel = new();
    private readonly ListView _processList = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly Button _closeSelectedButton = new();
    private DateTimeOffset _lastProcessRefresh = DateTimeOffset.MinValue;
    private MemoryPressureLevel _lastPressureLevel = MemoryPressureLevel.Normal;
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
        trayMenu.Items.Add("רענן נתונים", null, (_, _) => RefreshAll());
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
            HideToTray();
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
        _statusLabel.Text = "ממתין לקריאת נתוני זיכרון...";
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;

        _explanationLabel.Dock = DockStyle.Top;
        _explanationLabel.Height = 24;
        _explanationLabel.Text = "Windows מנהלת את המטמון בעצמה. RAM Sentinel מתערבת רק כשיש סימנים ללחץ אמיתי.";
        _explanationLabel.ForeColor = SystemColors.GrayText;
        _explanationLabel.TextAlign = ContentAlignment.MiddleRight;

        header.Controls.Add(_explanationLabel);
        header.Controls.Add(_statusLabel);
        header.Controls.Add(title);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 82,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(8, 3, 8, 3)
        };
        for (int i = 0; i < 5; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        }

        metrics.Controls.Add(CreateMetric("סה״כ RAM", _totalLabel), 0, 0);
        metrics.Controls.Add(CreateMetric("בשימוש", _usedLabel), 1, 0);
        metrics.Controls.Add(CreateMetric("זמין ל-Windows", _availableLabel), 2, 0);
        metrics.Controls.Add(CreateMetric("Cache מערכת", _cacheLabel), 3, 0);
        metrics.Controls.Add(CreateMetric("Commit", _commitLabel), 4, 0);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.GridLines = true;
        _processList.HideSelection = false;
        _processList.MultiSelect = false;
        _processList.Columns.Add("תהליך", 250);
        _processList.Columns.Add("PID", 75);
        _processList.Columns.Add("RAM פיזי", 135);
        _processList.Columns.Add("זיכרון פרטי", 135);

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

        Controls.Add(_processList);
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
        RefreshProcessList();
        _lastProcessRefresh = DateTimeOffset.Now;
    }

    private void RefreshSnapshot()
    {
        try
        {
            MemorySnapshot snapshot = _memoryProvider.Read();

            _totalLabel.Text = FormatBytes(snapshot.TotalPhysicalBytes);
            _usedLabel.Text = FormatBytes(snapshot.UsedPhysicalBytes);
            _availableLabel.Text = $"{FormatBytes(snapshot.AvailablePhysicalBytes)} ({snapshot.AvailablePercent:0}%)";
            _cacheLabel.Text = FormatBytes(snapshot.SystemCacheBytes);
            _commitLabel.Text = snapshot.CommitLimitBytes > 0
                ? $"{FormatBytes(snapshot.CommitUsedBytes)} / {FormatBytes(snapshot.CommitLimitBytes)}"
                : "לא זמין";

            MemoryPressureLevel level = _pressureEvaluator.Evaluate(snapshot, snapshot.CapturedAt);
            _statusLabel.Text = level switch
            {
                MemoryPressureLevel.Normal =>
                    $"תקין · {snapshot.MemoryLoadPercent}% מה-RAM בשימוש · {snapshot.ProcessCount} תהליכים",
                MemoryPressureLevel.Warning =>
                    $"עומס זיכרון גבוה · {snapshot.MemoryLoadPercent}% · בודק אם מדובר במצב מתמשך",
                MemoryPressureLevel.High =>
                    $"זוהה לחץ זיכרון מתמשך · {snapshot.MemoryLoadPercent}% · בדוק את התהליכים הכבדים",
                _ => "מצב זיכרון לא ידוע"
            };

            if (level == MemoryPressureLevel.High && _lastPressureLevel != MemoryPressureLevel.High)
            {
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "RAM Sentinel זיהתה לחץ זיכרון",
                    "בדוק אילו תוכנות משתמשות בזיכרון רב. אפשר לבקש סגירה רגילה של תוכנת משתמש, בלי לסיים תהליכי מערכת.",
                    ToolTipIcon.Warning);
            }

            _lastPressureLevel = level;
            _notifyIcon.Text = $"RAM Sentinel | RAM {snapshot.MemoryLoadPercent}% | פנוי {snapshot.AvailablePercent:0}%";

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
        var rows = new List<ProcessRow>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    DateTime? startedAtUtc = null;
                    try
                    {
                        startedAtUtc = process.StartTime.ToUniversalTime();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        // Some protected processes do not expose their start time.
                    }

                    rows.Add(new ProcessRow(
                        process.Id,
                        process.ProcessName,
                        process.WorkingSet64,
                        process.PrivateMemorySize64,
                        startedAtUtc));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // A process can exit or become inaccessible during sampling.
                }
            }
        }

        _processList.BeginUpdate();
        try
        {
            _processList.Items.Clear();
            foreach (ProcessRow row in rows.OrderByDescending(x => x.WorkingSetBytes).Take(20))
            {
                var item = new ListViewItem(row.Name) { Tag = row };
                item.SubItems.Add(row.ProcessId.ToString());
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
        RefreshAll();
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

    private sealed record ProcessRow(
        int ProcessId,
        string Name,
        long WorkingSetBytes,
        long PrivateBytes,
        DateTime? StartTimeUtc);
}
