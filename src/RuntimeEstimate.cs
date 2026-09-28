using System.Text.Json;

namespace MayaXBattery;

// Sum only intervals bracketed by successful reads. Sleep/offline time is not measured use.
// A new segment breaks continuity without throwing away already measured discharge.
internal sealed class RuntimeEstimate
{
    static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(12);
    static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    readonly string _path;
    History _history = new();
    Segment _active;
    DateTimeOffset? _lastObservation;
    bool _storageProblem, _loadProblem;

    internal RuntimeEstimate(string path = null)
    {
        _path = path;
        if (path == null) return;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException();
            var loaded = JsonSerializer.Deserialize<History>(File.ReadAllText(path));
            if (loaded == null || loaded.Version != 1 || loaded.Segments == null ||
                loaded.Segments.Count > 512 || loaded.Device?.Length > 512 ||
                loaded.Segments.Any(s => s == null || s.Start > s.End ||
                    s.End - s.Start > Retention || s.Initial is < 1 or > 100 ||
                    s.Minimum < 1 || s.Minimum > s.Initial))
                throw new InvalidDataException();
            _history = loaded;
            // Restart always breaks continuity: an unobserved charge may have happened.
        }
        catch (Exception ex) when (IsStorageError(ex)) { _loadProblem = true; }
    }

    internal double ObservedHours => _history.Segments.Sum(s => (s.End - s.Start).TotalHours);
    internal int Drop => _history.Segments.Sum(s => s.Initial - s.Minimum);
    internal double? Rate => ObservedHours >= .5 && Drop >= 2 ? Drop / ObservedHours : null;
    internal string Note => GetNote(UiLanguage.Russian);

    internal string GetNote(UiLanguage language)
    {
        var text = new UiStrings(language);
        return (_storageProblem ? text.HistorySaveFailed : _loadProblem && !Rate.HasValue ? text.HistoryUnavailable : "") +
            text.HistoryNote(ObservedHours, Drop, Rate.HasValue);
    }

    internal double? Observe(Reading reading, DateTimeOffset now)
    {
        // Do not let out-of-order timestamps extend a sample or erase useful history.
        if (_lastObservation.HasValue && now <= _lastObservation.Value) return null;
        _lastObservation = now;
        _history.Segments.RemoveAll(s => s.Start < now - Retention || s.End > now);
        if (_active != null && !_history.Segments.Contains(_active)) _active = null;
        if (!reading.Ok)
        {
            // A brief timeout may be bridged by the next valid read; a long gap is split below.
            Save();
            return null;
        }
        if (reading.Percent is < 1 or > 100 || string.IsNullOrWhiteSpace(reading.Device)) return null;
        if (_history.Device != reading.Device)
        {
            _history = new History { Device = reading.Device };
            _active = null;
        }
        if (reading.Charging)
        {
            _active = null;
            Save();
            return null;
        }
        if (_active == null || now - _active.End > MaxGap || reading.Percent > _active.Minimum + 2)
        {
            _active = new Segment { Start = now, End = now, Initial = reading.Percent, Minimum = reading.Percent };
            _history.Segments.Add(_active);
            if (_history.Segments.Count > 512) _history.Segments.RemoveAt(0);
        }
        else
        {
            if (now - _active.Start >= TimeSpan.FromDays(1))
            {
                _active = new Segment { Start = _active.End, End = _active.End, Initial = _active.Minimum, Minimum = _active.Minimum };
                _history.Segments.Add(_active);
                if (_history.Segments.Count > 512) _history.Segments.RemoveAt(0);
            }
            _active.End = now;
            // Ignore one/two-point upward sensor jitter; never count its return as new discharge.
            _active.Minimum = Math.Min(_active.Minimum, reading.Percent);
        }
        Save();
        return Rate is double rate ? reading.Percent / rate : null;
    }

    void Save()
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_history));
            File.Move(temporary, _path, overwrite: true);
            _storageProblem = false;
        }
        catch (Exception ex) when (IsStorageError(ex)) { _storageProblem = true; }
    }

    static bool IsStorageError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or System.Security.SecurityException or JsonException or NotSupportedException;

    internal sealed class History
    {
        public int Version { get; set; } = 1;
        public string Device { get; set; }
        public List<Segment> Segments { get; set; } = new();
    }

    internal sealed class Segment
    {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public int Initial { get; set; }
        public int Minimum { get; set; }
    }
}