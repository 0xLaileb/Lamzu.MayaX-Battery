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
        var path = _cachedPath;
        var name = _cachedName;

        if (path == null && !Locate(out path, out name))
            return Reading.Fail("устройство не найдено");

        // Up to four attempts: the dongle occasionally answers with a stale or empty frame,
        // which is why Aurora retries too.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var tx = new byte[ReportLen];
            tx[0] = 0;               // report ID
            tx[3] = 2;               // payload[2] — device id
            tx[4] = 2;               // payload[3] — payload length
            tx[6] = OpReadBattery;   // payload[5] — opcode

            var rx = Hid.FeatureExchange(path, tx, ReportLen, 100);
            if (rx == null)
            {
                // Device may have been re-enumerated (unplugged, dongle re-seated).
                _cachedPath = null;
                if (!Locate(out path, out name)) return Reading.Fail("устройство не найдено");
                continue;
            }

            if (TryParse(rx, out int percent, out bool charging))
            {
                _cachedPath = path;
                _cachedName = name;
                return new Reading(percent, charging, name, null);
            }
            // percent 0 is the firmware's "no answer" sentinel (Aurora returns [0,0] the same way)
            // rather than a genuinely flat battery, so keep retrying instead of reporting 0%.
            Thread.Sleep(150);
        }

        _cachedPath = path;
        _cachedName = name;
        return Reading.Fail("мышь не отвечает");
    }

    internal static bool TryParse(byte[] rx, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (rx == null || rx.Length < 8) return false;
        int offset = rx[0] == ReplyMagic ? 0 : 1;
        if (rx.Length < offset + 8 || rx[offset] != ReplyMagic ||
            rx[offset + 3] != 2 || rx[offset + 5] != OpReadBattery || rx[offset + 6] > 1)
            return false;
        percent = rx[offset + 7];
        charging = rx[offset + 6] == 1;
        return percent is >= 1 and <= 100;
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

