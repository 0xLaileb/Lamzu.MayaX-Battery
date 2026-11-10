namespace MayaXBattery;

/// <summary>
/// Decides when a low or critical reading deserves one notification. Each level is announced
/// once; only a clear recovery above the low range re-arms it, so a short top-up does not repeat
/// the notification. A failed read changes nothing, because an unreadable device says nothing
/// about its charge.
/// </summary>
internal sealed class LowBatteryAlert
{
    // Sensor jitter around the threshold must not repeat the notification.
    const int RearmPercent = Reading.LowPercent + 2;
    ChargeState _announced = ChargeState.Normal;

    internal ChargeState? Observe(Reading reading)
    {
        if (!reading.Ok) return null;
        if (reading.Percent > RearmPercent)
        {
            _announced = ChargeState.Normal;
            return null;
        }
        // Charging is never announced; Normal here is only the jitter band just above the threshold.
        var state = reading.State;
        if (state is not (ChargeState.Low or ChargeState.Critical)) return null;
        if (_announced == ChargeState.Critical || _announced == state) return null;
        _announced = state;
        return state;
    }
}