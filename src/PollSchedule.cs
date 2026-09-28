namespace MayaXBattery;

internal sealed class PollSchedule
{
    int _failures;
    internal int DelaySeconds { get; private set; } = 60;

    internal void Observe(bool success)
    {
        // Four quick recovery checks, then back off for a sleeping or unplugged device.
        _failures = success ? 0 : Math.Min(_failures + 1, 5);
        DelaySeconds = _failures is >= 1 and <= 4 ? 15 : 60;
    }
}
