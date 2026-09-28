using System.Runtime.InteropServices;
using System.Text;

namespace MayaXBattery;

/// <summary>Minimal HID interop: enumerate interfaces, read/write feature reports.</summary>
internal static class Hid
{
    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const int HIDP_STATUS_SUCCESS = 0x00110000;
    static readonly IntPtr INVALID = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(IntPtr h, StringBuilder b, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder b, int len);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, uint detSize, ref uint req, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    internal sealed record Collection(string Path, ushort Vid, ushort Pid, string Product,
                                      ushort UsagePage, ushort Usage, int FeatureLen, int InputLen,
                                      string Manufacturer = "", ushort VersionNumber = 0, int OutputLen = 0,
                                      bool CapsAvailable = true);

    internal sealed record DiagnosticEnumeration(int TotalInterfaces, int InspectedCollections,
        IReadOnlyList<Collection> Collections, IReadOnlyList<DiagnosticIssue> Issues);

    static IEnumerable<string> InterfacePaths()
    {
        HidD_GetHidGuid(out Guid g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID) yield break;
        try
        {
            var d = new SP_DEVICE_INTERFACE_DATA();
            d.cbSize = Marshal.SizeOf(d);
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
            {
                uint need = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                if (need == 0) continue;
                IntPtr buf = Marshal.AllocHGlobal((int)need);
                string path = null;
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, ref need, IntPtr.Zero))
                        path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (path != null) yield return path;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    /// <summary>Enumerates every present HID collection. Opens with zero access rights,
    /// which Windows permits even for mouse/keyboard collections it holds exclusively.</summary>
    internal static List<Collection> Enumerate()
    {
        var list = new List<Collection>();
        foreach (var path in InterfacePaths())
        {
            IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID) continue;
            try
            {
                var a = new HIDD_ATTRIBUTES();
                a.Size = Marshal.SizeOf(a);
                if (!HidD_GetAttributes(h, ref a)) continue;
                if (!HidD_GetPreparsedData(h, out IntPtr pp)) continue;
                try
                {
                    var caps = new HIDP_CAPS();
                    if (HidP_GetCaps(pp, ref caps) != HIDP_STATUS_SUCCESS) continue;
                    var sb = new StringBuilder(128);
                    string product = HidD_GetProductString(h, sb, 256) ? sb.ToString() : "";
                    list.Add(new Collection(path, a.VendorID, a.ProductID, product,
                                            caps.UsagePage, caps.Usage,
                                            caps.FeatureReportByteLength, caps.InputReportByteLength));
                }
                finally { HidD_FreePreparsedData(pp); }
            }
            finally { CloseHandle(h); }
        }
        return list;
    }

    /// <summary>Descriptor-only enumeration. Paths remain in memory and are never exported.</summary>
    internal static DiagnosticEnumeration EnumerateForDiagnostics()
    {
        var collections = new List<Collection>();
        var issues = new List<DiagnosticIssue>();
        int total = 0, inspected = 0;
        static void Issue(List<DiagnosticIssue> list, string stage, string status, int? win32 = null,
            int? native = null, string path = null)
        {
            if (list.Count < 128)
                list.Add(new DiagnosticIssue { AnonymousId = DiagnosticLog.AnonymousId(path),
                    Stage = stage, Status = status, Win32Error = win32, NativeStatus = native });
        }

        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID)
        {
            Issue(issues, "setup.get-class-devices", "failed", Marshal.GetLastPInvokeError());
            return new DiagnosticEnumeration(0, 0, collections, issues);
        }
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; i < 4096; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != 259) Issue(issues, "setup.enum-interface", "failed", error);
                    break;
                }
                total++;
                uint needed = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, ref needed, IntPtr.Zero);
                int sizeError = Marshal.GetLastPInvokeError();
                if (needed < 6 || needed > 65536)
                {
                    Issue(issues, "setup.detail-size", "invalid-or-unavailable", sizeError);
                    continue;
                }
                IntPtr buffer = Marshal.AllocHGlobal((int)needed);
                string path;
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, needed, ref needed, IntPtr.Zero))
                    {
                        Issue(issues, "setup.detail", "failed", Marshal.GetLastPInvokeError());
                        continue;
                    }
                    path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                }
                finally { Marshal.FreeHGlobal(buffer); }
                if (string.IsNullOrEmpty(path))
                {
                    Issue(issues, "setup.detail", "empty-path");
                    continue;
                }
                IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == INVALID)
                {
                    Issue(issues, "hid.open-descriptor", "failed", Marshal.GetLastPInvokeError(), path: path);
                    continue;
                }
                try
                {
                    var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(handle, ref attributes))
                    {
                        Issue(issues, "hid.attributes", "failed", Marshal.GetLastPInvokeError(), path: path);
                        continue;
                    }
                    // Names help identify other Maya revisions. Never request serial strings.
                    var productBuffer = new StringBuilder(128);
                    var manufacturerBuffer = new StringBuilder(128);
                    bool hasProduct = HidD_GetProductString(handle, productBuffer, 256);
                    bool hasManufacturer = HidD_GetManufacturerString(handle, manufacturerBuffer, 256);
                    string product = hasProduct ? productBuffer.ToString() : "";
                    string manufacturer = hasManufacturer ? manufacturerBuffer.ToString() : "";
                    bool candidate = attributes.VendorID is 0x373E or 0x3554 ||
                        product.Contains("MAYA", StringComparison.OrdinalIgnoreCase) ||
                        product.Contains("LAMZU", StringComparison.OrdinalIgnoreCase) ||
                        manufacturer.Contains("LAMZU", StringComparison.OrdinalIgnoreCase);
                    if (!candidate) continue;
                    inspected++;
                    if (!hasProduct) Issue(issues, "hid.product-string", "unavailable", path: path);
                    if (!hasManufacturer) Issue(issues, "hid.manufacturer-string", "unavailable", path: path);
                    if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
                    {
                        Issue(issues, "hid.preparsed-data", "failed", Marshal.GetLastPInvokeError(), path: path);
                        collections.Add(new Collection(path, attributes.VendorID, attributes.ProductID,
                            product, 0, 0, 0, 0, manufacturer, attributes.VersionNumber,
                            CapsAvailable: false));
                        continue;
                    }
                    try
                    {
                        var caps = new HIDP_CAPS();
                        int status = HidP_GetCaps(preparsed, ref caps);
                        if (status != HIDP_STATUS_SUCCESS)
                        {
                            Issue(issues, "hid.caps", "failed", native: status, path: path);
                            collections.Add(new Collection(path, attributes.VendorID, attributes.ProductID,
                                product, 0, 0, 0, 0, manufacturer, attributes.VersionNumber,
                                CapsAvailable: false));
                            continue;
                        }
                        collections.Add(new Collection(path, attributes.VendorID, attributes.ProductID, product,
                            caps.UsagePage, caps.Usage, caps.FeatureReportByteLength,
                            caps.InputReportByteLength, manufacturer, attributes.VersionNumber,
                            caps.OutputReportByteLength));
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
                }
                finally { CloseHandle(handle); }
            }
            if (total == 4096) Issue(issues, "setup.enum-interface", "limit-reached");
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return new DiagnosticEnumeration(total, inspected, collections, issues);
    }

    /// <summary>Sends a feature report, waits, then reads one back. Returns null on any failure.</summary>
    internal static byte[] FeatureExchange(string path, byte[] request, int responseLen, int delayMs, int retry = 0)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == INVALID)
            h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == INVALID)
        {
            DiagnosticLog.Record("poll.open", $"failed-win32-{Marshal.GetLastPInvokeError()}",
                watch.ElapsedMilliseconds, retry, path: path);
            return null;
        }
        try
        {
            if (!HidD_SetFeature(h, request, request.Length))
            {
                DiagnosticLog.Record("poll.set-feature", $"failed-win32-{Marshal.GetLastPInvokeError()}",
                    watch.ElapsedMilliseconds, retry, path: path);
                return null;
            }
            Thread.Sleep(delayMs);
            var rx = new byte[responseLen];
            rx[0] = request[0];             // report ID
            if (!HidD_GetFeature(h, rx, responseLen))
            {
                DiagnosticLog.Record("poll.get-feature", $"failed-win32-{Marshal.GetLastPInvokeError()}",
                    watch.ElapsedMilliseconds, retry, path: path);
                return null;
            }
            return rx;
        }
        finally { CloseHandle(h); }
    }
}
