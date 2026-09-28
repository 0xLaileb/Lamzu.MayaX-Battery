namespace MayaXBattery;

internal sealed class DiagnosticEvent
{
    public DateTimeOffset Utc { get; init; }
    public string AnonymousId { get; init; } = "";
    public string Stage { get; init; } = "";
    public string Status { get; init; } = "";
    public long ElapsedMs { get; init; }
    public int Retry { get; init; }
    public string ResponsePrefix { get; init; } = "";
}

/// <summary>Bounded, process-local observations of the existing Maya X poll only.</summary>
internal static class DiagnosticLog
{
    const int Capacity = 128;
    static readonly object Gate = new();
    static readonly Queue<DiagnosticEvent> Events = new();
    static readonly Dictionary<string, string> Ids = new(StringComparer.OrdinalIgnoreCase);

    internal static string AnonymousId(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        lock (Gate)
        {
            if (Ids.TryGetValue(path, out string id)) return id;
            if (Ids.Count >= 512) Ids.Clear();
            do
            {
                id = "D" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
            } while (Ids.ContainsValue(id));
            Ids[path] = id;
            return id;
        }
    }

    internal static void Record(string stage, string status, long elapsedMs = 0, int retry = 0,
        byte[] response = null, string path = null)
    {
        var prefix = response is null ? "" : Convert.ToHexString(response.AsSpan(0, Math.Min(response.Length, 9)));
        string id = AnonymousId(path);
        lock (Gate)
        {
            if (Events.Count == Capacity) Events.Dequeue();
            Events.Enqueue(new DiagnosticEvent
            {
                Utc = DateTimeOffset.UtcNow,
                AnonymousId = id,
                Stage = stage,
                Status = status,
                ElapsedMs = Math.Clamp(elapsedMs, 0, 60_000),
                Retry = Math.Clamp(retry, 0, 4),
                ResponsePrefix = prefix
            });
        }
    }

    internal static IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (Gate) return Events.ToArray();
    }
}