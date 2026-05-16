using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class LightingSetting
{
    public int Port { get; set; }
    public int Mode { get; set; }
    public int Speed { get; set; }
    public int Direction { get; set; }
    public int Brightness { get; set; }
    public List<LightingColor> Colors { get; set; } = new();
}
