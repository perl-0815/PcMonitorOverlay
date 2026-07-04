using LibreHardwareMonitor.Hardware;
using PcMonitorOverlay.Models;

namespace PcMonitorOverlay.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private static readonly TimeSpan GpuRefreshInterval = TimeSpan.FromSeconds(3);

    private readonly SystemMetricsReader _systemMetrics = new();
    private readonly Computer _computer;
    private readonly object _sync = new();
    private readonly IHardware[] _gpuHardware;
    private GpuReadings _cachedGpuReadings = new(
        Unavailable("GPU", "GPU sensor pending"),
        Unavailable("VRAM", "VRAM sensor pending"));
    private DateTimeOffset _lastGpuRefresh = DateTimeOffset.MinValue;
    private bool _disposed;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = false,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false
        };

        _computer.Open();
        _gpuHardware = _computer.Hardware.Where(IsGpuHardware).ToArray();
    }

    public MonitorSnapshot ReadSnapshot()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var cpu = ReadCpu();
            var memory = ReadMemory();
            var gpuInfo = ReadGpu();

            return new MonitorSnapshot(
                cpu,
                memory,
                gpuInfo.Gpu,
                gpuInfo.Vram,
                DateTimeOffset.Now);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _computer.Close();
            _disposed = true;
        }
    }

    private MetricReading ReadCpu()
    {
        var usage = _systemMetrics.ReadCpuUsage();
        return usage is null
            ? Unavailable("CPU", "CPU usage unavailable")
            : new MetricReading("CPU", usage, "Total processor load");
    }

    private MetricReading ReadMemory()
    {
        var memory = _systemMetrics.ReadMemory();
        if (memory is null)
        {
            return Unavailable("MEM", "Memory unavailable");
        }

        return new MetricReading(
            "MEM",
            memory.Percent,
            $"{FormatBytes(memory.UsedBytes)} / {FormatBytes(memory.TotalBytes)}");
    }

    private GpuReadings ReadGpu()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastGpuRefresh < GpuRefreshInterval)
        {
            return _cachedGpuReadings;
        }

        try
        {
            var gpus = _gpuHardware
                .Select(ReadGpuHardware)
                .Where(reading => reading is not null)
                .Cast<GpuHardwareReading>()
                .ToList();

            if (gpus.Count == 0)
            {
                _cachedGpuReadings = new GpuReadings(
                    Unavailable("GPU", "No GPU sensor"),
                    Unavailable("VRAM", "No VRAM sensor"));
                _lastGpuRefresh = now;
                return _cachedGpuReadings;
            }

            var selected = gpus
                .OrderByDescending(gpu => gpu.GpuPercent ?? -1)
                .ThenByDescending(gpu => gpu.VramPercent ?? -1)
                .First();

            var gpu = selected.GpuPercent is null
                ? Unavailable("GPU", selected.Name)
                : new MetricReading("GPU", selected.GpuPercent, selected.Name);

            var vramDetail = selected.VramDetail ?? selected.Name;
            var vram = selected.VramPercent is null
                ? Unavailable("VRAM", vramDetail)
                : new MetricReading("VRAM", selected.VramPercent, vramDetail);

            _cachedGpuReadings = new GpuReadings(gpu, vram);
            _lastGpuRefresh = now;
            return _cachedGpuReadings;
        }
        catch
        {
            _cachedGpuReadings = new GpuReadings(
                Unavailable("GPU", "GPU sensor error"),
                Unavailable("VRAM", "VRAM sensor error"));
            _lastGpuRefresh = now;
            return _cachedGpuReadings;
        }
    }

    private static bool IsGpuHardware(IHardware hardware) =>
        hardware.HardwareType is HardwareType.GpuAmd
            or HardwareType.GpuIntel
            or HardwareType.GpuNvidia;

    private static GpuHardwareReading? ReadGpuHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
        {
            subHardware.Update();
        }

        var sensors = hardware.Sensors
            .Concat(hardware.SubHardware.SelectMany(sub => sub.Sensors))
            .Where(sensor => sensor.Value.HasValue)
            .ToList();

        var gpuLoad = PickGpuLoad(sensors);
        var vramLoad = PickVramLoad(sensors);
        var memoryUsed = PickMemorySensor(sensors, "used");
        var memoryTotal = PickMemorySensor(sensors, "total");

        string? vramDetail = null;
        if (memoryUsed.HasValue && memoryTotal is > 0)
        {
            var usedGb = NormalizeGpuMemoryToGb(memoryUsed.Value, memoryTotal.Value);
            var totalGb = NormalizeGpuMemoryToGb(memoryTotal.Value, memoryTotal.Value);
            vramDetail = $"{usedGb:0.0} / {totalGb:0.0} GB";

            if (!vramLoad.HasValue)
            {
                vramLoad = usedGb / totalGb * 100d;
            }
        }

        return new GpuHardwareReading(
            hardware.Name,
            gpuLoad.HasValue ? SystemMetricsReader.ClampPercent(gpuLoad.Value) : null,
            vramLoad.HasValue ? SystemMetricsReader.ClampPercent(vramLoad.Value) : null,
            vramDetail);
    }

    private static double? PickGpuLoad(IEnumerable<ISensor> sensors)
    {
        var loadSensors = sensors
            .Where(sensor => sensor.SensorType == SensorType.Load)
            .ToList();

        var preferred = loadSensors.FirstOrDefault(sensor =>
            Contains(sensor.Name, "core") ||
            Contains(sensor.Name, "3d") ||
            Contains(sensor.Name, "graphics") ||
            (Contains(sensor.Name, "gpu") && !Contains(sensor.Name, "memory")));

        return preferred?.Value ?? loadSensors.FirstOrDefault()?.Value;
    }

    private static double? PickVramLoad(IEnumerable<ISensor> sensors)
    {
        var preferred = sensors.FirstOrDefault(sensor =>
            sensor.SensorType == SensorType.Load &&
            (Contains(sensor.Name, "memory") || Contains(sensor.Name, "vram")));

        return preferred?.Value;
    }

    private static float? PickMemorySensor(IEnumerable<ISensor> sensors, string token)
    {
        var preferred = sensors.FirstOrDefault(sensor =>
            sensor.SensorType is SensorType.SmallData or SensorType.Data &&
            (Contains(sensor.Name, "memory") || Contains(sensor.Name, "vram")) &&
            Contains(sensor.Name, token));

        return preferred?.Value;
    }

    private static double NormalizeGpuMemoryToGb(float value, float total)
    {
        return total > 512 ? value / 1024d : value;
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static MetricReading Unavailable(string title, string detail) =>
        new(title, null, detail, "N/A");

    private static string FormatBytes(ulong bytes)
    {
        var gib = bytes / 1024d / 1024d / 1024d;
        return $"{gib:0.0} GB";
    }

    private sealed record GpuHardwareReading(
        string Name,
        double? GpuPercent,
        double? VramPercent,
        string? VramDetail);

    private sealed record GpuReadings(MetricReading Gpu, MetricReading Vram);
}
