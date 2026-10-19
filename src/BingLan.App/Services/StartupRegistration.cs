using Microsoft.Win32;

namespace BingLan.App.Services;

/// <summary>
/// The app's single per-user startup entry under the current user's Run key. No
/// administrator rights, scheduled task or second resident process are involved.
/// </summary>
internal sealed class StartupRegistration(string executablePath, bool canModify)
{
    internal const string ValueName = "BingLanDock";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _command = $"\"{executablePath}\"";

    internal bool CanModify { get; } = canModify;

    internal bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value
            && string.Equals(value, _command, StringComparison.OrdinalIgnoreCase);
    }

    internal void SetEnabled(bool enabled)
    {
        if (!CanModify)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, _command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
