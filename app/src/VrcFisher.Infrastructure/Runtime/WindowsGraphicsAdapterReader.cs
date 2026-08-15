using System.Runtime.InteropServices;
using Microsoft.Win32;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Runtime;

internal static class WindowsGraphicsAdapterReader
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private const uint SoftwareAdapterFlag = 2;
    private static readonly Guid IidDxgiFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

    public static IReadOnlyList<GraphicsAdapterInfo> Read()
    {
        var registry = ReadRegistryMetadata();
        var adapters = ReadDxgiAdapters();
        if (adapters.Count == 0)
        {
            return registry
                .Where(item => !IsSoftwareAdapterName(item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select((item, index) => new GraphicsAdapterInfo(index, item.Name, item.Memory, item.Driver))
                .ToArray();
        }

        var physicalAdapters = adapters
            .Where(adapter => (adapter.Flags & SoftwareAdapterFlag) == 0)
            .GroupBy(adapter => adapter.VendorId != 0 && adapter.DeviceId != 0
                ? $"device:{adapter.VendorId}:{adapter.DeviceId}:{adapter.SubSystemId}:{adapter.Name}"
                : $"luid:{adapter.Luid}:{adapter.Name}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        return physicalAdapters.Select(adapter =>
        {
            var metadata = registry.FirstOrDefault(item =>
                string.Equals(item.Name, adapter.Name, StringComparison.OrdinalIgnoreCase));
            return new GraphicsAdapterInfo(adapter.Index, adapter.Name, adapter.Memory, metadata.Driver);
        }).ToArray();
    }

    public static string GetDirectMlDeviceName(int deviceId)
    {
        var adapter = Read().FirstOrDefault(item => item.Index == deviceId);
        return adapter is null ? $"DirectML device {deviceId}" : $"{adapter.Name} (DirectML {deviceId})";
    }

    private static IReadOnlyList<DxgiAdapter> ReadDxgiAdapters()
    {
        var adapters = new List<DxgiAdapter>();
        var factoryId = IidDxgiFactory1;
        var result = CreateDXGIFactory1(ref factoryId, out var factory);
        if (result < 0 || factory == IntPtr.Zero) return adapters;

        try
        {
            var enumAdapters = GetComMethod<EnumAdapters1Delegate>(factory, EnumAdapters1Slot);
            for (uint index = 0; ; index++)
            {
                result = enumAdapters(factory, index, out var adapter);
                if (result == DxgiErrorNotFound) break;
                if (result < 0 || adapter == IntPtr.Zero) break;
                try
                {
                    var getDescription = GetComMethod<GetDesc1Delegate>(adapter, GetDesc1Slot);
                    if (getDescription(adapter, out var description) < 0
                        || string.IsNullOrWhiteSpace(description.Description))
                        continue;
                    var memory = description.DedicatedVideoMemory.ToUInt64();
                    adapters.Add(new DxgiAdapter(
                        checked((int)index),
                        description.Description.Trim(),
                        checked((long)Math.Min(memory, long.MaxValue)),
                        description.AdapterLuid,
                        description.VendorId,
                        description.DeviceId,
                        description.SubSystemId,
                        description.Flags));
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException or MarshalDirectiveException)
        {
            adapters.Clear();
        }
        finally
        {
            Marshal.Release(factory);
        }
        return adapters;
    }

    private static IReadOnlyList<(string Name, long Memory, string? Driver)> ReadRegistryMetadata()
    {
        var adapters = new List<(string Name, long Memory, string? Driver)>();
        using var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
        if (video is null) return adapters;

        foreach (var adapterKeyName in video.GetSubKeyNames())
        {
            using var adapter = video.OpenSubKey($@"{adapterKeyName}\0000");
            var name = adapter?.GetValue("DriverDesc") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var memory = ReadRegistryInteger(adapter, "HardwareInformation.qwMemorySize");
            if (memory <= 0) memory = ReadRegistryInteger(adapter, "HardwareInformation.MemorySize");
            var driver = adapter?.GetValue("DriverVersion") as string;
            if (adapters.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(item.Driver, driver, StringComparison.OrdinalIgnoreCase)))
                continue;
            adapters.Add((name.Trim(), Math.Max(0, memory), driver?.Trim()));
        }
        return adapters;
    }

    private static long ReadRegistryInteger(RegistryKey? key, string name) => key?.GetValue(name) switch
    {
        long value => value,
        int value => unchecked((uint)value),
        byte[] value when value.Length >= sizeof(long) => BitConverter.ToInt64(value, 0),
        _ => 0
    };

    private static bool IsSoftwareAdapterName(string name) =>
        name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase);

    private static T GetComMethod<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    private sealed record DxgiAdapter(
        int Index,
        string Name,
        long Memory,
        long Luid,
        uint VendorId,
        uint DeviceId,
        uint SubSystemId,
        uint Flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DxgiAdapterDescription1 description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid factoryId, out IntPtr factory);
}
