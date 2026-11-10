using System.Diagnostics;
using System.Reflection;

namespace MayaXBattery;

internal static class AppInfo
{
    internal const string ReleasesUrl = "https://github.com/0xLaileb/Lamzu.MayaX-Battery/releases";

    /// <summary>Display version without the source revision suffix, for example "1.2.0".</summary>
    internal static string Version { get; } = DisplayVersion(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3));

    internal static string DisplayVersion(string informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "?";
        int suffix = informational.IndexOf('+');
        return (suffix > 0 ? informational[..suffix] : informational).Trim();
    }

    internal static void OpenReleases()
    {
        try { Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}