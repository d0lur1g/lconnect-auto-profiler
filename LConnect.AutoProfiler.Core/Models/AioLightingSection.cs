using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public record AioLightingSection
{
    public List<LightingColor> Colors { get; init; } = new();
    public int Speed      { get; init; }
    public int Brightness { get; init; }
    public int Direction  { get; init; } = 0;
}
