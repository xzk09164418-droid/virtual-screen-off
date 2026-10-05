using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel;

public static class DisplayNative
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Source { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct Target
    {
        public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh; public uint ScanLine; public int Available; public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Path { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] public struct Mode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public SourceMode Source;
        // Preserve every byte in the native union, including target signal timing.
        [FieldOffset(16)] public ulong U0; [FieldOffset(24)] public ulong U1;
        [FieldOffset(32)] public ulong U2; [FieldOffset(40)] public ulong U3;
        [FieldOffset(48)] public ulong U4; [FieldOffset(56)] public ulong U5;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    // Private per-source DPI packets used by Windows Settings; bounds-checked and
    // limited to the test virtual display. Reference: lihas/windows-DPI-scaling-sample.
    [StructLayout(LayoutKind.Sequential)] struct DpiGet { public Header Header; public int Min, Current, Max; }
    [StructLayout(LayoutKind.Sequential)] struct DpiSet { public Header Header; public int Relative; }
    static readonly int[] ScaleValues = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName
    {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct AdapterName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra; public uint Fields;
        public int X, Y; public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels; public uint BitsPerPel, Width, Height, DisplayFlags, Frequency;
        public uint ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    public class Config { public Path[] Paths; public Mode[] Modes; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] Path[] pathArray, ref uint modes, [Out] Mode[] modeArray, IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, [In] Path[] pathArray, uint modes, [In] Mode[] modeArray, uint flags);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetAdapterName(ref AdapterName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetDpi(ref DpiGet value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] static extern int SetDpi(ref DpiSet value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DevMode value);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint state);

    public static Config Query(bool all)
    {
        if (Marshal.SizeOf(typeof(Path)) != 72 || Marshal.SizeOf(typeof(Mode)) != 64) throw new Exception("Unexpected native structure size.");
        uint flags = all ? 1u : 2u;
        for (int retry = 0; retry < 5; retry++)
        {
            uint p, m; Check(GetDisplayConfigBufferSizes(flags, out p, out m));
            Path[] paths = new Path[p]; Mode[] modes = new Mode[m];
            int rc = QueryDisplayConfig(flags, ref p, paths, ref m, modes, IntPtr.Zero);
            if (rc == 122) continue;
            Check(rc); Array.Resize(ref paths, (int)p); Array.Resize(ref modes, (int)m);
            return new Config { Paths = paths, Modes = modes };
        }
        throw new Exception("Display topology is changing. Try again.");
    }
    public static TargetName Name(Path p)
    {
        TargetName n = new TargetName(); n.Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = p.Target.Adapter, Id = p.Target.Id };
        Check(GetTargetName(ref n)); return n;
    }
    public static string GdiName(Path p)
    {
        SourceName n = new SourceName(); n.Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = p.Source.Adapter, Id = p.Source.Id };
        Check(GetSourceName(ref n)); return n.Name;
    }
    public static bool Internal(Path p) { return p.Target.Technology == 0x80000000 || p.Target.Technology == 11 || p.Target.Technology == 6; }
    public static string AdapterPath(Path p)
    {
        AdapterName n = new AdapterName(); n.Header = new Header { Type = 4, Size = (uint)Marshal.SizeOf(typeof(AdapterName)), Adapter = p.Target.Adapter };
        Check(GetAdapterName(ref n)); return n.DevicePath;
    }
    public static SourceMode GetSource(Config c, Path p)
    {
        if (p.Source.ModeIndex >= c.Modes.Length || c.Modes[p.Source.ModeIndex].Type != 1) throw new Exception("Source mode unavailable.");
        return c.Modes[p.Source.ModeIndex].Source;
    }
    public static void Check(int rc) { if (rc != 0) throw new Win32Exception(rc); }
    public static void Apply(Config c, bool validate, bool allowChanges = false)
    {
        int rc = SetDisplayConfig((uint)c.Paths.Length, c.Paths, (uint)c.Modes.Length, c.Modes, 0x20u | (validate ? 0x40u : 0x80u) | (allowChanges ? 0x400u : 0u));
        if (rc != 0) throw new Exception(String.Format("SetDisplayConfig ({0}, paths={1}, allowChanges={2}) error {3}: {4}", validate ? "validate" : "apply", c.Paths.Length, allowChanges, rc, new Win32Exception(rc).Message));
    }
    public static void RestoreInternal() { Check(SetDisplayConfig(0, null, 0, null, 0x81)); }
    static DpiGet Dpi(Path p)
    {
        if (Marshal.SizeOf(typeof(DpiGet)) != 32 || Marshal.SizeOf(typeof(DpiSet)) != 24) throw new Exception("Unexpected DPI structure size.");
        DpiGet q = new DpiGet(); q.Header = new Header { Type = unchecked((uint)-3), Size = 32, Adapter = p.Source.Adapter, Id = p.Source.Id };
        Check(GetDpi(ref q));
        if (q.Min > 0 || q.Current < q.Min || q.Current > q.Max || -q.Min + q.Max >= ScaleValues.Length) throw new Exception("Unsupported DPI scale range.");
        return q;
    }
    public static int GetScale(Path p) { DpiGet q = Dpi(p); return ScaleValues[-q.Min + q.Current]; }
    public static void SetScale(Path p, int percent)
    {
        DpiGet q = Dpi(p); int index = Array.IndexOf(ScaleValues, percent); int relative = index + q.Min;
        if (index < 0 || relative < q.Min || relative > q.Max) throw new Exception("Requested display scale is unsupported.");
        if (q.Current == relative) return;
        DpiSet value = new DpiSet { Header = new Header { Type = unchecked((uint)-4), Size = 24, Adapter = p.Source.Adapter, Id = p.Source.Id }, Relative = relative };
        Check(SetDpi(ref value));
        if (GetScale(p) != percent) throw new Exception("Could not verify virtual display scale.");
    }
    public static Config Single(string devicePath, uint width, uint height)
    {
        Config c = Query(true);
        Path[] matches = c.Paths.Where(p => p.Target.Available != 0 && String.Equals(Name(p).DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new Exception("Selected display is unavailable.");
        Path p0 = matches.OrderByDescending(p => (p.Flags & 1) != 0).First();
        p0.Flags = 1; p0.Source.ModeIndex = 0; p0.Target.ModeIndex = 0xffffffff;
        p0.Target.Rotation = 1; p0.Target.Scaling = 1;
        p0.Target.Refresh = new Rational(); p0.Target.ScanLine = 0;
        Mode mode = new Mode { Type = 1, Id = p0.Source.Id, Adapter = p0.Source.Adapter, Source = new SourceMode { Width = width, Height = height, PixelFormat = 4, Position = new Point() } };
        return new Config { Paths = new Path[] { p0 }, Modes = new Mode[] { mode } };
    }
    public static void Save(Config config, string filename)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(filename)))
        {
            w.Write(0x534f4431); WriteArray(w, config.Paths); WriteArray(w, config.Modes);
        }
    }
    static void WriteArray<T>(BinaryWriter w, T[] items) where T : struct
    {
        int size = Marshal.SizeOf(typeof(T)); w.Write(items.Length); w.Write(size); IntPtr ptr = Marshal.AllocHGlobal(size);
        try { foreach (T item in items) { Marshal.StructureToPtr(item, ptr, false); byte[] b = new byte[size]; Marshal.Copy(ptr, b, 0, size); w.Write(b); } }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    static T[] ReadArray<T>(BinaryReader r) where T : struct
    {
        int count = r.ReadInt32(), size = r.ReadInt32();
        if (count < 0 || count > 256 || size != Marshal.SizeOf(typeof(T))) throw new Exception("Invalid saved configuration.");
        T[] items = new T[count]; IntPtr ptr = Marshal.AllocHGlobal(size);
        try { for (int i = 0; i < count; i++) { byte[] b = r.ReadBytes(size); if (b.Length != size) throw new EndOfStreamException(); Marshal.Copy(b, 0, ptr, size); items[i] = (T)Marshal.PtrToStructure(ptr, typeof(T)); } }
        finally { Marshal.FreeHGlobal(ptr); }
        return items;
    }
    public static Config Load(string filename)
    {
        using (BinaryReader r = new BinaryReader(File.OpenRead(filename)))
        {
            if (r.ReadInt32() != 0x534f4431) throw new Exception("Invalid saved configuration.");
            return new Config { Paths = ReadArray<Path>(r), Modes = ReadArray<Mode>(r) };
        }
    }
    public static string Describe(Config c)
    {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        foreach (Path p in c.Paths)
        {
            TargetName name = Name(p);
            b.AppendFormat("Active={0}; Available={1}; Internal={2}; Tech={3}; GDI={4}; Name={5}\r\nDevicePath={6}\r\n", (p.Flags & 1) != 0, p.Target.Available, Internal(p), p.Target.Technology, GdiName(p), name.Name, name.DevicePath);
            b.AppendLine("Adapter=" + AdapterPath(p));
            if (p.Source.ModeIndex < c.Modes.Length) { SourceMode s = GetSource(c, p); b.AppendFormat("Size={0}x{1}; Position={2},{3}; Refresh={4}/{5}\r\n", s.Width, s.Height, s.Position.X, s.Position.Y, p.Target.Refresh.Numerator, p.Target.Refresh.Denominator); }
        }
        return b.ToString();
    }
    public static string Modes(Path p)
    {
        var modes = new System.Collections.Generic.SortedSet<string>(); string name = GdiName(p);
        for (int i = 0; i < 2000; i++) { DevMode d = new DevMode(); d.Size = (ushort)Marshal.SizeOf(typeof(DevMode)); if (!EnumDisplaySettings(name, i, ref d)) break; modes.Add(String.Format("{0}x{1}@{2}", d.Width, d.Height, d.Frequency)); }
        return String.Join(", ", modes);
    }
    public static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware();
            if (args.Length == 2 && args[0] == "--save") { Save(Query(false), args[1]); Console.WriteLine("Saved."); }
            else if (args.Length == 2 && args[0] == "--restore") { Apply(Load(args[1]), false); Console.WriteLine("Restored."); }
            else if (args.Length == 2 && args[0] == "--validate") { Apply(Load(args[1]), true); Console.WriteLine("Valid."); }
            else { Config c = Query(args.Length > 0 && args[0] == "--all"); Console.WriteLine(Describe(c)); if (args.Length == 0) foreach (Path p in c.Paths) Console.WriteLine(Name(p).Name + ": " + Modes(p)); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
