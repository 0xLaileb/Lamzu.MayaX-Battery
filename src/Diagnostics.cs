using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MayaXBattery;

internal sealed class DiagnosticIssue
{
    public string AnonymousId { get; init; } = "";
    public string Stage { get; init; } = "";
    public string Status { get; init; } = "";
    public int? Win32Error { get; init; }
    public int? NativeStatus { get; init; }
}

internal sealed class DiagnosticDevice
{
    public string AnonymousId { get; init; } = "";
    public string VendorId { get; init; } = "";
    public string ProductId { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Product { get; init; } = "";
    public string UsbVersionNumber { get; init; } = "";
    public string MouseFirmwareVersion { get; init; } = "unknown";
    public string ReceiverFirmwareVersion { get; init; } = "unknown";
    public string ReportIds { get; init; } = "not collected";
    public string UsagePage { get; init; } = "";
    public string Usage { get; init; } = "";
    public int InputLength { get; init; }
    public int OutputLength { get; init; }
    public int FeatureLength { get; init; }
    public bool CapabilitiesAvailable { get; init; } = true;
    public string ProfileHint { get; init; } = "";
    public string SelectionReason { get; init; } = "";
}

internal sealed class DiagnosticSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string AppVersion { get; init; } = "unknown";
    public DateTimeOffset CapturedUtc { get; init; }
    public string OperatingSystem { get; init; } = "";
    public string Architecture { get; init; } = "";
    public int TotalHidInterfaces { get; init; }
    public int InspectedCollections { get; init; }
    public IReadOnlyList<DiagnosticDevice> Devices { get; init; } = Array.Empty<DiagnosticDevice>();
    public IReadOnlyList<DiagnosticIssue> Issues { get; init; } = Array.Empty<DiagnosticIssue>();
    public IReadOnlyList<DiagnosticEvent> Events { get; init; } = Array.Empty<DiagnosticEvent>();
}

internal static class Diagnostics
{
    const int MaxItems = 128;
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static readonly JsonSerializerOptions LineOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Passive HID descriptor capture. No feature, input, output or firmware command is sent.</summary>
    internal static DiagnosticSnapshot Capture()
    {
        try
        {
            var enumeration = Hid.EnumerateForDiagnostics();
            return FromCollections(enumeration.Collections, enumeration.TotalInterfaces, enumeration.Issues,
                enumeration.InspectedCollections);
        }
        catch (Exception ex)
        {
            return FromCollections(Array.Empty<Hid.Collection>(), 0,
                [new DiagnosticIssue { Stage = "discovery", Status = $"exception-{ex.GetType().Name}" }]);
        }
    }

    /// <summary>Snapshot factory for checks. Collection paths are used only in memory.</summary>
    internal static DiagnosticSnapshot FromCollections(IEnumerable<Hid.Collection> collections,
        int totalHidInterfaces = 0, IEnumerable<DiagnosticIssue> issues = null, int? inspectedCollections = null)
    {
        ArgumentNullException.ThrowIfNull(collections);
        var devices = new List<DiagnosticDevice>();
        int eligible = 0, inspected = 0;
        foreach (var c in collections)
        {
            if (c is null) continue;
            bool candidate = c.Vid is 0x373E or 0x3554 ||
                c.Product?.Contains("MAYA", StringComparison.OrdinalIgnoreCase) == true ||
                c.Product?.Contains("LAMZU", StringComparison.OrdinalIgnoreCase) == true ||
                c.Manufacturer?.Contains("LAMZU", StringComparison.OrdinalIgnoreCase) == true;
            if (!candidate) continue;
            inspected++;
            if (devices.Count == MaxItems) continue;
            string reason = c.Vid switch
            {
                0x3554 when c.Pid is 0xF401 or 0xF402 or 0xF403 => "bootloader; never poll",
                0x3554 => "different vendor protocol; current poll ignores VID 3554",
                _ when !c.CapsAvailable => "capabilities unavailable; current poll eligibility unknown",
                0x373E when c.FeatureLen != 65 => "current poll rejects feature report length",
                0x373E when c.UsagePage < 0xFF00 => "current poll rejects usage page",
                0x373E when eligible++ == 0 => "first eligible in this snapshot for current poll",
                0x373E => "eligible in this snapshot; current poll uses first eligible",
                _ => "current poll rejects vendor ID"
            };
            devices.Add(new DiagnosticDevice
            {
                AnonymousId = DiagnosticLog.AnonymousId(c.Path),
                VendorId = c.Vid.ToString("X4"), ProductId = c.Pid.ToString("X4"),
                Manufacturer = SafeText(c.Manufacturer), Product = SafeText(c.Product),
                UsbVersionNumber = c.VersionNumber.ToString("X4"),
                UsagePage = c.UsagePage.ToString("X4"), Usage = c.Usage.ToString("X4"),
                InputLength = c.InputLen, OutputLength = c.OutputLen, FeatureLength = c.FeatureLen,
                CapabilitiesAvailable = c.CapsAvailable,
                ProfileHint = Profile(c.Vid, c.Pid), SelectionReason = reason
            });
        }
        var issueList = (issues ?? []).Take(MaxItems).ToArray();
        if (inspected > MaxItems)
            issueList = [.. issueList.Take(MaxItems - 1), new DiagnosticIssue { Stage = "snapshot", Status = "candidate-limit-reached" }];
        var version = typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "unknown";
        return new DiagnosticSnapshot
        {
            AppVersion = version,
            CapturedUtc = DateTimeOffset.UtcNow,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = $"OS {RuntimeInformation.OSArchitecture}; process {RuntimeInformation.ProcessArchitecture}",
            TotalHidInterfaces = Math.Max(0, totalHidInterfaces),
            InspectedCollections = Math.Max(0, inspectedCollections ?? inspected),
            Devices = devices,
            Issues = issueList,
            Events = DiagnosticLog.Snapshot()
        };
    }

    static string Profile(ushort vid, ushort pid) => (vid, pid) switch
    {
        (0x3554, 0xF50F) => "Nordic Maya mouse candidate (unverified on this device)",
        (0x3554, 0xF50D) => "Nordic 1K receiver candidate (paired mouse unknown)",
        (0x3554, 0xF510) => "Nordic 4K receiver candidate (paired mouse unknown)",
        (0x3554, 0xF403) => "Nordic Maya mouse bootloader; never poll",
        (0x3554, 0xF402) => "Nordic 1K receiver bootloader; never poll",
        (0x3554, 0xF401) => "Nordic 4K receiver bootloader; never poll",
        (0x373E, _) => "Compx Maya X protocol candidate (model unverified)",
        (0x3554, _) => "unknown 3554 device; no protocol assumption",
        _ => "name match only; no protocol assumption"
    };

    /// <summary>Writes a bounded diagnostic ZIP beside a temporary file, then replaces the target.</summary>
    internal static void Export(string path, DiagnosticSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string temp = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var devices = (snapshot.Devices ?? []).Take(MaxItems).Select(SafeDevice).ToArray();
        var issues = (snapshot.Issues ?? []).Take(MaxItems).Select(SafeIssue).ToArray();
        var events = (snapshot.Events ?? []).TakeLast(MaxItems).Select(SafeEvent).ToArray();
        string summary = Summary(snapshot, devices, issues, events);
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    Write(archive, "summary.txt", summary);
                    Write(archive, "devices.json", JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        capturedUtc = snapshot.CapturedUtc.ToUniversalTime(),
                        appVersion = SafeVersion(snapshot.AppVersion),
                        operatingSystem = SafeText(snapshot.OperatingSystem),
                        architecture = SafeText(snapshot.Architecture),
                        passive = true,
                        newVendorCommands = false,
                        totalHidInterfaces = Math.Max(0, snapshot.TotalHidInterfaces),
                        inspectedCollections = Math.Max(0, snapshot.InspectedCollections),
                        devices,
                        issues
                    }, JsonOptions));
                    Write(archive, "events.jsonl", string.Concat(events.Select(e => JsonSerializer.Serialize(e, LineOptions) + "\n")));
                }
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void Write(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    static string Summary(DiagnosticSnapshot snapshot, DiagnosticDevice[] devices, DiagnosticIssue[] issues, DiagnosticEvent[] events)
    {
        var b = new StringBuilder();
        b.AppendLine("MayaX-Battery diagnostic snapshot, schema 1");
        b.AppendLine($"Captured UTC: {snapshot.CapturedUtc.ToUniversalTime():O}");
        b.AppendLine($"Application: {SafeVersion(snapshot.AppVersion)}");
        b.AppendLine($"Windows: {SafeText(snapshot.OperatingSystem)}; {SafeText(snapshot.Architecture)}");
        b.AppendLine("Passive descriptor enumeration: true; new vendor commands: false");
        b.AppendLine("Mouse firmware: unknown; receiver firmware: unknown. USB VersionNumber is a USB descriptor, not firmware evidence.");
        b.AppendLine("HID report IDs: not collected from descriptors; no probe commands are sent.");
        b.AppendLine($"HID interfaces found: {Math.Max(0, snapshot.TotalHidInterfaces)}; candidate collections inspected: {Math.Max(0, snapshot.InspectedCollections)}; exported: {devices.Length}");
        b.AppendLine($"Discovery issues: {issues.Length}; recent poll events: {events.Length}");
        bool incomplete = issues.Length != 0 || devices.Any(d => !d.CapabilitiesAvailable);
        bool eligible = devices.Any(d => d.SelectionReason.StartsWith("first eligible", StringComparison.Ordinal) ||
            d.SelectionReason.StartsWith("eligible in", StringComparison.Ordinal));
        if (incomplete)
            b.AppendLine("Discovery is incomplete: some interfaces or descriptors could not be inspected. Current poll eligibility may be unknown; review issues in devices.json.");
        if (devices.Length == 0)
            b.AppendLine("No LAMZU/Maya candidates were captured. Try direct USB and receiver separately, then export again.");
        else if (!eligible && !incomplete)
            b.AppendLine("No captured collection matches the current Maya X poll filter. Device descriptors in devices.json may guide compatibility work.");
        b.AppendLine("Paths, serial numbers, full HID frames and user files are excluded. A receiver ID does not identify the paired mouse.");
        b.AppendLine("Known 3554 Nordic IDs are hints from a vendor firmware package; live protocol support is unverified.");
        return b.ToString();
    }

    static DiagnosticDevice SafeDevice(DiagnosticDevice d) => new()
    {
        AnonymousId = SafeId(d.AnonymousId), VendorId = Hex4(d.VendorId), ProductId = Hex4(d.ProductId),
        Manufacturer = SafeText(d.Manufacturer), Product = SafeText(d.Product),
        UsbVersionNumber = Hex4(d.UsbVersionNumber),
        MouseFirmwareVersion = "unknown", ReceiverFirmwareVersion = "unknown",
        ReportIds = "not collected",
        UsagePage = Hex4(d.UsagePage), Usage = Hex4(d.Usage),
        InputLength = Math.Clamp(d.InputLength, 0, 65535),
        OutputLength = Math.Clamp(d.OutputLength, 0, 65535),
        FeatureLength = Math.Clamp(d.FeatureLength, 0, 65535),
        CapabilitiesAvailable = d.CapabilitiesAvailable,
        ProfileHint = SafeText(d.ProfileHint), SelectionReason = SafeText(d.SelectionReason)
    };

    static DiagnosticIssue SafeIssue(DiagnosticIssue i) => new()
    {
        AnonymousId = SafeId(i.AnonymousId), Stage = SafeText(i.Stage), Status = SafeText(i.Status),
        Win32Error = i.Win32Error, NativeStatus = i.NativeStatus
    };

    static DiagnosticEvent SafeEvent(DiagnosticEvent e) => new()
    {
        Utc = e.Utc.ToUniversalTime(), AnonymousId = SafeId(e.AnonymousId),
        Stage = SafeText(e.Stage), Status = SafeText(e.Status),
        ElapsedMs = Math.Clamp(e.ElapsedMs, 0, 60_000), Retry = Math.Clamp(e.Retry, 0, 4),
        ResponsePrefix = SafeBatteryPrefix(e.Stage, e.ResponsePrefix)
    };

    static string SafeBatteryPrefix(string stage, string value)
    {
        if (stage != "poll.parse" || value is null || !Regex.IsMatch(value, "\\A[0-9A-Fa-f]{16,18}\\z") ||
            value.Length % 2 != 0) return "";
        byte[] prefix = Convert.FromHexString(value);
        int offset = prefix[0] == 0xA1 ? 0 : 1;
        return prefix.Length >= offset + 8 && prefix[offset] == 0xA1 &&
            prefix[offset + 3] == 2 && prefix[offset + 5] == 0x83
            ? value.ToUpperInvariant() : "";
    }

    static string SafeId(string value) => Regex.IsMatch(value ?? "", "\\AD[0-9A-Fa-f]{8}\\z")
        ? value.ToUpperInvariant() : "";
    static string Hex4(string value) => Regex.IsMatch(value ?? "", "\\A[0-9A-Fa-f]{4}\\z")
        ? value.ToUpperInvariant() : "0000";

    static string SafeVersion(string value)
    {
        if (value is not { Length: > 0 and <= 96 } ||
            !Regex.IsMatch(value, "\\A[0-9A-Za-z.+-]+\\z")) return "unknown";
        if ((!string.IsNullOrEmpty(Environment.UserName) && value.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(Environment.MachineName) && value.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
            return "unknown";
        return value;
    }

    static string SafeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string trimmed = value.Length > 96 ? value[..96] : value;
        // Reject paths, serial labels, long identifiers and injected controls; allow normal product punctuation.
        if (Regex.IsMatch(trimmed, @"\\|[A-Za-z]:/|/(?:Users|home|tmp|var)/|\b(serial|s/n|username|computername)\b|[0-9A-Fa-f]{16}|[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-", RegexOptions.IgnoreCase))
            return "[redacted]";
        if (trimmed.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch))) return "[redacted]";
        string username = Environment.UserName, machine = Environment.MachineName;
        if ((!string.IsNullOrEmpty(username) && trimmed.Contains(username, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(machine) && trimmed.Contains(machine, StringComparison.OrdinalIgnoreCase)))
            return "[redacted]";
        return trimmed;
    }
}