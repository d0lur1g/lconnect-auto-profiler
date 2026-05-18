namespace LConnect.AutoProfiler.Core.Models;

public class MergeOrderConfig
{
    /// <summary>
    /// Ordre des devices transmis à l'endpoint MergeOrder (ex: [0,1,2,3]).
    /// </summary>
    public int[] DeviceOrder { get; set; } = [0, 1, 2, 3];

    /// <summary>
    /// Paramètres d'éclairage du mode Merge.
    /// </summary>
    public MergeLightingSetting? LightingSetting { get; set; }
}
