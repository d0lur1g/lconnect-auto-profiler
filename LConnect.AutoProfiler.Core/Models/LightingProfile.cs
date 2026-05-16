using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class LightingProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public List<DeviceConfig> Devices { get; set; } = new();
}
