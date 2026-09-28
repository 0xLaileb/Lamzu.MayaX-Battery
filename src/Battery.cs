namespace MayaXBattery;

internal readonly record struct Reading(int Percent, bool Charging, string Device, string Error)
{
    internal bool Ok => Error == null;
    internal static Reading Fail(string why) => new(0, false, null, why);
}

/// <summary>
/// Compx / Xvalley vendor protocol, as used by LAMZU Aurora and the many brands built on
/// the same platform (VXE, Ajazz, Attack Shark, Darmoshark, Incott ...).
///
/// Verified against LAMZU Maya X (373E:001E) on 2026-09-03: Aurora reported 50%, this read 50%.
///
/// Wire format is a 64-byte payload carried in feature report 0, so Windows buffers are 65
/// bytes with the report ID at [0] and every payload index shifted by +1.
///
///   request   payload[2]=2 (device)  payload[3]=2 (len)  payload[5]=0x83 (read BatteryLevel)
///   response  payload[0]=0xA1  payload[3]=2  payload[5]=0x83
///             payload[6]=charging flag   payload[7]=percent
///
/// Aurora's own UI does exactly this: ChargeStatus = c[0]==1, batPer = c[1].
/// </summary>
internal sealed class CompxSource
{
    const byte OpReadBattery = 0x83;
    const byte ReplyMagic = 0xA1;
    const int ReportLen = 65;

    // Vendors known to ship this protocol. Extend as devices are confirmed.
    static readonly Dictionary<ushort, string> KnownVendors = new()
    {
        [0x373E] = "LAMZU",
    };

    string _cachedPath;
    string _cachedName;

    public Reading Read()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var path = _cachedPath;
        var name = _cachedName;

        if (path == null && !Locate(out path, out name))
        {
            DiagnosticLog.Record("poll.locate", "no-supported-collection", watch.ElapsedMilliseconds);
            return Reading.Fail("устройство не найдено");
        }

        // Up to four attempts: the dongle occasionally answers with a stale or empty frame,
        // which is why Aurora retries too.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var tx = new byte[ReportLen];
            tx[0] = 0;               // report ID
            tx[3] = 2;               // payload[2] — device id
            tx[4] = 2;               // payload[3] — payload length
            tx[6] = OpReadBattery;   // payload[5] — opcode

            var rx = Hid.FeatureExchange(path, tx, ReportLen, 100, attempt + 1);
            if (rx == null)
            {
                DiagnosticLog.Record("poll.exchange", "no-response", watch.ElapsedMilliseconds, attempt + 1, path: path);
                // Device may have been re-enumerated (unplugged, dongle re-seated).
                _cachedPath = null;
                if (!Locate(out path, out name))
                {
                    DiagnosticLog.Record("poll.relocate", "no-supported-collection", watch.ElapsedMilliseconds, attempt + 1, path: path);
                    return Reading.Fail("устройство не найдено");
                }
                continue;
            }

            if (TryParseDetailed(rx, out int percent, out bool charging, out string rejection))
            {
                DiagnosticLog.Record("poll.parse", "ok", watch.ElapsedMilliseconds, attempt + 1, rx, path);
                _cachedPath = path;
                _cachedName = name;
                return new Reading(percent, charging, name, null);
            }
            DiagnosticLog.Record("poll.parse", rejection, watch.ElapsedMilliseconds, attempt + 1,
                RecognizedBatteryEnvelope(rx) ? rx : null, path);
            // percent 0 is the firmware's "no answer" sentinel (Aurora returns [0,0] the same way)
            // rather than a genuinely flat battery, so keep retrying instead of reporting 0%.
            Thread.Sleep(150);
        }

        _cachedPath = path;
        _cachedName = name;
        DiagnosticLog.Record("poll.result", "retries-exhausted", watch.ElapsedMilliseconds, 4, path: path);
        return Reading.Fail("мышь не отвечает");
    }

    internal static bool TryParse(byte[] rx, out int percent, out bool charging)
        => TryParseDetailed(rx, out percent, out charging, out _);

    static bool RecognizedBatteryEnvelope(byte[] rx)
    {
        if (rx == null || rx.Length < 8) return false;
        int offset = rx[0] == ReplyMagic ? 0 : 1;
        return rx.Length >= offset + 8 && rx[offset] == ReplyMagic &&
            rx[offset + 3] == 2 && rx[offset + 5] == OpReadBattery;
    }

    internal static bool TryParseDetailed(byte[] rx, out int percent, out bool charging, out string rejection)
    {
        percent = 0;
        charging = false;
        rejection = "";
        if (rx == null || rx.Length < 8) { rejection = "short-response"; return false; }
        int offset = rx[0] == ReplyMagic ? 0 : 1;
        if (rx.Length < offset + 8) { rejection = "short-response"; return false; }
        if (rx[offset] != ReplyMagic) { rejection = "wrong-marker"; return false; }
        if (rx[offset + 3] != 2) { rejection = "wrong-length"; return false; }
        if (rx[offset + 5] != OpReadBattery) { rejection = "wrong-opcode"; return false; }
        if (rx[offset + 6] > 1) { rejection = "invalid-charging-flag"; return false; }
        percent = rx[offset + 7];
        charging = rx[offset + 6] == 1;
        if (percent is >= 1 and <= 100) return true;
        rejection = percent == 0 ? "zero-percent-sentinel" : "invalid-percent";
        return false;
    }

    static bool Locate(out string path, out string name)
    {
        path = null;
        name = null;
        foreach (var c in Hid.Enumerate())
        {
            if (!KnownVendors.ContainsKey(c.Vid)) continue;
            // The control channel is the vendor-defined collection carrying a 64-byte feature report.
            if (c.FeatureLen != ReportLen || c.UsagePage < 0xFF00) continue;
            path = c.Path;
            name = string.IsNullOrWhiteSpace(c.Product) ? KnownVendors[c.Vid] : c.Product;
            return true;
        }
        return false;
    }
}

