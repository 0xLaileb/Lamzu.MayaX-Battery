using System.Globalization;
using System.Text.Json;

namespace MayaXBattery;

internal enum UiLanguage { English, Russian }

internal static class LanguageSettings
{
    internal static string DefaultPath => AppStorage.SettingsPath;

    internal static UiLanguage DefaultFor(CultureInfo culture) =>
        string.Equals(culture?.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Russian : UiLanguage.English;

    internal static UiLanguage Load(string path, CultureInfo culture)
    {
        var fallback = DefaultFor(culture);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024) return fallback;
            var saved = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(path));
            return saved?.Version == 1 ? Parse(saved.Language) ?? fallback : fallback;
        }
        catch (Exception ex) when (IsStorageError(ex)) { return fallback; }
    }

    internal static bool Save(string path, UiLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new SettingsDocument
            {
                Language = Code(language)
            }));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsStorageError(ex)) { return false; }
    }

    internal static UiLanguage? Parse(string code) => code?.ToLowerInvariant() switch
    {
        "en" => UiLanguage.English,
        "ru" => UiLanguage.Russian,
        _ => null
    };

    internal static string Code(UiLanguage language) => language == UiLanguage.Russian ? "ru" : "en";

    static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or JsonException or NotSupportedException
        or ArgumentException;

    internal sealed class SettingsDocument
    {
        public int Version { get; set; } = 1;
        public string Language { get; set; }
    }
}

internal sealed class UiStrings
{
    internal UiLanguage Language { get; }
    internal CultureInfo Culture { get; }
    bool Ru => Language == UiLanguage.Russian;

    internal UiStrings(UiLanguage language)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(Ru ? "ru-RU" : "en-US");
    }

    internal string BatteryHeading => Ru ? "ЗАРЯД БАТАРЕИ" : "BATTERY LEVEL";
    internal string BatteryAutomation => Ru ? "Заряд батареи" : "Battery level";
    internal string MouseIconAutomation => Ru ? "Значок мыши" : "Mouse icon";
    internal string UnknownCharge => Ru ? "Заряд неизвестен" : "Battery level unknown";
    internal string ChargeAutomation(int percent) => Ru ? $"Заряд {percent}%" : $"Battery level {percent}%";
    internal string WaitingForData => Ru ? "Ожидание данных" : "Waiting for data";
    internal string Charging => Ru ? "Заряжается" : "Charging";
    internal string LowCharge => Ru ? "Низкий заряд · подключите кабель" : "Low battery · connect the cable";
    internal string OnBattery => Ru ? "Работает от аккумулятора" : "Running on battery";
    internal string RateHeading => Ru ? "Расход" : "Drain rate";
    internal string RemainingHeading => Ru ? "Осталось" : "Remaining";
    internal string Rate(double rate) => rate.ToString("0.#", Culture) + (Ru ? " %/ч" : " %/h");
    internal string RemainingMinutes(double minutes) => "≈ " + Math.Max(1, minutes).ToString("0", Culture) + (Ru ? " мин" : " min");
    internal string RemainingHours(double hours) => "≈ " + hours.ToString("0", Culture) + (Ru ? " ч" : " h");
    internal string WakeMouse => Ru ? "Разбудите мышь и проверьте приёмник." : "Wake the mouse and check the receiver.";
    internal string EstimateAfterDischarge => Ru ? "Прогноз обновится после начала разряда." : "The estimate will update when discharging starts.";
    internal string ApproximateEstimate => Ru ? "Приблизительная оценка по наблюдаемому расходу." : "Approximate estimate based on observed drain.";
    internal string EstimateAfterChange => Ru ? "Оценка появится после изменения заряда." : "An estimate will appear after the battery level changes.";
    internal string FirstPollPending => Ru ? "Первый опрос ещё не завершён" : "The first check has not finished";
    internal string CheckedAt(DateTimeOffset time, int seconds) => Ru
        ? $"Проверено {time.ToString("HH:mm", Culture)} · опрос {seconds} с"
        : $"Checked {time.ToString("HH:mm", Culture)} · every {seconds} s";
    internal string Refresh => Ru ? "Обновить" : "Refresh";
    internal string RefreshAutomation => Ru ? "Обновить заряд мыши" : "Refresh mouse battery level";
    internal string Checking => Ru ? "Проверяем…" : "Checking…";
    internal string ExportDiagnostics => Ru ? "Экспорт диагностики…" : "Export diagnostics…";
    internal string CollectingDiagnostics => Ru ? "Собираем отчёт…" : "Collecting report…";
    internal string DiagnosticPrivacy => Ru ? "Локальный ZIP-отчёт. Данные никуда не отправляются." : "Local ZIP report. Nothing is uploaded.";
    internal string DiagnosticWaiting => Ru ? "Ожидаем завершения опроса и собираем данные устройств." : "Waiting for polling to finish and collecting device details.";
    internal string DiagnosticHelp => Ru ? "Проверьте приёмник. Если ошибка остаётся, экспортируйте диагностику." : "Check the receiver. If the problem persists, export diagnostics.";
    internal string DiagnosticSaveTitle => Ru ? "Сохранить отчёт диагностики" : "Save diagnostic report";
    internal string DiagnosticSaved => Ru ? "Диагностика сохранена." : "Diagnostics saved.";
    internal string DiagnosticFailed => Ru ? "Не удалось сохранить диагностику. Проверьте папку и права доступа." : "Could not save diagnostics. Check the folder and access permissions.";
    internal string DiagnosticBusy => Ru ? "Опрос не завершился. Запустите приложение с --diagnostics: отчёт сохранится рядом с EXE." : "Polling did not finish. Run the app with --diagnostics to save a report beside the EXE.";
    internal string DiagnosticMode => Ru ? "Режим диагностики: опрос батареи отключён" : "Diagnostic mode: battery polling is off";
    internal string OpenApp => Ru ? "Открыть MayaX-Battery" : "Open MayaX-Battery";
    internal string PollingMouse => Ru ? "Опрашиваю мышь…" : "Checking mouse…";
    internal string RefreshNow => Ru ? "Обновить сейчас" : "Refresh now";
    internal string Exit => Ru ? "Выход" : "Exit";
    internal string LanguageMenu => Ru ? "Язык" : "Language";
    internal string StatusUnavailable => Ru ? "Мышь: нет данных" : "Mouse: no data";
    internal string TrayPolling => Ru ? "Заряд мыши · опрашиваю…" : "Mouse battery · checking…";
    internal string TrayStatus(string device, int percent, bool charging) =>
        $"{device}: {percent}%{(charging ? Ru ? " · зарядка" : " · charging" : "")}";
    internal string AlreadyRunning => Ru ? "MayaX-Battery уже запущен." : "MayaX-Battery is already running.";
    internal string ReadFailure(string exceptionName) => Ru ? $"Ошибка чтения: {exceptionName}" : $"Read error: {exceptionName}";
    internal string DeviceError(string error)
    {
        if (error?.StartsWith("read-error:", StringComparison.Ordinal) == true)
            return ReadFailure(error[11..]);
        return error switch
        {
            "устройство не найдено" => Ru ? "Устройство не найдено" : "Device not found",
            "мышь не отвечает" or "Нет ответа от мыши" => Ru ? "Мышь не отвечает" : "Mouse is not responding",
            "Ожидание первого опроса" => FirstPollPending,
            "diagnostic-only" => DiagnosticMode,
            _ => error
        };
    }
    internal string HistorySaveFailed => Ru ? "Не удалось сохранить историю. " : "Could not save history. ";
    internal string HistoryUnavailable => Ru ? "История недоступна; начат новый сбор. " : "History unavailable; starting a new record. ";
    internal string HistoryNote(double observedHours, int drop, bool hasRate)
    {
        var hours = observedHours.ToString("F1", Culture);
        if (Ru)
            return hasRate
                ? $"По {hours} ч наблюдений · снижение на {drop} п.п.\nПри прежнем режиме использования; долгие паузы исключены."
                : $"Наблюдения: {hours} ч · снижение на {drop} п.п.\n" +
                  (observedHours < .5 ? "Для оценки: 30 мин наблюдений и снижение на 2 п.п." : "Ждём снижения заряда на 2 п.п. для оценки.");
        return hasRate
            ? $"Based on {hours} h of observation · down {drop} percentage points.\nAssumes similar use; long pauses are excluded."
            : $"Observed: {hours} h · down {drop} percentage points.\n" +
              (observedHours < .5 ? "Needs 30 minutes and a 2-point drop for an estimate." : "Waiting for a 2-point drop to estimate runtime.");
    }
    internal string PreviewCharging => Ru ? "зарядка" : "charging";
    internal string PreviewLow => Ru ? "18% мало" : "18% low";
    internal string PreviewCritical => Ru ? "7% критично" : "7% critical";
    internal string PreviewUnknown => Ru ? "нет данных" : "no data";
}