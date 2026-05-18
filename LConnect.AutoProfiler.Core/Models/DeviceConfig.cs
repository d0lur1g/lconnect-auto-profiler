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

    /// <summary>
    /// Vrai si les zones Inner et Outer sont configurées indépendamment.
    /// Lorsque false, LightingMode s'applique à toutes les zones.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsIndividualMode { get; set; }

    /// <summary>
    /// Mode d'éclairage global (utilisé quand IsIndividualMode = false).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LightingMode { get; set; }

    /// <summary>
    /// Mode d'éclairage de la zone Inner (utilisé quand IsIndividualMode = true).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LightingModeInner { get; set; }

    /// <summary>
    /// Mode d'éclairage de la zone Outer (utilisé quand IsIndividualMode = true).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LightingModeOuter { get; set; }

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
