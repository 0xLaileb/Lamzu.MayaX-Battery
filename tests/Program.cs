using MayaXBattery;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    count++;
}
var frame = new byte[] { 0, 0xA1, 0, 2, 2, 0, 0x83, 0, 50 };
Check(CompxSource.TryParse(frame, out var percent, out var charge) && percent == 50 && !charge, "Report ID layout");
Check(CompxSource.TryParse(frame[1..], out percent, out charge) && percent == 50, "Payload layout");
frame[7] = 1;
Check(CompxSource.TryParse(frame, out percent, out charge) && charge, "Charging");
frame[7] = 2;
Check(!CompxSource.TryParse(frame, out _, out _), "Invalid charging flag");
frame[7] = 0;
foreach (var value in new byte[] { 0, 101, 255 })
{
    frame[8] = value;
    Check(!CompxSource.TryParse(frame, out _, out _), "Invalid percentage");
}
foreach (var bytes in new[] { Array.Empty<byte>(), new byte[7], new byte[8], (byte[])null })
    Check(!CompxSource.TryParse(bytes, out _, out _), "Malformed frame");
var now = DateTimeOffset.UtcNow;
Reading R(int value, bool charging = false, string device = "Maya") => new(value, charging, device, null);
bool Near(double? actual, double expected) => actual.HasValue && Math.Abs(actual.Value - expected) < .000001;

var estimate = new RuntimeEstimate();
Check(estimate.Observe(R(80), now) == null, "First sample has no estimate");
Check(estimate.Observe(R(79), now.AddMinutes(10)) == null, "One percentage point is insufficient");
Check(estimate.Observe(R(78), now.AddMinutes(20)) == null, "Less than 30 observed minutes is insufficient");
Check(Near(estimate.Observe(R(78), now.AddMinutes(30)), 19.5), "Estimate uses observed time and percentage loss");
Check(Near(estimate.ObservedHours, .5) && estimate.Drop == 2 && Near(estimate.Rate, 4), "Half-hour and two-point threshold includes valid intervals");
Check(estimate.Observe(R(20), now.AddMinutes(20)) == null, "Out-of-order sample is ignored");
Check(Near(estimate.ObservedHours, .5) && estimate.Drop == 2, "Out-of-order sample cannot change history");
Check(Near(estimate.Observe(R(77), now.AddMinutes(40)), 77 / 4.5), "Following valid sample uses monotonic history");

var interrupted = new RuntimeEstimate();
interrupted.Observe(R(80), now);
interrupted.Observe(R(79), now.AddMinutes(10));
Check(interrupted.Observe(Reading.Fail("temporary timeout"), now.AddMinutes(15)) == null, "Read failure has no estimate");
interrupted.Observe(R(78), now.AddMinutes(20));
Check(Near(interrupted.Observe(R(78), now.AddMinutes(30)), 19.5), "Brief failed read preserves measured discharge");
Check(interrupted.Observe(R(78, charging: true), now.AddMinutes(35)) == null, "Charging has no runtime estimate");
Check(Near(interrupted.ObservedHours, .5) && interrupted.Drop == 2, "Charging retains discharge history");
Check(Near(interrupted.Observe(R(75), now.AddMinutes(40)), 18.75), "Discharge resumes from historical rate after charging");
Check(Near(interrupted.ObservedHours, .5) && interrupted.Drop == 2, "Charge interval does not count as discharge");
interrupted.Observe(Reading.Fail("offline"), now.AddMinutes(45));
Check(interrupted.Observe(R(70), now.AddHours(2)) is double, "Long outage retains earlier estimate");
Check(Near(interrupted.ObservedHours, .5) && interrupted.Drop == 2, "Long outage adds no unknown time or loss");
Check(interrupted.Observe(R(70, device: "Other"), now.AddHours(2).AddMinutes(10)) == null, "Device change clears old estimate");
Check(Near(interrupted.ObservedHours, 0) && interrupted.Drop == 0, "Device change clears old statistics");

var split = new RuntimeEstimate();
split.Observe(R(80), now);
split.Observe(R(79), now.AddMinutes(10));
split.Observe(R(78), now.AddMinutes(20));
split.Observe(R(70), now.AddMinutes(60));
Check(Near(split.ObservedHours, 1.0 / 3) && split.Drop == 2, "Gap over twelve minutes excludes unknown time and loss");
split.Observe(R(69), now.AddMinutes(70));
Check(Near(split.Observe(R(68), now.AddMinutes(80)), 68.0 / 6), "Separated valid segments combine their evidence");
Check(Near(split.ObservedHours, 2.0 / 3) && split.Drop == 4, "Long gap preserves earlier measured segments");

var boundary = new RuntimeEstimate();
boundary.Observe(R(80), now);
boundary.Observe(R(79), now.AddMinutes(12));
boundary.Observe(R(78), now.AddMinutes(24));
Check(Near(boundary.Observe(R(78), now.AddMinutes(36)), 23.4), "Twelve-minute gaps remain valid observations");
boundary.Observe(R(70), now.AddMinutes(49));
Check(Near(boundary.ObservedHours, .6) && boundary.Drop == 2, "Thirteen-minute gap starts a new observation");

var jitter = new RuntimeEstimate();
jitter.Observe(R(80), now);
jitter.Observe(R(79), now.AddMinutes(10));
jitter.Observe(R(81), now.AddMinutes(20));
jitter.Observe(R(80), now.AddMinutes(30));
jitter.Observe(R(79), now.AddMinutes(40));
Check(Near(jitter.ObservedHours, 2.0 / 3) && jitter.Drop == 1, "One- and two-point upward jitter cannot be counted twice");
jitter.Observe(R(78), now.AddMinutes(50));
Check(jitter.Drop == 2 && jitter.Rate.HasValue, "New low after jitter counts once");
jitter.Observe(R(85), now.AddMinutes(60));
Check(jitter.Drop == 2, "Larger upward change starts a new segment");
jitter.Observe(R(84), now.AddMinutes(70));
Check(jitter.Drop == 3, "Loss after larger rise is measured separately");

var fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MayaXBattery-Checks-" + Guid.NewGuid().ToString("N")));
var tempRoot = Path.GetFullPath(Path.GetTempPath());
Check(fixtureRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase), "History fixture stays under the temp directory");
Directory.CreateDirectory(fixtureRoot);
var blockedTempDirectory = Path.Combine(fixtureRoot, "blocked.json.tmp");
try
{
    var englishCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    var russianCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
    var settingsPath = Path.Combine(fixtureRoot, "settings.json");
    Check(LanguageSettings.DefaultFor(russianCulture) == UiLanguage.Russian &&
          LanguageSettings.DefaultFor(englishCulture) == UiLanguage.English,
        "Language default follows UI culture");
    Check(LanguageSettings.Load(settingsPath, englishCulture) == UiLanguage.English &&
          LanguageSettings.Load(settingsPath, russianCulture) == UiLanguage.Russian,
        "Missing settings use the UI culture without creating a file");
    Check(!File.Exists(settingsPath), "Loading language does not write settings");
    Check(LanguageSettings.Save(settingsPath, UiLanguage.English) &&
          LanguageSettings.Load(settingsPath, russianCulture) == UiLanguage.English,
        "Saved English overrides Russian UI culture");
    Check(LanguageSettings.Save(settingsPath, UiLanguage.Russian) &&
          LanguageSettings.Load(settingsPath, englishCulture) == UiLanguage.Russian,
        "Saved Russian overrides English UI culture");
    File.WriteAllText(settingsPath, "{invalid json");
    Check(LanguageSettings.Load(settingsPath, englishCulture) == UiLanguage.English,
        "Malformed language settings fall back safely");
    File.WriteAllText(settingsPath, "{\"Version\":2,\"Language\":\"ru\"}");
    Check(LanguageSettings.Load(settingsPath, englishCulture) == UiLanguage.English,
        "Unsupported settings version falls back safely");
    File.WriteAllText(settingsPath, "{\"Version\":1,\"Language\":\"fr\"}");
    Check(LanguageSettings.Load(settingsPath, russianCulture) == UiLanguage.Russian,
        "Unsupported language falls back safely");
    var englishText = new UiStrings(UiLanguage.English);
    var russianText = new UiStrings(UiLanguage.Russian);
    Check(englishText.BatteryHeading == "BATTERY LEVEL" && russianText.BatteryHeading == "ЗАРЯД БАТАРЕИ",
        "Battery heading has both languages");
    Check(englishText.Rate(1.8).Contains("1.8") && russianText.Rate(1.8).Contains("1,8"),
        "Numeric UI formatting follows selected language");
    Check(englishText.DeviceError("мышь не отвечает") == "Mouse is not responding" &&
          russianText.DeviceError("мышь не отвечает") == "Мышь не отвечает",
        "Device errors follow selected language after a read");
    Check(englishText.DeviceError("read-error:IOException") == "Read error: IOException" &&
          russianText.DeviceError("read-error:IOException") == "Ошибка чтения: IOException",
        "Read exceptions follow selected language after a read");
    Check(englishText.HistoryNote(10, 18, true).Contains("10.0 h") &&
          russianText.HistoryNote(10, 18, true).Contains("10,0 ч"),
        "History notes follow language and decimal formatting");
    var blockedSettingsPath = Path.Combine(fixtureRoot, "blocked-settings.json");
    Directory.CreateDirectory(blockedSettingsPath + ".tmp");
    Check(!LanguageSettings.Save(blockedSettingsPath, UiLanguage.English) &&
          LanguageSettings.Load(blockedSettingsPath, russianCulture) == UiLanguage.Russian,
        "Unavailable settings storage does not crash or change the fallback");
    Directory.Delete(blockedSettingsPath + ".tmp");

    var migrationRoot = Path.Combine(fixtureRoot, "migration");
    var oldData = Path.Combine(migrationRoot, "MouseBattery");
    var newData = Path.Combine(migrationRoot, "MayaX-Battery");
    Directory.CreateDirectory(oldData);
    Directory.CreateDirectory(newData);
    File.WriteAllText(Path.Combine(oldData, "settings.json"), "old settings");
    File.WriteAllText(Path.Combine(oldData, "history.json"), "old history");
    File.WriteAllText(Path.Combine(newData, "settings.json"), "new settings");
    AppStorage.MigrateLegacyFiles(migrationRoot);
    Check(File.ReadAllText(Path.Combine(newData, "settings.json")) == "new settings" &&
          File.ReadAllText(Path.Combine(newData, "history.json")) == "old history",
        "Migration preserves existing settings and copies only missing history");
    Check(File.ReadAllText(Path.Combine(oldData, "settings.json")) == "old settings" &&
          File.ReadAllText(Path.Combine(oldData, "history.json")) == "old history",
        "Migration leaves legacy files intact");
    File.WriteAllText(Path.Combine(oldData, "history.json"), "changed history");
    AppStorage.MigrateLegacyFiles(migrationRoot);
    Check(File.ReadAllText(Path.Combine(newData, "history.json")) == "old history",
        "Repeated migration does not overwrite new data");

    var blockedMigrationRoot = Path.Combine(fixtureRoot, "blocked-migration");
    var blockedOldData = Path.Combine(blockedMigrationRoot, "MouseBattery");
    var blockedNewData = Path.Combine(blockedMigrationRoot, "MayaX-Battery");
    Directory.CreateDirectory(blockedOldData);
    Directory.CreateDirectory(Path.Combine(blockedNewData, "settings.json"));
    File.WriteAllText(Path.Combine(blockedOldData, "settings.json"), "old settings");
    File.WriteAllText(Path.Combine(blockedOldData, "history.json"), "old history");
    AppStorage.MigrateLegacyFiles(blockedMigrationRoot);
    Check(File.ReadAllText(Path.Combine(blockedNewData, "history.json")) == "old history",
        "One inaccessible migration destination does not stop the other file");

    var historyPath = Path.Combine(fixtureRoot, "history.json");
    var beforeRestart = new RuntimeEstimate(historyPath);
    beforeRestart.Observe(R(80), now);
    beforeRestart.Observe(R(79), now.AddMinutes(10));
    beforeRestart.Observe(R(78), now.AddMinutes(20));
    beforeRestart.Observe(R(78), now.AddMinutes(30));
    Check(File.Exists(historyPath), "Discharge history is saved");
    var afterRestart = new RuntimeEstimate(historyPath);
    Check(Near(afterRestart.ObservedHours, .5) && afterRestart.Drop == 2, "Restart restores measured history");
    Check(Near(afterRestart.Observe(R(77), now.AddMinutes(35)), 77.0 / 4), "Restart retains historical estimate");
    Check(Near(afterRestart.ObservedHours, .5) && afterRestart.Drop == 2, "Restart does not connect unobserved interval");
    afterRestart.Observe(R(76), now.AddMinutes(45));
    Check(Near(afterRestart.ObservedHours, 2.0 / 3) && afterRestart.Drop == 3, "New observations extend restored history");

    Directory.CreateDirectory(blockedTempDirectory);
    var blockedPath = Path.Combine(fixtureRoot, "blocked.json");
    var blocked = new RuntimeEstimate(blockedPath);
    var healthy = new RuntimeEstimate();
    foreach (var sample in new[] { (Minute: 0, Percent: 80), (Minute: 10, Percent: 79),
                                   (Minute: 20, Percent: 78), (Minute: 30, Percent: 78) })
    {
        blocked.Observe(R(sample.Percent), now.AddMinutes(sample.Minute));
        healthy.Observe(R(sample.Percent), now.AddMinutes(sample.Minute));
    }
    Check(Near(blocked.Rate, 4) && Near(blocked.ObservedHours, .5) && blocked.Drop == 2,
        "Failed history writes do not lose in-memory measurements");
    Check(blocked.Note != healthy.Note && !string.IsNullOrWhiteSpace(blocked.Note),
        "Failed history writes are reported in the status note");
    Directory.Delete(blockedTempDirectory);
    blocked.Observe(R(77), now.AddMinutes(40));
    healthy.Observe(R(77), now.AddMinutes(40));
    Check(File.Exists(blockedPath) && Near(blocked.Rate, healthy.Rate!.Value) && blocked.Note == healthy.Note,
        "History can be saved after the write obstruction is removed");
    var recovered = new RuntimeEstimate(blockedPath);
    Check(Near(recovered.ObservedHours, blocked.ObservedHours) && recovered.Drop == blocked.Drop,
        "Recovered history survives restart");

    var longPath = Path.Combine(fixtureRoot, "long-session.json");
    var longSession = new RuntimeEstimate(longPath);
    var longStart = now.AddDays(-16);
    const int samplesPerDay = 24 * 5;
    for (var sample = 0; sample <= 16 * samplesPerDay; sample++)
        longSession.Observe(R(90 - sample / samplesPerDay), longStart.AddMinutes(sample * 12));
    Check(longSession.Rate is double longRate && double.IsFinite(longRate) && longRate > 0,
        "Continuous session longer than retention keeps a usable discharge rate");
    var longObservedHours = longSession.ObservedHours;
    var longDrop = longSession.Drop;
    var resumedLongSession = new RuntimeEstimate(longPath);
    Check(Near(resumedLongSession.ObservedHours, longObservedHours) && resumedLongSession.Drop == longDrop &&
          resumedLongSession.Rate is double restoredRate && double.IsFinite(restoredRate),
        "Daily-split long session loads with measured history intact");
    var resumedEstimate = resumedLongSession.Observe(R(74), now.AddMinutes(10));
    Check(resumedEstimate is double hours && double.IsFinite(hours) && hours > 0 &&
          Near(resumedLongSession.ObservedHours, longObservedHours),
        "Long session yields a finite estimate after restart without linking the downtime");

    var stalePath = Path.Combine(fixtureRoot, "stale.json");
    var oldHistory = new RuntimeEstimate(stalePath);
    oldHistory.Observe(R(80), now);
    oldHistory.Observe(R(78), now.AddMinutes(10));
    var stale = new RuntimeEstimate(stalePath);
    stale.Observe(R(60), now.AddDays(15));
    Check(Near(stale.ObservedHours, 0) && stale.Drop == 0 && stale.Rate == null, "History older than fourteen days is discarded");

    var futurePath = Path.Combine(fixtureRoot, "future.json");
    var futureHistory = new RuntimeEstimate(futurePath);
    futureHistory.Observe(R(80), now.AddDays(1));
    futureHistory.Observe(R(78), now.AddDays(1).AddMinutes(10));
    var future = new RuntimeEstimate(futurePath);
    future.Observe(R(60), now);
    Check(Near(future.ObservedHours, 0) && future.Drop == 0 && future.Rate == null, "Future-dated history is discarded");

    var badJsonPath = Path.Combine(fixtureRoot, "bad-json.json");
    File.WriteAllText(badJsonPath, "{not valid json");
    var badJson = new RuntimeEstimate(badJsonPath);
    var normalNote = new RuntimeEstimate().Note;
    Check(badJson.Rate == null && badJson.Note != normalNote, "Malformed JSON is reported without crashing");
    Check(badJson.Observe(R(80), now) == null, "Malformed history can start a fresh observation");

    var corruptPath = Path.Combine(fixtureRoot, "corrupt.json");
    File.WriteAllText(corruptPath, System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 1,
        Device = "Maya",
        Segments = new[] { new { Start = now.AddMinutes(10), End = now, Initial = 80, Minimum = 78 } }
    }));
    var corrupt = new RuntimeEstimate(corruptPath);
    Check(corrupt.Rate == null && corrupt.Note != normalNote, "Invalid saved interval is reported without crashing");
    Check(corrupt.Observe(R(80), now) == null, "Invalid saved interval can start a fresh observation");
}
finally
{
    if (Directory.Exists(blockedTempDirectory)) Directory.Delete(blockedTempDirectory);
    Directory.Delete(fixtureRoot, recursive: true);
}
foreach (var size in new[] { 16, 20, 24, 28, 32, 40, 48, 64 })
    foreach (var label in new[] { "?", "7", "18", "87", "100" })
    {
        using var bitmap = IconFactory.Render(label, Palette.Normal, size);
                bool hasInk = false;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                hasInk |= bitmap.GetPixel(x, y).A > 0;
        Check(bitmap.Width == size && bitmap.GetPixel(0, 0).A == 0 && hasInk, "Transparent icon with visible numerals");
    }
using (var icon = IconFactory.Build("100", Palette.Normal))
using (var bitmap = icon.ToBitmap())
    Check(bitmap.Width > 0, "Icon usable after source stream disposal");
var schedule = new PollSchedule();
Check(schedule.DelaySeconds == 60, "Normal polling is once per minute");
schedule.Observe(true);
Check(schedule.DelaySeconds == 60, "Successful read keeps normal interval");
for (int failure = 1; failure <= 4; failure++)
{
    schedule.Observe(false);
    Check(schedule.DelaySeconds == 15, "First four failures retry quickly");
}
schedule.Observe(false);
Check(schedule.DelaySeconds == 60, "Persistent failure backs off");
for (int i = 0; i < 10000; i++) schedule.Observe(false);
Check(schedule.DelaySeconds == 60, "Long offline period keeps bounded backoff");
schedule.Observe(true);
Check(schedule.DelaySeconds == 60, "Recovery resets retry budget");
schedule.Observe(false);
Check(schedule.DelaySeconds == 15, "New outage gets quick retries again");
Console.WriteLine($"PASS: {count} checks");
if (args.Contains("--device"))
{
    var reading = new CompxSource().Read();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(reading));
}



