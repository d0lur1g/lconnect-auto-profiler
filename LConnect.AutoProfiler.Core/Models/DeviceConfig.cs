using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LConnect.AutoProfiler.Core.Models;

public class DeviceConfig
{
    public string DevicePath { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;

    // Éclairage GA II : liste de ports
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LightingSetting>? Settings { get; set; }

    // Éclairage AIO écran
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AioLightingConfig? AioLighting { get; set; }

    // Ventilateurs GA II (SetFanSpeed) : liste de groupes
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FanGroupConfig>? FanGroups { get; set; }

    // Pompe AIO (PumpSpeed) ou ventilateurs AIO (FanSpeed)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FanCurveConfig? FanCurve { get; set; }
}