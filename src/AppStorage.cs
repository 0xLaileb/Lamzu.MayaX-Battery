namespace MayaXBattery;

internal static class AppStorage
{
    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MayaX-Battery");

    internal static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    internal static string HistoryPath => Path.Combine(DirectoryPath, "history.json");

    internal static void MigrateLegacyFiles() => MigrateLegacyFiles(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static void MigrateLegacyFiles(string localDataDirectory)
    {
        try
        {
            var oldDirectory = Path.Combine(localDataDirectory, "MouseBattery");
            if (!Directory.Exists(oldDirectory)) return;

            var newDirectory = Path.Combine(localDataDirectory, "MayaX-Battery");
            foreach (var fileName in new[] { "settings.json", "history.json" })
            {
                var source = Path.Combine(oldDirectory, fileName);
                var destination = Path.Combine(newDirectory, fileName);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                try
                {
                    Directory.CreateDirectory(newDirectory);
                    File.Copy(source, destination, overwrite: false);
                }
                catch (Exception ex) when (IsStorageError(ex)) { }
            }
        }
        catch (Exception ex) when (IsStorageError(ex)) { }
    }

    static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or NotSupportedException or ArgumentException;
}