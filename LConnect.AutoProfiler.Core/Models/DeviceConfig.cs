using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class DeviceConfig
{
    public string DevicePath { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty; // e.g., "LightingSetting", "ScreenLEDLighting"
    public List<LightingSetting> Settings { get; set; } = new();
}
