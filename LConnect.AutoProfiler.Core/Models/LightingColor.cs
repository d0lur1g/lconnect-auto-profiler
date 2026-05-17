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

    /// <summary>
    /// Construit une couleur avec Sc* calculés via gamma 2.2,
    /// identique à la formule utilisée par L-Connect (Program.cs référence).
    /// </summary>
    public static LightingColor From(int a, int r, int g, int b) => new()
    {
        A   = a,
        R   = r,
        G   = g,
        B   = b,
        ScA = 1.0,
        ScR = ToLinear(r),
        ScG = ToLinear(g),
        ScB = ToLinear(b)
    };

    // Gamma 2.2 pur — confirmé par capture Program.cs L-Connect
    public static double ToLinear(int c) => Math.Pow(c / 255.0, 2.2);
}
