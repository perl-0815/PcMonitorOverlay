using System.Runtime.InteropServices;

namespace PcMonitorOverlay.Services;

public sealed class SystemMetricsReader
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousCpuSample;

    public double? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return null;
        }

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();

        if (!_hasPreviousCpuSample)
        {
            StoreCpuSample(idle, kernel, user);
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var totalDelta = (kernel - _previousKernel) + (user - _previousUser);
        StoreCpuSample(idle, kernel, user);

        if (totalDelta == 0)
        {
            return null;
        }

        var busy = 1d - (idleDelta / (double)totalDelta);
        return ClampPercent(busy * 100d);
    }

    public MemoryReading? ReadMemory()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return null;
        }

        var used = status.TotalPhys - status.AvailPhys;
        var percent = used / (double)status.TotalPhys * 100d;
        return new MemoryReading(ClampPercent(percent), used, status.TotalPhys);
    }

    internal static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private void StoreCpuSample(ulong idle, ulong kernel, ulong user)
    {
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;
        _hasPreviousCpuSample = true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

public sealed record MemoryReading(double Percent, ulong UsedBytes, ulong TotalBytes);
