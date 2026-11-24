using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace RamSentinel.App.Platform;

internal sealed record StartupEntry(
    string Name,
    string Source,
    string Scope,
    string Command,
    string TargetStatus);

/// <summary>
/// Read-only inventory of common Windows startup locations.
/// This class deliberately contains no methods that enable, disable or delete entries.
/// </summary>
internal sealed class StartupInventoryProvider
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly Regex ExecutablePattern = new(
        @"^(.+?\.(?:exe|com|bat|cmd|ps1|vbs|js))(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<StartupEntry> ReadAll()
    {
        var entries = new List<StartupEntry>();

        ReadRunKey(entries, RegistryHive.CurrentUser, "HKCU", "הפעלה אוטומטית - המשתמש הנוכחי");
        ReadRunKey(entries, RegistryHive.LocalMachine, "HKLM", "הפעלה אוטומטית - כל המשתמשים");

        ReadStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "תיקיית Startup", "המשתמש הנוכחי");
        ReadStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "תיקיית Startup משותפת", "כל המשתמשים");

        return entries
            .OrderBy(x => x.Scope, StringComparer.CurrentCulture)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void ReadRunKey(
        ICollection<StartupEntry> destination,
        RegistryHive hive,
        string hiveLabel,
        string sourceLabel)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(RunKeyPath, writable: false);
            if (key is null)
            {
                return;
            }

            foreach (string valueName in key.GetValueNames())
            {
                try
                {
                    object? rawValue = key.GetValue(
                        valueName,
                        defaultValue: null,
                        options: RegistryValueOptions.DoNotExpandEnvironmentNames);

                    string command = rawValue switch
                    {
                        string text => text,
                        string[] values => string.Join(" ", values),
                        null => string.Empty,
                        _ => Convert.ToString(rawValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                    };

                    string? target = ExtractTargetPath(command);
                    string targetStatus = target is null
                        ? "לא זוהה"
                        : File.Exists(target) ? "קיים" : "לא נמצא";

                    RegistryValueKind kind = key.GetValueKind(valueName);
                    destination.Add(new StartupEntry(
                        valueName,
                        $"{hiveLabel}\\{RunKeyPath} ({kind})",
                        hiveLabel,
                        command,
                        targetStatus));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                {
                    // Skip a single unreadable value; keep the rest of the inventory.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            // Registry access is read-only and best-effort. The rest of the inventory can still be shown.
        }
    }

    private static void ReadStartupFolder(
        ICollection<StartupEntry> destination,
        string folderPath,
        string sourceLabel,
        string scope)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string extension = Path.GetExtension(filePath);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    destination.Add(new StartupEntry(
                        Path.GetFileNameWithoutExtension(filePath),
                        filePath,
                        scope,
                        filePath,
                        File.Exists(filePath) ? "קיים" : "לא נמצא"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                {
                    // Skip a single inaccessible entry.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // An inaccessible folder does not prevent the other sources being listed.
        }
    }

    private static string? ExtractTargetPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        string? candidate = null;

        if (expanded.StartsWith('"'))
        {
            int closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                candidate = expanded[1..closingQuote];
            }
        }
        else
        {
            Match match = ExecutablePattern.Match(expanded);
            if (match.Success)
            {
                candidate = match.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
