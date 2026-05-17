namespace LConnect.AutoProfiler.Core.Models;

public class FanGroupConfig
{
    public int FanGroupIndex { get; set; }
    public FanCurveConfig Config { get; set; } = new();
}