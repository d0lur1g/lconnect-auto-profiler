using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class AioLightingSection
{
    public List<LightingColor> Colors { get; set; } = new();
    public int Speed { get; set; }
    public int Brightness { get; set; }
    public int Direction { get; set; } = 0;
}