using Microsoft.Win32;

namespace RamSentinel.App.Platform;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RamSentinel";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("לא ניתן לפתוח את הגדרות ההפעלה של המשתמש.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("לא ניתן לזהות את קובץ ההפעלה.");
        }

        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("יש להפעיל את הגדרת ההפעלה האוטומטית מתוך קובץ ה-EXE שפורסם, ולא מתוך dotnet run.");
        }

        key.SetValue(ValueName, $"\"{executablePath}\"");
    }
}
