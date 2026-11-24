using System.Globalization;
using RamSentinel.Core;

namespace RamSentinel.App.UI;

/// <summary>
/// A compact, read-only summary for ordinary users. It shows one main finding
/// and no more than two secondary findings, without exposing raw telemetry by default.
/// </summary>
internal sealed class DiagnosticsSummaryView : UserControl
{
    private readonly FlowLayoutPanel _content = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        RightToLeft = RightToLeft.Yes,
        Padding = new Padding(20)
    };

    private readonly List<Label> _wrappingLabels = [];
    private readonly Label _statusLabel;
    private readonly Label _updatedLabel;
    private readonly Label _mainTitleLabel;
    private readonly Label _summaryLabel;
    private readonly Label _evidenceLabel;
    private readonly Label _recommendationLabel;
    private readonly Label _secondaryTitleLabel;
    private readonly Label _secondaryLabel;
    private readonly Label _limitationsLabel;

    public DiagnosticsSummaryView()
    {
        RightToLeft = RightToLeft.Yes;
        Controls.Add(_content);

        _statusLabel = AddLabel(
            "מתחילים לאסוף נתונים. אין צורך לבצע פעולה כרגע.",
            new Font("Segoe UI", 10F, FontStyle.Bold),
            SystemColors.ControlText,
            spacingAfter: 6);

        _updatedLabel = AddLabel(
            "הבדיקה מתבצעת מקומית במחשב.",
            new Font("Segoe UI", 8.5F),
            SystemColors.GrayText,
            spacingAfter: 20);

        AddLabel(
            "הגורם העיקרי שכדאי לבדוק",
            new Font("Segoe UI", 11F, FontStyle.Bold),
            SystemColors.ControlText,
            spacingAfter: 8);

        _mainTitleLabel = AddLabel(
            "אוספים מדידות ראשוניות",
            new Font("Segoe UI", 14F, FontStyle.Bold),
            SystemColors.ControlText,
            spacingAfter: 8);

        _summaryLabel = AddLabel(
            "RAM Sentinel בודקת את הזיכרון, המעבד והדיסק לאורך זמן כדי לא לבלבל בין עומס רגעי לבעיה אמיתית.",
            new Font("Segoe UI", 9.5F),
            SystemColors.ControlText,
            spacingAfter: 10);

        _evidenceLabel = AddLabel(
            "",
            new Font("Segoe UI", 9F),
            SystemColors.GrayText,
            spacingAfter: 10);

        _recommendationLabel = AddLabel(
            "בשלב זה לא מוצע לבצע שינוי במחשב.",
            new Font("Segoe UI", 9.5F, FontStyle.Bold),
            SystemColors.ControlText,
            spacingAfter: 24);

        _secondaryTitleLabel = AddLabel(
            "ממצאים נוספים",
            new Font("Segoe UI", 10F, FontStyle.Bold),
            SystemColors.ControlText,
            spacingAfter: 8);

        _secondaryLabel = AddLabel(
            "אין עדיין ממצאים נוספים.",
            new Font("Segoe UI", 9F),
            SystemColors.ControlText,
            spacingAfter: 20);

        _limitationsLabel = AddLabel(
            "השפעת תוכנות ההפעלה עדיין לא נמדדה. לא מתבצעים ניקוי זיכרון, סגירת תוכנות או שינויי Windows אוטומטיים.",
            new Font("Segoe UI", 8.5F),
            SystemColors.GrayText,
            spacingAfter: 4);

        Resize += (_, _) => UpdateWrappingWidths();
        UpdateWrappingWidths();
    }

    public void Render(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        _statusLabel.Text = report.StatusMessage;
        _updatedLabel.Text =
            $"עודכן: {report.GeneratedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)}" +
            $"  ·  {report.SampleCount} מדידות" +
            $"  ·  חלון מדידה: {FormatDuration(report.ObservedWindow)}" +
            "  ·  היסטוריה נשמרת בזיכרון בלבד";

        PerformanceFinding? primary = report.PrimaryFinding;
        if (primary is null)
        {
            _mainTitleLabel.Text = report.IsCollecting
                ? "אוספים מדידות ראשוניות"
                : "לא זוהה כרגע צוואר בקבוק מתמשך";

            _summaryLabel.Text = report.IsCollecting
                ? "נדרשות עוד מדידות כדי לקבוע אם יש גורם שמאט את המחשב. אין צורך לבצע פעולה כרגע."
                : "במדדים הזמינים לא נמצאה כרגע בעיה מתמשכת שמצדיקה שינוי במחשב.";

            _evidenceLabel.Text = "";
            _recommendationLabel.Text = "המלצה: אין צורך לשנות הגדרות או לסגור תוכנות על סמך מדידה בודדת.";
        }
        else
        {
            _mainTitleLabel.Text = report.IsCollecting
                ? $"ממצא ראשוני לבדיקה: {primary.Title}"
                : primary.Title;

            _summaryLabel.Text = primary.Summary;

            string evidence = string.Join(
                Environment.NewLine,
                primary.Evidence.Take(4).Select(item =>
                    $"• {item.Metric}: {item.Value} {item.Unit}"));

            _evidenceLabel.Text =
                $"רמת ביטחון: {FormatConfidence(primary.Confidence)}" +
                (string.IsNullOrWhiteSpace(evidence) ? "" : Environment.NewLine + evidence);

            _recommendationLabel.Text = $"מה כדאי לעשות: {primary.Recommendation}";
        }

        PerformanceFinding[] secondary = report.SecondaryFindings.ToArray();
        _secondaryTitleLabel.Visible = secondary.Length > 0;
        _secondaryLabel.Visible = secondary.Length > 0;
        _secondaryLabel.Text = secondary.Length == 0
            ? "אין ממצאים נוספים."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                secondary.Select(item =>
                    $"{item.Title} ({FormatConfidence(item.Confidence)}): {item.Summary}"));

        bool startupUnmeasured = report.NotMeasuredCategories.Contains(DiagnosticCategory.Startup);
        _limitationsLabel.Text =
            (startupUnmeasured ? "השפעת תוכנות ההפעלה: לא נמדדה עדיין. " : "") +
            "האבחון אינו משנה הגדרות, אינו סוגר תוכנות ואינו מוחק קבצים.";
        UpdateWrappingWidths();
    }

    private Label AddLabel(string text, Font font, Color color, int spacingAfter)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = font,
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleRight,
            RightToLeft = RightToLeft.Yes,
            Margin = new Padding(4, 2, 4, spacingAfter)
        };

        _wrappingLabels.Add(label);
        _content.Controls.Add(label);
        return label;
    }

    private void UpdateWrappingWidths()
    {
        int availableWidth = Math.Max(320, _content.ClientSize.Width - 55);
        foreach (Label label in _wrappingLabels)
        {
            label.MaximumSize = new Size(availableWidth, 0);
        }
    }

    private static string FormatConfidence(ConfidenceLevel confidence) =>
        confidence switch
        {
            ConfidenceLevel.High => "גבוהה",
            ConfidenceLevel.Medium => "בינונית",
            ConfidenceLevel.Low => "נמוכה",
            _ => "לא ידועה"
        };

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:0} דקות"
            : $"{Math.Max(0, duration.TotalSeconds):0} שניות";
}
