using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class LightingProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public List<DeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Présent si le profil contient une entrée MainType=2 (MergeOrder).
    /// </summary>
    public MergeOrderConfig? MergeOrder { get; set; }
}
