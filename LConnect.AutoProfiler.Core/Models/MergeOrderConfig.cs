namespace LConnect.AutoProfiler.Core.Models;

public class MergeOrderConfig
{
    /// <summary>
    /// Chemin du device GA II portant le MergeOrder (Metadata du nœud MainType=2).
    /// </summary>
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>
    /// Ordre des devices transmis à l'endpoint MergeOrder (ex: [0,1,2,3]).
    /// Correspond au payload de type=MergeOrder.
    /// </summary>
    public int[] DeviceOrder { get; set; } = [0, 1, 2, 3];

    /// <summary>
    /// Paramètres d'éclairage transmis via type=LightingSetting sur le device Merge.
    /// Même structure que l'appel LightingSetting standard — un objet par port.
    /// </summary>
    public List<LightingSetting>? LightingSettings { get; set; }
}
