#requires -Version 7
# One-shot probe: read battery from the LAMZU/Compx vendor feature collection.
# Command layout (from Aurora bundle, getBatPer):
#   payload[2]=2  payload[3]=2  payload[5]=0x83      -> report ID 0, 64-byte payload
# Response: payload[0]=0xA1, payload[3]=2, payload[5]=0x83, payload[6]=charging, payload[7]=percent
# On Windows HidD_* buffers carry the report ID in [0], so every index shifts by +1.

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Probe
{
    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, uint detSize, ref uint req, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static string FindVendorPath(ushort vid, ushort pid)
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
        string result = null;
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            uint need = 0;
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, ref need, IntPtr.Zero);
            if (need == 0) continue;
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            string path = null;
            try {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, ref need, IntPtr.Zero))
                    path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
            } finally { Marshal.FreeHGlobal(buf); }
            if (path == null) continue;

            IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == (IntPtr)(-1)) continue;
            try {
                var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
                if (!HidD_GetAttributes(h, ref a) || a.VendorID != vid || a.ProductID != pid) continue;
                IntPtr pp;
                if (!HidD_GetPreparsedData(h, out pp)) continue;
                try {
                    var caps = new HIDP_CAPS();
                    if (HidP_GetCaps(pp, ref caps) == HIDP_STATUS_SUCCESS
                        && caps.FeatureReportByteLength == 65 && caps.UsagePage >= 0xFF00)
                    { result = path; }
                } finally { HidD_FreePreparsedData(pp); }
            } finally { CloseHandle(h); }
            if (result != null) break;
        }
        SetupDiDestroyDeviceInfoList(set);
        return result;
    }

    public static string Read(string path, int attempts)
    {
        var log = new StringBuilder();
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == (IntPtr)(-1)) {
            log.AppendLine("rw open failed (" + Marshal.GetLastWin32Error() + "), retrying with no access");
            h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }
        if (h == (IntPtr)(-1)) return "open failed: " + Marshal.GetLastWin32Error();
        try {
            for (int k = 0; k < attempts; k++)
            {
                var tx = new byte[65];
                tx[0] = 0;      // report ID
                tx[3] = 2;      // payload[2] = device id
                tx[4] = 2;      // payload[3] = length
                tx[6] = 0x83;   // payload[5] = opcode (read BatteryLevel)
                if (!HidD_SetFeature(h, tx, 65)) {
                    log.AppendLine("attempt " + k + ": SetFeature failed " + Marshal.GetLastWin32Error());
                    System.Threading.Thread.Sleep(100); continue;
                }
                System.Threading.Thread.Sleep(100);
                var rx = new byte[65];
                rx[0] = 0;
                if (!HidD_GetFeature(h, rx, 65)) {
                    log.AppendLine("attempt " + k + ": GetFeature failed " + Marshal.GetLastWin32Error());
                    System.Threading.Thread.Sleep(100); continue;
                }
                log.AppendLine("attempt " + k + " raw: " + BitConverter.ToString(rx, 0, 16));
                if (rx[1] == 0xA1 && rx[4] == 2 && rx[6] == 0x83) {
                    log.AppendLine(">> MATCH layout A: charging=" + rx[7] + " percent=" + rx[8]);
                    return log.ToString();
                }
                if (rx[0] == 0xA1 && rx[3] == 2 && rx[5] == 0x83) {
                    log.AppendLine(">> MATCH layout B: charging=" + rx[6] + " percent=" + rx[7]);
                    return log.ToString();
                }
                System.Threading.Thread.Sleep(120);
            }
            log.AppendLine("no matching response");
            return log.ToString();
        } finally { CloseHandle(h); }
    }
}
'@ -ErrorAction Stop

$p = [Probe]::FindVendorPath(0x373E, 0x001E)
if (-not $p) { Write-Host "vendor collection not found" -ForegroundColor Red; exit 1 }
Write-Host "path: $p" -ForegroundColor Cyan
Write-Host ([Probe]::Read($p, 5))
