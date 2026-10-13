using System.Runtime.InteropServices;

namespace ScreenDash;

/// <summary>Enumerates DXGI in the same adapter/output order used by FFmpeg's ddagrab.</summary>
public static class DisplayCatalog
{
    private const int DxgiNotFound = unchecked((int)0x887A0002);

    public static IReadOnlyList<DisplaySource> GetDisplays()
    {
        var result = new List<DisplaySource>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumAdapters = Method<EnumChild>(factory, 7); // IDXGIFactory::EnumAdapters
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var hr = enumAdapters(factory, adapterIndex, out var adapter);
                if (hr == DxgiNotFound) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var enumOutputs = Method<EnumChild>(adapter, 7); // IDXGIAdapter::EnumOutputs
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        hr = enumOutputs(adapter, outputIndex, out var output);
                        if (hr == DxgiNotFound) break;
                        Marshal.ThrowExceptionForHR(hr);
                        try
                        {
                            Marshal.ThrowExceptionForHR(Method<GetOutputDesc>(output, 7)(output, out var desc));
                            if (!desc.AttachedToDesktop) continue;
                            var width = desc.Bounds.Right - desc.Bounds.Left;
                            var height = desc.Bounds.Bottom - desc.Bounds.Top;
                            if (width <= 0 || height <= 0) continue;
                            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == desc.DeviceName);
                            var primary = screen?.Primary == true ? " · Hauptbildschirm" : "";
                            var label = $"{desc.DeviceName.Replace(@"\\.\", "")} · {width} × {height}{primary}";
                            result.Add(new DisplaySource($"{adapterIndex}:{outputIndex}:{desc.DeviceName}", label,
                                (int)adapterIndex, (int)outputIndex, desc.Bounds.Left, desc.Bounds.Top, width, height));
                        }
                        finally { Marshal.Release(output); }
                    }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumChild(IntPtr self, uint index, out IntPtr child);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetOutputDesc(IntPtr self, out OutputDesc desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OutputDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public Rect Bounds;
        [MarshalAs(UnmanagedType.Bool)] public bool AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
    }
}
