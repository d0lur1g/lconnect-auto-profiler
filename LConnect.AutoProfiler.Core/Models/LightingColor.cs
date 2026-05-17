namespace LConnect.AutoProfiler.Core.Models;

public sealed class LightingColor
{
    public object? ColorContext { get; set; } = null;
    public int A { get; set; } = 255;
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public double ScA { get; set; } = 1.0;
    public double ScR { get; set; }
    public double ScG { get; set; }
    public double ScB { get; set; }
}