using Microsoft.Win32;

namespace MayaXBattery;

/// <summary>
/// Per-user "Start with Windows" entry. The command always carries --tray, so sign-in leaves the
/// app in the notification area instead of opening the window.
/// </summary>
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "MayaX-Battery";

    internal static string Command(string executable) => $"\"{executable}\" --tray";

    /// <summary>True when a stored command starts this executable in the tray.</summary>
    internal static bool Matches(string command, string executable) =>
        !string.IsNullOrWhiteSpace(command) && !string.IsNullOrWhiteSpace(executable) &&
        string.Equals(command.Trim(), Command(executable), StringComparison.OrdinalIgnoreCase);

    internal static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return Matches(key?.GetValue(ValueName) as string, Environment.ProcessPath);
        }
        catch (Exception ex) when (IsRegistryError(ex)) { return false; }
    }

    internal static bool Set(bool enabled)
    {
        try
        {
            string executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return false;
            if (enabled && !IsPermanentLocation(executable, Path.GetTempPath())) return false;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (IsRegistryError(ex)) { return false; }
    }

    /// <summary>A copy started from inside a ZIP runs from the temp folder and would not survive a restart.</summary>
    internal static bool IsPermanentLocation(string executable, string tempDirectory)
    {
        try
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempDirectory)) + Path.DirectorySeparatorChar;
            return !Path.GetFullPath(executable).StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    static bool IsRegistryError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ObjectDisposedException;
}