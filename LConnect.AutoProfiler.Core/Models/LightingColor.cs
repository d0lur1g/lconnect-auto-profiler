using System;

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

    /// <summary>Construit une couleur avec ScA/ScR/ScG/ScB calculés (sRGB IEC).</summary>
    public static LightingColor From(int a, int r, int g, int b) => new()
    {
        A = a,
        R = r,
        G = g,
        B = b,
        ScA = a / 255.0,
        ScR = ToLinear(r),
        ScG = ToLinear(g),
        ScB = ToLinear(b)
    };

    // Conversion sRGB standard IEC (confirmée par capture Wireshark L-Connect)
    private static double ToLinear(int c)
    {
        double v = c / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}