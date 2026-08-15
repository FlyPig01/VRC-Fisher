using System.Runtime.InteropServices;
using Microsoft.Win32;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Runtime;

public sealed class WindowsHardwareInfoProvider : IHardwareInfoProvider
{
    public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private static HardwareSnapshot Read(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new HardwareSnapshot(
                ReadCpuName(),
                ReadPhysicalCoreCount(),
                Environment.ProcessorCount,
                WindowsGraphicsAdapterReader.Read(),
                ReadTotalMemory(),
                ReadWindowsVersion(),
                Environment.Is64BitOperatingSystem);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return HardwareSnapshot.Unavailable(error.GetBaseException().Message);
        }
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim()
            ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            ?? "Unavailable";
    }

    private static int ReadPhysicalCoreCount()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length == 0) return Environment.ProcessorCount;
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length))
                return Environment.ProcessorCount;
            var offset = 0;
            var cores = 0;
            while (offset < length)
            {
                var relationship = Marshal.ReadInt32(buffer, offset);
                var size = Marshal.ReadInt32(buffer, offset + sizeof(int));
                if (size <= 0) break;
                if (relationship == 0) cores++;
                offset += size;
            }
            return cores > 0 ? cores : Environment.ProcessorCount;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long ReadTotalMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? checked((long)status.TotalPhysical) : 0;
    }

    private static string ReadWindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var display = key?.GetValue("DisplayVersion") as string;
        var build = key?.GetValue("CurrentBuildNumber") as string;
        return FormatWindowsVersion(product, display, build);
    }

    internal static string FormatWindowsVersion(string product, string? display, string? build)
    {
        if (int.TryParse(build, out var buildNumber) && buildNumber >= 22000)
        {
            const string windows10 = "Windows 10";
            const string windows11 = "Windows 11";
            product = product.StartsWith(windows10, StringComparison.OrdinalIgnoreCase)
                ? windows11 + product[windows10.Length..]
                : product.StartsWith(windows11, StringComparison.OrdinalIgnoreCase)
                    ? product
                    : windows11;
        }
        return string.Join(' ', new[] { product, display, build is null ? null : $"({build})" }
            .Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
