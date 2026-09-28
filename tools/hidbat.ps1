#requires -Version 7
<#
  HID battery scanner.
  Default: parses report descriptors only (no traffic to devices).
  -Read : additionally issues GET_FEATURE for collections that declare a battery
          usage inside a feature report (read-only control transfer).
#>
param([switch]$Read, [string]$Dump)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Hid
{
    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS {
        public ushort UsagePage;
        public byte ReportID;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
        public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public bool IsRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)] public bool HasNull;
        public byte Reserved;
        public ushort BitSize, ReportCount;
        public ushort R0, R1, R2, R3, R4;
        public uint UnitsExp, Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesigMin, DesigMax, DataIdxMin, DataIdxMax;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(IntPtr h, StringBuilder b, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder b, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int reportType, [Out] HIDP_BUTTON_CAPS[] caps, ref ushort len, IntPtr pp);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_BUTTON_CAPS {
        public ushort UsagePage;
        public byte ReportID;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
        public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public bool IsRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
        public uint R0, R1, R2, R3, R4, R5, R6, R7, R8, R9;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesigMin, DesigMax, DataIdxMin, DataIdxMax;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, uint detSize, ref uint req, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public class ValueEntry {
        public string ReportType; public ushort UsagePage; public ushort Usage;
        public byte ReportID; public ushort BitSize; public int LogicalMin; public int LogicalMax;
        public ushort LinkUsagePage; public ushort LinkUsage;
    }

    public class DevEntry {
        public string Path; public ushort Vid, Pid, Ver;
        public string Manufacturer = "", Product = "";
        public ushort UsagePage, Usage;
        public int InLen, OutLen, FeatLen;
        public bool Opened; public string Error = "";
        public List<ValueEntry> Battery = new List<ValueEntry>();
        public int TotalValueCaps;
        public int TotalButtonCaps;
        public List<string> AllValues = new List<string>();
    }

    static List<string> Paths()
    {
        Guid g; HidD_GetHidGuid(out g);
        var res = new List<string>();
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == (IntPtr)(-1)) return res;
        var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            uint need = 0;
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, ref need, IntPtr.Zero);
            if (need == 0) continue;
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            try {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, ref need, IntPtr.Zero))
                    res.Add(Marshal.PtrToStringUni(IntPtr.Add(buf, 4)));
            } finally { Marshal.FreeHGlobal(buf); }
        }
        SetupDiDestroyDeviceInfoList(set);
        return res;
    }

    // battery-related HID usages
    static bool IsBattery(ushort page, ushort usage)
    {
        if (page == 0x06 && usage == 0x20) return true;   // Generic Device Controls / Battery Strength
        if (page == 0x84 || page == 0x85) return true;    // Power Device / Battery System pages
        return false;
    }

    static void Collect(IntPtr pp, int type, string name, DevEntry e)
    {
        var caps = new HIDP_CAPS();
        if (HidP_GetCaps(pp, ref caps) != HIDP_STATUS_SUCCESS) return;
        ushort n = type == 0 ? caps.NumberInputValueCaps : (type == 1 ? caps.NumberOutputValueCaps : caps.NumberFeatureValueCaps);
        if (n == 0) return;
        var arr = new HIDP_VALUE_CAPS[n];
        ushort len = n;
        if (HidP_GetValueCaps(type, arr, ref len, pp) != HIDP_STATUS_SUCCESS) return;
        e.TotalValueCaps += len;
        for (int i = 0; i < len; i++)
        {
            ushort u = arr[i].IsRange ? arr[i].UsageMin : arr[i].UsageMin; // NotRange.Usage overlays UsageMin
            e.AllValues.Add(string.Format("{0} page 0x{1:X2} usage 0x{2:X2} id {3} {4}bit {5}..{6}",
                name, arr[i].UsagePage, u, arr[i].ReportID, arr[i].BitSize, arr[i].LogicalMin, arr[i].LogicalMax));
            if (!IsBattery(arr[i].UsagePage, u)) continue;
            e.Battery.Add(new ValueEntry {
                ReportType = name, UsagePage = arr[i].UsagePage, Usage = u,
                ReportID = arr[i].ReportID, BitSize = arr[i].BitSize,
                LogicalMin = arr[i].LogicalMin, LogicalMax = arr[i].LogicalMax,
                LinkUsagePage = arr[i].LinkUsagePage, LinkUsage = arr[i].LinkUsage
            });
        }
    }

    static void CollectButtons(IntPtr pp, int type, string name, DevEntry e)
    {
        var caps = new HIDP_CAPS();
        if (HidP_GetCaps(pp, ref caps) != HIDP_STATUS_SUCCESS) return;
        ushort n = type == 0 ? caps.NumberInputButtonCaps : caps.NumberFeatureButtonCaps;
        if (n == 0) return;
        var arr = new HIDP_BUTTON_CAPS[n];
        ushort len = n;
        if (HidP_GetButtonCaps(type, arr, ref len, pp) != HIDP_STATUS_SUCCESS) return;
        e.TotalButtonCaps += len;
        for (int i = 0; i < len; i++)
        {
            ushort lo = arr[i].UsageMin, hi = arr[i].IsRange ? arr[i].UsageMax : arr[i].UsageMin;
            bool hit = false;
            if (arr[i].UsagePage == 0x84 || arr[i].UsagePage == 0x85) hit = true;
            if (arr[i].UsagePage == 0x06 && lo <= 0x20 && hi >= 0x20) hit = true;
            if (!hit) continue;
            e.Battery.Add(new ValueEntry {
                ReportType = name, UsagePage = arr[i].UsagePage, Usage = lo,
                ReportID = arr[i].ReportID, BitSize = 1, LogicalMin = lo, LogicalMax = hi,
                LinkUsagePage = arr[i].LinkUsagePage, LinkUsage = arr[i].LinkUsage
            });
        }
    }

    public static List<DevEntry> Scan()
    {
        var list = new List<DevEntry>();
        foreach (var p in Paths())
        {
            var e = new DevEntry { Path = p };
            IntPtr h = CreateFile(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == (IntPtr)(-1)) { e.Error = "open failed (" + Marshal.GetLastWin32Error() + ")"; list.Add(e); continue; }
            e.Opened = true;
            try {
                var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
                if (HidD_GetAttributes(h, ref a)) { e.Vid = a.VendorID; e.Pid = a.ProductID; e.Ver = a.VersionNumber; }
                var sb = new StringBuilder(256);
                if (HidD_GetProductString(h, sb, 512)) e.Product = sb.ToString();
                sb.Clear();
                if (HidD_GetManufacturerString(h, sb, 512)) e.Manufacturer = sb.ToString();
                IntPtr pp;
                if (HidD_GetPreparsedData(h, out pp)) {
                    try {
                        var caps = new HIDP_CAPS();
                        if (HidP_GetCaps(pp, ref caps) == HIDP_STATUS_SUCCESS) {
                            e.UsagePage = caps.UsagePage; e.Usage = caps.Usage;
                            e.InLen = caps.InputReportByteLength;
                            e.OutLen = caps.OutputReportByteLength;
                            e.FeatLen = caps.FeatureReportByteLength;
                        }
                        Collect(pp, 0, "Input", e);
                        Collect(pp, 2, "Feature", e);
                        CollectButtons(pp, 0, "InputBtn", e);
                        CollectButtons(pp, 2, "FeatBtn", e);
                    } finally { HidD_FreePreparsedData(pp); }
                } else e.Error = "no preparsed data";
            } finally { CloseHandle(h); }
            list.Add(e);
        }
        return list;
    }

    // read-only GET_FEATURE; only called explicitly
    public static string ReadFeature(string path, byte reportId, int len)
    {
        IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == (IntPtr)(-1)) return "ERR open";
        try {
            var buf = new byte[len]; buf[0] = reportId;
            if (!HidD_GetFeature(h, buf, len)) return "ERR " + Marshal.GetLastWin32Error();
            return BitConverter.ToString(buf);
        } finally { CloseHandle(h); }
    }
}
'@ -ErrorAction Stop

$vendors = @{
  0x046D = 'Logitech';   0x1532 = 'Razer';      0x1B1C = 'Corsair';    0x1038 = 'SteelSeries'
  0x0C45 = 'Sonix/Compx';0x373E = 'LAMZU/Compx';0x3554 = 'Compx';      0x25A7 = 'Compx (Areson)'
  0x05AC = 'Apple';      0x26CE = 'ASUS/Astro'; 0x2972 = 'unknown';    0x043E = 'LG'
  0x093A = 'Pixart';     0x30FA = 'Generic';    0x1A2C = 'China Resource'
}
$pages = @{
  0x01='Generic Desktop'; 0x0C='Consumer'; 0x06='Generic Device Ctl'; 0x07='Keyboard'
  0x08='LED'; 0x84='Power Device'; 0x85='Battery System'
}

$devs = [Hid]::Scan()
Write-Host ("HID interfaces found: {0}   (opened: {1})" -f $devs.Count, ($devs | Where-Object Opened).Count) -ForegroundColor Cyan
Write-Host ""

$rows = foreach ($d in $devs) {
    $vend = if ($vendors.ContainsKey([int]$d.Vid)) { $vendors[[int]$d.Vid] } else { '' }
    $pg = if ($pages.ContainsKey([int]$d.UsagePage)) { $pages[[int]$d.UsagePage] }
          elseif ($d.UsagePage -ge 0xFF00) { 'Vendor' } else { ('0x{0:X2}' -f $d.UsagePage) }
    [pscustomobject]@{
        VIDPID  = ('{0:X4}:{1:X4}' -f $d.Vid, $d.Pid)
        Vendor  = $vend
        Product = if ($d.Product) { $d.Product } else { '-' }
        TopColl = ('{0}/0x{1:X2}' -f $pg, $d.Usage)
        In      = $d.InLen; Out = $d.OutLen; Feat = $d.FeatLen
        Values  = $d.TotalValueCaps; Btns = $d.TotalButtonCaps
        Battery = if ($d.Battery.Count) { 'YES' } else { '' }
        Note    = $d.Error
    }
}
$rows | Sort-Object VIDPID, TopColl | Format-Table -AutoSize

if ($Dump) {
    Write-Host "=== Parser self-check: all value usages for $Dump ===" -ForegroundColor Cyan
    foreach ($d in ($devs | Where-Object { ('{0:X4}:{1:X4}' -f $_.Vid, $_.Pid) -eq $Dump })) {
        Write-Host ("-- topcoll 0x{0:X2}/0x{1:X2}  in={2} feat={3}" -f $d.UsagePage, $d.Usage, $d.InLen, $d.FeatLen) -ForegroundColor Yellow
        foreach ($v in $d.AllValues) { Write-Host ("     " + $v) }
    }
    Write-Host ""
}

Write-Host "=== Collections declaring a battery usage ===" -ForegroundColor Cyan
$hits = $devs | Where-Object { $_.Battery.Count -gt 0 }
if (-not $hits) {
    Write-Host "none" -ForegroundColor Yellow
} else {
    foreach ($d in $hits) {
        Write-Host ("{0:X4}:{1:X4}  {2}" -f $d.Vid, $d.Pid, $d.Product) -ForegroundColor Green
        foreach ($b in $d.Battery) {
            Write-Host ("   {0,-7} page 0x{1:X2} usage 0x{2:X2}  reportID {3}  {4} bit  logical {5}..{6}" -f `
                $b.ReportType, $b.UsagePage, $b.Usage, $b.ReportID, $b.BitSize, $b.LogicalMin, $b.LogicalMax)
        }
        if ($Read) {
            foreach ($b in ($d.Battery | Where-Object ReportType -eq 'Feature')) {
                $raw = [Hid]::ReadFeature($d.Path, $b.ReportID, $d.FeatLen)
                Write-Host ("   GET_FEATURE id {0}: {1}" -f $b.ReportID, $raw) -ForegroundColor DarkGray
            }
        }
    }
}
