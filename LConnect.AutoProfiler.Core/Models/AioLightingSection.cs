using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

/// <summary>
/// Speed : valeur API L-Connect (délai inverse, 0=rapide, ~200=lent).
/// null = le firmware AIO applique sa valeur par défaut.
/// Brightness : 0 = intensité maximale.
/// </summary>
public class AioLightingSection
{
    public List<LightingColor> Colors { get; set; } = new();
    public int? Speed      { get; set; }
    public int  Brightness { get; set; }
    public int  Direction  { get; set; } = 0;
}
