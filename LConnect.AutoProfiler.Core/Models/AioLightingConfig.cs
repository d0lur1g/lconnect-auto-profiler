namespace LConnect.AutoProfiler.Core.Models;

public class AioLightingConfig
{
    public int Mode { get; set; }
    public bool IsDynamicMode { get; set; } = false;
    public int SensorType { get; set; } = 1;
    public SensorRange Range { get; set; } = new();
    public AioLightingSection Static { get; set; } = new();
    public AioLightingSection DynamicHigh { get; set; } = new();
    public AioLightingSection DynamicLow { get; set; } = new();
}

public class SensorRange
{
    public int HighValue { get; set; } = 60;
    public int LowValue { get; set; } = 30;
    public int MaxValue { get; set; } = 100;
    public int MinValue { get; set; } = 0;
}