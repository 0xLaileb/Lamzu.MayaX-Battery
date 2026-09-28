using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text.Json;
using MayaXBattery;

internal static class DiagnosticChecks
{
    internal static void Run(Action<bool, string> check, string fixtureRoot)
    {
        FromCollections_NoCandidate_ExportsEmptyInventory(check, fixtureRoot);
        FromCollections_NordicOnly_ReportsUnsupportedCandidate(check);
        FromCollections_MayaXCollection_ReportsPollEligibility(check);
        FromCollections_UnavailableCapabilities_DoesNotClaimEligibility(check, fixtureRoot);
        FromCollections_BootloaderIds_ExcludePolling(check);
        FromCollections_MultipleCollections_UsesAnonymousIds(check, fixtureRoot);
        Export_PersonalCanaries_RedactsEveryEntry(check, fixtureRoot);
        Export_FailureEvents_RetainsDistinctStagesWithinBound(check, fixtureRoot);
        Export_UsbVersion_DoesNotClaimFirmware(check, fixtureRoot);
        Export_InformationalVersion_PreservesGitHash(check, fixtureRoot);
        Export_MissingDirectory_DoesNotCreateData(check, fixtureRoot);
        Export_LockedExistingTarget_PreservesExistingData(check, fixtureRoot);
        Record_OverCapacity_RetainsRecentBoundedEvents(check);
        Run_ValidDirectory_SavesUniqueArchivesBesideExecutable(check, fixtureRoot);
        Run_CaptureFailure_ReturnsErrorWithoutArchive(check, fixtureRoot);
        Run_MissingDirectory_ReturnsErrorWithoutFallback(check, fixtureRoot);
        Run_BlockedDirectory_ReturnsErrorAndPreservesExistingFile(check, fixtureRoot);
    }

    private static void FromCollections_NoCandidate_ExportsEmptyInventory(Action<bool, string> check, string root)
    {
        var snapshot = Diagnostics.FromCollections(
            [Collection("unrelated", 0x1234, 0x5678, "Generic HID", 0x0001, 0x0002, 0, 8)],
            totalHidInterfaces: 1);
        check(snapshot.Devices.Count == 0 && snapshot.TotalHidInterfaces == 1 && snapshot.InspectedCollections == 0,
            nameof(FromCollections_NoCandidate_ExportsEmptyInventory));

        var entries = ExportEntries(root, "empty.zip", snapshot);
        check(entries.Keys.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(
                new[] { "devices.json", "events.jsonl", "summary.txt" }),
            "Export_NoCandidate_ContainsExpectedEntries");
        using var devices = JsonDocument.Parse(entries["devices.json"]);
        var data = devices.RootElement;
        check(data.GetProperty("devices").GetArrayLength() == 0 &&
              data.GetProperty("totalHidInterfaces").GetInt32() == 1 &&
              data.GetProperty("passive").GetBoolean() &&
              !data.GetProperty("newVendorCommands").GetBoolean() &&
              !string.IsNullOrWhiteSpace(entries["summary.txt"]),
            "Export_NoCandidate_IsParseableAndPassive");
    }

    private static void FromCollections_NordicOnly_ReportsUnsupportedCandidate(Action<bool, string> check)
    {
        var snapshot = Diagnostics.FromCollections(
            [Collection("nordic", 0x3554, 0xF50F, "MAYA", 0xFF00, 0x0001, 0, 17)]);
        var device = snapshot.Devices.Single();
        check(device.VendorId == "3554" && device.ProductId == "F50F" &&
              device.ProfileHint.Contains("Nordic", StringComparison.OrdinalIgnoreCase) &&
              !device.SelectionReason.Contains("eligible", StringComparison.OrdinalIgnoreCase) &&
              !string.IsNullOrWhiteSpace(device.SelectionReason),
            nameof(FromCollections_NordicOnly_ReportsUnsupportedCandidate));
    }

    private static void FromCollections_MayaXCollection_ReportsPollEligibility(Action<bool, string> check)
    {
        var snapshot = Diagnostics.FromCollections(
            [Collection("maya-x", 0x373E, 0x001E, "Maya X", 0xFF00, 0x0001, 65, 0)]);
        var device = snapshot.Devices.Single();
        check(device.VendorId == "373E" && device.FeatureLength == 65 &&
              device.SelectionReason.Contains("eligible", StringComparison.OrdinalIgnoreCase) &&
              device.ProfileHint.Contains("Maya X", StringComparison.OrdinalIgnoreCase),
            nameof(FromCollections_MayaXCollection_ReportsPollEligibility));
    }

    private static void FromCollections_BootloaderIds_ExcludePolling(Action<bool, string> check)
    {
        var snapshot = Diagnostics.FromCollections(
        [
            Collection("mouse-boot", 0x3554, 0xF403, "MAYA", 0xFF00, 1, 65, 17),
            Collection("receiver-1k-boot", 0x3554, 0xF402, "MAYA", 0xFF00, 1, 65, 17),
            Collection("receiver-4k-boot", 0x3554, 0xF401, "MAYA", 0xFF00, 1, 65, 17)
        ]);
        check(snapshot.Devices.Count == 3 && snapshot.Devices.All(device =>
                  device.ProfileHint.Contains("bootloader", StringComparison.OrdinalIgnoreCase) &&
                  device.SelectionReason.Contains("never poll", StringComparison.OrdinalIgnoreCase)),
            nameof(FromCollections_BootloaderIds_ExcludePolling));
    }

    private static void FromCollections_UnavailableCapabilities_DoesNotClaimEligibility(Action<bool, string> check, string root)
    {
        var snapshot = Diagnostics.FromCollections(
            [new Hid.Collection("unreadable-caps", 0x373E, 0x001E, "Maya X", 0, 0, 0, 0,
                                CapsAvailable: false)],
            issues: [new DiagnosticIssue { Stage = "discovery", Status = "capabilities-unavailable" }]);
        var device = snapshot.Devices.Single();
        check(!device.CapabilitiesAvailable &&
              device.SelectionReason.Contains("unknown", StringComparison.OrdinalIgnoreCase) &&
              !device.SelectionReason.Contains("eligible", StringComparison.OrdinalIgnoreCase),
            nameof(FromCollections_UnavailableCapabilities_DoesNotClaimEligibility));
        var entries = ExportEntries(root, "missing-capabilities.zip", snapshot);
        var summary = entries["summary.txt"];
        check((summary.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("incomplete", StringComparison.OrdinalIgnoreCase)) &&
              !summary.Contains("No collection matches", StringComparison.OrdinalIgnoreCase) &&
              !summary.Contains("No captured collection matches", StringComparison.OrdinalIgnoreCase),
            "Export_UnavailableCapabilities_DoesNotClaimNoMatch");
        using var parsed = JsonDocument.Parse(entries["devices.json"]);
        check(parsed.RootElement.GetProperty("issues")[0].GetProperty("status").GetString() ==
              "capabilities-unavailable",
            "Export_UnavailableCapabilities_PreservesDiscoveryIssue");
    }

    private static void FromCollections_MultipleCollections_UsesAnonymousIds(Action<bool, string> check, string root)
    {
        const string pathCanary = "PrivateHidInstancePath-548391";
        var secondPath = pathCanary + "-col05";
        DiagnosticLog.Record("poll.open", "opening-collection", path: secondPath);
        var snapshot = Diagnostics.FromCollections(
        [
            Collection(pathCanary + "-col01", 0x3554, 0xF50F, "MAYA", 0x0001, 2, 0, 17),
            Collection(secondPath, 0x3554, 0xF50F, "MAYA", 0xFF00, 1, 0, 17)
        ]);
        check(snapshot.Devices.Count == 2 &&
              snapshot.Devices.Select(device => device.AnonymousId).Distinct().Count() == 2 &&
              snapshot.Events.Last().AnonymousId == snapshot.Devices[1].AnonymousId,
            nameof(FromCollections_MultipleCollections_UsesAnonymousIds));
        var entries = ExportEntries(root, "collections.zip", snapshot);
        check(entries.Values.All(value => !value.Contains(pathCanary, StringComparison.Ordinal)),
            "Export_MultipleCollections_OmitsRawInstancePath");
    }

    private static void Export_PersonalCanaries_RedactsEveryEntry(Action<bool, string> check, string root)
    {
        const string pathCanary = "C:\\Users\\PrivateFriend\\AppData\\secret";
        const string serialCanary = "SERIAL:Secret123456";
        var snapshot = new DiagnosticSnapshot
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            AppVersion = "1.0.0",
            OperatingSystem = "Windows 11",
            Architecture = "x64",
            Devices = [new DiagnosticDevice
            {
                AnonymousId = "D001", VendorId = "3554", ProductId = "F50F",
                Manufacturer = serialCanary, Product = pathCanary,
                UsbVersionNumber = "0124", UsagePage = "FF00", Usage = "0001",
                SelectionReason = pathCanary, ProfileHint = "Nordic Maya candidate"
            }],
            Issues = [new DiagnosticIssue { Stage = "discovery", Status = pathCanary }],
            Events = [new DiagnosticEvent { Utc = DateTimeOffset.UtcNow, Stage = "read", Status = serialCanary,
                                            ResponsePrefix = "DEADBEEF" }]
        };
        var entries = ExportEntries(root, "private.zip", snapshot);
        check(entries.Values.All(value => !value.Contains(pathCanary, StringComparison.Ordinal) &&
                                           !value.Contains(serialCanary, StringComparison.Ordinal) &&
                                           !value.Contains("DEADBEEF", StringComparison.Ordinal)),
            nameof(Export_PersonalCanaries_RedactsEveryEntry));
        using var devices = JsonDocument.Parse(entries["devices.json"]);
        var exported = devices.RootElement.GetProperty("devices")[0];
        check(exported.GetProperty("manufacturer").GetString() == "[redacted]" &&
              exported.GetProperty("product").GetString() == "[redacted]",
            "Export_PersonalCanaries_MarksRedactedFields");
    }

    private static void Export_FailureEvents_RetainsDistinctStagesWithinBound(Action<bool, string> check, string root)
    {
        var events = Enumerable.Range(0, 140).Select(index => new DiagnosticEvent
        {
            Utc = DateTimeOffset.UtcNow,
            Stage = "read",
            Status = $"attempt-{index}"
        }).Concat(
        [
            new DiagnosticEvent { Utc = DateTimeOffset.UtcNow, Stage = "transport", Status = "access-failed" },
            new DiagnosticEvent { Utc = DateTimeOffset.UtcNow, Stage = "parse", Status = "invalid-frame" }
        ]).ToArray();
        var snapshot = new DiagnosticSnapshot
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Events = events,
            Issues =
            [
                new DiagnosticIssue { Stage = "transport", Status = "access-failed" },
                new DiagnosticIssue { Stage = "parse", Status = "invalid-frame" }
            ]
        };
        var entries = ExportEntries(root, "failures.zip", snapshot);
        var lines = entries["events.jsonl"].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        check(lines.Length <= 128 && lines.Length > 0,
            "Export_ManyEvents_BoundsExportSize");
        var stages = new List<string>();
        var statuses = new List<string>();
        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            stages.Add(parsed.RootElement.GetProperty("stage").GetString() ?? "");
            statuses.Add(parsed.RootElement.GetProperty("status").GetString() ?? "");
        }
        check(stages.Contains("transport") && stages.Contains("parse") &&
              statuses.Contains("access-failed") && statuses.Contains("invalid-frame") &&
              !statuses.Contains("attempt-0"),
            nameof(Export_FailureEvents_RetainsDistinctStagesWithinBound));
        using var devices = JsonDocument.Parse(entries["devices.json"]);
        var issues = devices.RootElement.GetProperty("issues").EnumerateArray().ToArray();
        check(issues.Any(issue => issue.GetProperty("stage").GetString() == "transport") &&
              issues.Any(issue => issue.GetProperty("stage").GetString() == "parse"),
            "Export_FailureIssues_RetainsDistinctStages");
    }

    private static void Export_UsbVersion_DoesNotClaimFirmware(Action<bool, string> check, string root)
    {
        var snapshot = Diagnostics.FromCollections(
            [new Hid.Collection("nordic", 0x3554, 0xF50F, "MAYA", 0xFF00, 1, 0, 17,
                                VersionNumber: 0x0124)]);
        var entries = ExportEntries(root, "version.zip", snapshot);
        using var parsed = JsonDocument.Parse(entries["devices.json"]);
        var device = parsed.RootElement.GetProperty("devices")[0];
        check(device.GetProperty("usbVersionNumber").GetString() == "0124" &&
              device.GetProperty("mouseFirmwareVersion").GetString() == "unknown" &&
              device.GetProperty("receiverFirmwareVersion").GetString() == "unknown" &&
              device.GetProperty("usagePage").GetString() == "FF00" &&
              device.GetProperty("inputLength").GetInt32() == 17 &&
              device.GetProperty("reportIds").GetString() == "not collected",
            nameof(Export_UsbVersion_DoesNotClaimFirmware));
    }

    private static void Export_InformationalVersion_PreservesGitHash(Action<bool, string> check, string root)
    {
        const string version = "1.2.3+gdeadbeef";
        var snapshot = new DiagnosticSnapshot { CapturedUtc = DateTimeOffset.UtcNow, AppVersion = version };
        var entries = ExportEntries(root, "informational-version.zip", snapshot);
        using var parsed = JsonDocument.Parse(entries["devices.json"]);
        check(parsed.RootElement.GetProperty("appVersion").GetString() == version &&
              entries["summary.txt"].Contains(version, StringComparison.Ordinal),
            nameof(Export_InformationalVersion_PreservesGitHash));
    }

    private static void Export_LockedExistingTarget_PreservesExistingData(Action<bool, string> check, string root)
    {
        var path = Path.Combine(root, "already-existing.zip");
        var original = new byte[] { 0x2A, 0x7F, 0x00, 0x55 };
        File.WriteAllBytes(path, original);
        bool failed = false;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { Diagnostics.Export(path, new DiagnosticSnapshot { CapturedUtc = DateTimeOffset.UtcNow }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        }
        check(failed && File.ReadAllBytes(path).SequenceEqual(original) &&
              !Directory.EnumerateFiles(root, ".already-existing.zip.*.tmp").Any(),
            nameof(Export_LockedExistingTarget_PreservesExistingData));
    }

    private static void Export_MissingDirectory_DoesNotCreateData(Action<bool, string> check, string root)
    {
        var missing = Path.Combine(root, "missing-diagnostic-directory");
        var target = Path.Combine(missing, "diagnostics.zip");
        bool failed = false;
        try { Diagnostics.Export(target, new DiagnosticSnapshot { CapturedUtc = DateTimeOffset.UtcNow }); }
        catch (DirectoryNotFoundException) { failed = true; }
        check(failed && !Directory.Exists(missing) && !File.Exists(target),
            nameof(Export_MissingDirectory_DoesNotCreateData));
    }

    private static void Record_OverCapacity_RetainsRecentBoundedEvents(Action<bool, string> check)
    {
        var marker = Guid.NewGuid().ToString("N");
        var response = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        for (var index = 0; index < 150; index++)
            DiagnosticLog.Record("read", $"{marker}:{index}", response: response);

        var events = DiagnosticLog.Snapshot();
        check(events.Count <= 128 && events.Any(item => item.Status == $"{marker}:149") &&
              !events.Any(item => item.Status == $"{marker}:0"),
            nameof(Record_OverCapacity_RetainsRecentBoundedEvents));
        check(events.Where(item => item.Status.StartsWith(marker, StringComparison.Ordinal))
              .All(item => item.ResponsePrefix.Length <= 18),
            "Record_LongDeviceResponse_DoesNotRetainFullPayload");
    }

    private static void Run_ValidDirectory_SavesUniqueArchivesBesideExecutable(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "cli-success");
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "notes.txt");
        File.WriteAllText(sentinel, "keep existing user data");
        var workingDirectory = Directory.GetCurrentDirectory();
        var beforeWorkingFiles = Directory.EnumerateFiles(workingDirectory, "MayaX-diagnostics-*.zip")
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var captures = 0;
        DiagnosticSnapshot Capture()
        {
            captures++;
            return Diagnostics.FromCollections(
                [Collection("cli-maya", 0x3554, 0xF50F, "MAYA", 0xFF00, 1, 0, 17)],
                issues: [new DiagnosticIssue { Stage = "discovery", Status = "capabilities-unavailable" }]);
        }

        using var output = new StringWriter();
        using var errors = new StringWriter();
        var first = DiagnosticCommand.Run(directory, Capture, output, errors);
        var second = DiagnosticCommand.Run(directory, Capture, output, errors);
        var archives = Directory.EnumerateFiles(directory, "*.zip").ToArray();
        check(first == 0 && second == 0 && captures == 2 && archives.Length == 2 &&
              archives.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 &&
              archives.All(path => Regex.IsMatch(Path.GetFileName(path),
                  @"\AMayaX-diagnostics-\d{8}T\d{6}Z-[0-9A-Fa-f]{32}\.zip\z")) &&
              Directory.EnumerateFiles(workingDirectory, "MayaX-diagnostics-*.zip")
                  .Order(StringComparer.OrdinalIgnoreCase)
                  .SequenceEqual(beforeWorkingFiles),
            nameof(Run_ValidDirectory_SavesUniqueArchivesBesideExecutable));
        check(File.ReadAllText(sentinel) == "keep existing user data" &&
              !Directory.EnumerateFiles(directory, "*.tmp").Any() &&
              Directory.EnumerateFiles(directory).Count() == 3,
            "Run_ValidDirectory_PreservesExistingDataAndCleansTemporaryFiles");
        foreach (var archivePath in archives)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            check(archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)
                  .SequenceEqual(new[] { "devices.json", "events.jsonl", "summary.txt" }),
                "Run_ValidDirectory_ArchiveHasDiagnosticEntries");
            using var reader = new StreamReader(archive.GetEntry("devices.json")!.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            check(document.RootElement.GetProperty("devices").GetArrayLength() == 1 &&
                  document.RootElement.GetProperty("passive").GetBoolean() &&
                  document.RootElement.GetProperty("issues")[0].GetProperty("status").GetString() ==
                      "capabilities-unavailable",
                "Run_ValidDirectory_ArchiveContainsIncompleteSnapshot");
        }
    }

    private static void Run_CaptureFailure_ReturnsErrorWithoutArchive(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "cli-capture-error");
        Directory.CreateDirectory(directory);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var result = DiagnosticCommand.Run(directory,
            () => throw new InvalidOperationException("PrivateFriendSecret capture failure"), output, errors);
        var errorFiles = Directory.EnumerateFiles(directory, "MayaX-diagnostics-error-*.txt").ToArray();
        var errorText = errorFiles.Length == 1 ? File.ReadAllText(errorFiles[0]) : "";
        check(result == 1 && errorFiles.Length == 1 &&
              Regex.IsMatch(Path.GetFileName(errorFiles[0]),
                  @"\AMayaX-diagnostics-error-\d{8}T\d{6}Z-[0-9A-Fa-f]{32}\.txt\z") &&
              Directory.EnumerateFileSystemEntries(directory).Count() == 1 &&
              !Directory.EnumerateFiles(directory, "*.zip").Any() &&
              !Directory.EnumerateFiles(directory, "*.tmp").Any() &&
              errorText.Contains("Diagnostics failed", StringComparison.OrdinalIgnoreCase) &&
              !errorText.Contains("PrivateFriendSecret", StringComparison.Ordinal) &&
              !errors.ToString().Contains("PrivateFriendSecret", StringComparison.Ordinal) &&
              !string.IsNullOrWhiteSpace(errors.ToString()),
            nameof(Run_CaptureFailure_ReturnsErrorWithoutArchive));
    }

    private static void Run_MissingDirectory_ReturnsErrorWithoutFallback(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "cli-directory-missing");
        var workingDirectory = Directory.GetCurrentDirectory();
        var beforeWorkingFiles = Directory.EnumerateFiles(workingDirectory, "MayaX-diagnostics-*.zip")
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var result = DiagnosticCommand.Run(directory,
            () => new DiagnosticSnapshot { CapturedUtc = DateTimeOffset.UtcNow }, output, errors);
        check(result == 1 && !Directory.Exists(directory) &&
              Directory.EnumerateFiles(workingDirectory, "MayaX-diagnostics-*.zip")
                  .Order(StringComparer.OrdinalIgnoreCase)
                  .SequenceEqual(beforeWorkingFiles) &&
              !string.IsNullOrWhiteSpace(errors.ToString()),
            nameof(Run_MissingDirectory_ReturnsErrorWithoutFallback));
    }

    private static void Run_BlockedDirectory_ReturnsErrorAndPreservesExistingFile(Action<bool, string> check, string root)
    {
        var blocked = Path.Combine(root, "cli-blocked-directory");
        File.WriteAllText(blocked, "keep existing user data");
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var result = DiagnosticCommand.Run(blocked,
            () => new DiagnosticSnapshot { CapturedUtc = DateTimeOffset.UtcNow }, output, errors);
        check(result == 1 && File.ReadAllText(blocked) == "keep existing user data" &&
              !Directory.EnumerateFiles(root, "*.tmp").Any() &&
              !string.IsNullOrWhiteSpace(errors.ToString()),
            nameof(Run_BlockedDirectory_ReturnsErrorAndPreservesExistingFile));
    }

    private static Hid.Collection Collection(string path, ushort vid, ushort pid, string product,
        ushort usagePage, ushort usage, int featureLength, int inputLength) =>
        new(path, vid, pid, product, usagePage, usage, featureLength, inputLength);

    private static Dictionary<string, string> ExportEntries(string root, string fileName, DiagnosticSnapshot snapshot)
    {
        var path = Path.Combine(root, fileName);
        Diagnostics.Export(path, snapshot);
        using var archive = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            entries.Add(entry.FullName, reader.ReadToEnd());
        }
        return entries;
    }
}