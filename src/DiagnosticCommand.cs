using System.Globalization;
using System.Text;

namespace MayaXBattery;

internal static class DiagnosticCommand
{
    internal static string ExecutableDirectory()
    {
        string executable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(executable)
            ? Path.GetDirectoryName(executable)!
            : AppContext.BaseDirectory;
    }

    internal static int Run(string executableDirectory, Func<DiagnosticSnapshot> capture,
        TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string name = $"MayaX-diagnostics-{stamp}-{Guid.NewGuid():N}.zip";
            string path = Path.Combine(executableDirectory, name);
            Diagnostics.Export(path, capture());
            TryWrite(stdout, $"Diagnostic report saved: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            string reason = ex switch
            {
                UnauthorizedAccessException => "Access denied. Move the executable to a writable folder and try again.",
                DirectoryNotFoundException => "The executable directory is unavailable. Move the executable to a writable folder and try again.",
                PathTooLongException => "The executable path is too long. Move it to a shorter writable folder and try again.",
                IOException => "Could not write the report. Check free space and access to the executable folder, then try again.",
                _ => "Diagnostic capture or export failed. Try again; if it persists, report this error to the maintainer."
            };
            string message = $"Diagnostics failed: {reason} (Exit code 1.)";
            TryWrite(stderr, message);
            TryWriteErrorFile(executableDirectory, message);
            return 1;
        }
    }

    static void TryWrite(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    static void TryWriteErrorFile(string directory, string message)
    {
        try
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory,
                $"MayaX-diagnostics-error-{stamp}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, message + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // A read-only executable directory also prevents an error file.
        }
    }
}