namespace PcMonitorOverlay.Models;

public sealed record MetricReading(
    string Title,
    double? Percent,
    string Detail,
    string Status = "OK");
