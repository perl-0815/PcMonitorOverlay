namespace PcMonitorOverlay.Models;

public sealed record MonitorSnapshot(
    MetricReading Cpu,
    MetricReading Memory,
    MetricReading Gpu,
    MetricReading Vram,
    DateTimeOffset Timestamp);
