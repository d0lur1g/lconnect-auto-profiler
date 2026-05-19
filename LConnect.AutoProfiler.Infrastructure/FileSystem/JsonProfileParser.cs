using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LConnect.AutoProfiler.Application.Exceptions;
using LConnect.AutoProfiler.Core.Interfaces;
using LConnect.AutoProfiler.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Infrastructure.FileSystem;

/// <summary>
/// Lit un fichier JSON fully_decoded L-Connect et construit le LightingProfile :
///   Datas[MainType=0, SubType=16789504] (GA II) → LightingSetting + SetFanSpeed
///   Datas[MainType=0, SubType=16846849] (AIO)   → ScreenLEDLighting + PumpSpeed + FanSpeed
///   Datas[MainType=2, SubType=0]                → MergeOrder + LightingSetting (type HTTP)
/// </summary>
public sealed class JsonProfileParser : IProfileParser
{
    private const int MainTypeDevice = 0;
    private const int MainTypeMerge = 2;
    private const int SubTypeGaII = 16789504;
    private const int SubTypeAio = 16846849;

    private readonly ProfileParserOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<JsonProfileParser> _logger;

    private readonly Dictionary<string, LightingProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Convertit un pourcentage de vitesse JSON [0–100] en valeur API L-Connect.
    /// L'API interprète la valeur comme un délai inverse : 0 = animation la plus rapide.
    /// Mapping : JSON 100% → API 0, JSON 0% → API 200.
    /// null en entrée → null (le firmware applique sa valeur par défaut).
    /// </summary>
    private static int? SpeedPercentToApiValue(int? speedPercent)
    {
        if (speedPercent is null) return null;
        var clamped = Math.Clamp(speedPercent.Value, 0, 100);
        return (int)Math.Round((100 - clamped) / 100.0 * 200);
    }

    /// <summary>
    /// Convertit un pourcentage de luminosité JSON [0–100] en valeur API L-Connect.
    /// Sur le firmware GA II, 0 = 100% d'intensité couleur.
    /// Mapping : JSON 100% → API 0, JSON 0% → API 100.
    /// null en entrée → 0 (luminosité maximale par défaut).
    /// </summary>
    private static int BrightnessPercentToApiValue(int? brightnessPercent)
    {
        if (brightnessPercent is null) return 0;
        var clamped = Math.Clamp(brightnessPercent.Value, 0, 100);
        return (int)Math.Round((100 - clamped) / 100.0 * 100);
    }

    public JsonProfileParser(
        IOptions<ProfileParserOptions> options,
        IHostEnvironment env,
        ILogger<JsonProfileParser> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    public async Task<LightingProfile> ParseProfileAsync(string profileName)
    {
        lock (_lock)
        {
            if (_profileCache.TryGetValue(profileName, out var cached))
            {
                _logger.LogDebug("JsonProfileParser: profil '{Name}' servi depuis le cache.", profileName);
                return cached;
            }
        }

        var dir = ResolvePath(_options.ProfilesDirectory);
        var filePath = Path.Combine(dir, $"{profileName}.json");

        if (!File.Exists(filePath))
            throw new ProfileNotFoundException(profileName);

        var json = await File.ReadAllTextAsync(filePath);
        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("Invalid JSON.");
        var profile = new LightingProfile { ProfileName = profileName };

        var datasNode = root["Datas"]?.AsArray() ?? new JsonArray();
        string gaIIDevicePath = string.Empty;

        foreach (var dataEntry in datasNode)
        {
            if (dataEntry is null) continue;

            var mainType = dataEntry["MainType"]?.GetValue<int>() ?? -1;
            var subType = dataEntry["SubType"]?.GetValue<int>() ?? -1;

            if (mainType == MainTypeDevice && subType == SubTypeGaII)
            {
                var path = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrEmpty(path))
                    gaIIDevicePath = path;
                ParseGaII(dataEntry, profile);
            }
            else if (mainType == MainTypeDevice && subType == SubTypeAio)
                ParseAio(dataEntry, profile);
            else if (mainType == MainTypeMerge)
                ParseMerge(dataEntry, profile, gaIIDevicePath);
        }

        if (profile.MergeOrder is not null && string.IsNullOrEmpty(profile.MergeOrder.DevicePath))
            profile.MergeOrder.DevicePath = gaIIDevicePath;

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s), MergeOrder={HasMerge}.",
            profileName, profile.Devices.Count, profile.MergeOrder is not null);

        lock (_lock)
        {
            _profileCache[profileName] = profile;
        }

        return profile;
    }

    public void InvalidateProfileCache(string? fileName)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                _profileCache.Clear();
                _logger.LogInformation("JsonProfileParser: cache complet des profils vidé.");
                return;
            }

            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                _profileCache.Clear();
                _logger.LogInformation("JsonProfileParser: changement non-JSON détecté, cache complet vidé.");
                return;
            }

            var profileName = Path.GetFileNameWithoutExtension(fileName);
            if (_profileCache.Remove(profileName))
                _logger.LogInformation("JsonProfileParser: cache invalidé pour le profil '{Name}'.", profileName);
            else
                _logger.LogDebug("JsonProfileParser: aucun cache pour le profil '{Name}' à invalider.", profileName);
        }
    }

    // =========================================================================
    // GA II
    // =========================================================================

    private void ParseGaII(JsonNode dataEntry, LightingProfile profile)
    {
        var devicePath = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
        var subProfiles = dataEntry["Data"]?["SubProfiles"]?.AsArray();
        if (subProfiles is null || subProfiles.Count == 0) return;

        var lightingConfig = new DeviceConfig
        {
            DevicePath = devicePath,
            DeviceType = "LightingSetting",
            Settings = new List<LightingSetting>()
        };

        var fanConfig = new DeviceConfig
        {
            DevicePath = devicePath,
            DeviceType = "SetFanSpeed",
            FanGroups = new List<FanGroupConfig>()
        };

        int groupIndex = 0;
        foreach (var groupNode in subProfiles)
        {
            if (groupNode is null) { groupIndex++; continue; }

            var groupName = groupNode["GroupName"]?.GetValue<string>() ?? string.Empty;
            var isIndividual = groupNode["IsIndividualMode"]?.GetValue<bool>() ?? false;
            var lightingMode = groupNode["LightingMode"]?.GetValue<int>() ?? 0;
            var lightingModeInner = groupNode["LightingModeInner"]?.GetValue<int>() ?? 0;
            var lightingModeOuter = groupNode["LightingModeOuter"]?.GetValue<int>() ?? 0;

            var activeInner = isIndividual ? lightingModeInner : lightingMode;
            var activeOuter = isIndividual ? lightingModeOuter : lightingMode;

            _logger.LogDebug(
                "GA II Group[{Index}] '{Name}': IsIndividual={Ind} LightingMode={Mode} Inner={Inner} Outer={Outer}",
                groupIndex, groupName, isIndividual, lightingMode, activeInner, activeOuter);

            if (groupIndex == 0)
            {
                lightingConfig.IsIndividualMode = isIndividual;
                lightingConfig.LightingMode = lightingMode;
                lightingConfig.LightingModeInner = lightingModeInner;
                lightingConfig.LightingModeOuter = lightingModeOuter;
            }

            var allSettings = groupNode["LightingSettings"]?.AsObject();
            if (allSettings is not null)
            {
                int portInner = groupIndex * 2;
                int portOuter = groupIndex * 2 + 1;

                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeInner, port: portInner, label: $"{groupName} Inner"));
                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeOuter, port: portOuter, label: $"{groupName} Outer"));
            }

            var rpmSetting = groupNode["RPMSetting"];
            if (rpmSetting is not null)
            {
                var activeMode = rpmSetting["Mode"]?.GetValue<int>() ?? 1;
                var minSpeed = rpmSetting["Profiles"]?.AsObject() is { } prof
                                 ? ResolveMinSpeed(prof, activeMode)
                                 : 210;
                var fanCurve = ExtractActiveFanCurve(
                    rpmSetting["Profiles"]?.AsObject(), activeMode, groupName, minSpeed, isGaII: true);
                if (fanCurve is not null)
                    fanConfig.FanGroups!.Add(new FanGroupConfig { FanGroupIndex = groupIndex, Config = fanCurve });
            }

            groupIndex++;
        }

        if (lightingConfig.Settings!.Count > 0)
            profile.Devices.Add(lightingConfig);

        if (fanConfig.FanGroups!.Count > 0)
            profile.Devices.Add(fanConfig);
    }

    // =========================================================================
    // AIO
    // =========================================================================

    private void ParseAio(JsonNode dataEntry, LightingProfile profile)
    {
        var devicePath = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
        var data = dataEntry["Data"];
        if (data is null) return;

        var screenProfiles = data["ScreenLEDProfiles"]?.AsObject();
        var activeScreenMode = data["ScreenLEDMode"]?.GetValue<int>() ?? 1;
        var aioLighting = ExtractAioLighting(screenProfiles, activeScreenMode);
        if (aioLighting is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "ScreenLEDLighting",
                AioLighting = aioLighting
            });

        var pumpSetting = data["Pump"];
        var activePumpMode = pumpSetting?["Mode"]?.GetValue<int>() ?? 10;
        var pumpCurve = ExtractActiveFanCurve(
            pumpSetting?["Profiles"]?.AsObject(), activePumpMode, "Pump", minSpeed: 0, isGaII: false);
        if (pumpCurve is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "PumpSpeed",
                FanCurve = pumpCurve
            });

        var fanSetting = data["Fan"];
        var activeFanMode = fanSetting?["Mode"]?.GetValue<int>() ?? 1;
        var fanCurve = ExtractActiveFanCurve(
            fanSetting?["Profiles"]?.AsObject(), activeFanMode, "AIO Fan", minSpeed: 0, isGaII: false);
        if (fanCurve is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "FanSpeed",
                FanCurve = fanCurve
            });
    }

    // =========================================================================
    // Merge (MainType=2)
    // =========================================================================

    private void ParseMerge(JsonNode dataEntry, LightingProfile profile, string fallbackDevicePath)
    {
        var devicePath = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrEmpty(devicePath))
            devicePath = fallbackDevicePath;

        var data = dataEntry["Data"];
        if (data is null) return;

        var deviceListNode = data["DeviceList"]?.AsArray();
        int[] deviceOrder;
        if (deviceListNode is not null && deviceListNode.Count > 0)
        {
            var list = new List<int>();
            foreach (var item in deviceListNode)
                if (item is not null) list.Add(item.GetValue<int>());
            deviceOrder = list.ToArray();
        }
        else
        {
            deviceOrder = [0, 1, 2, 3];
        }

        List<LightingSetting>? lightingSettings = null;
        var mergeNode = data["MergeLightingSetting"];
        if (mergeNode is not null)
        {
            var speedNode = mergeNode["Speed"];
            int? speedPercent = (speedNode is not null && speedNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                ? speedNode.GetValue<int>()
                : null;

            var brightnessNode = mergeNode["Brightness"];
            int? brightnessPercent = (brightnessNode is not null && brightnessNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                ? brightnessNode.GetValue<int>()
                : null;

            lightingSettings = new List<LightingSetting>
            {
                new LightingSetting
                {
                    Port       = 0,
                    Mode       = mergeNode["Mode"]?.GetValue<int>() ?? 0,
                    Speed      = SpeedPercentToApiValue(speedPercent),
                    Brightness = BrightnessPercentToApiValue(brightnessPercent),
                    Direction  = mergeNode["Direction"]?.GetValue<int>() ?? 0,
                    Colors     = ExtractColors(mergeNode["Colors"]?.AsArray())
                }
            };
        }

        profile.MergeOrder = new MergeOrderConfig
        {
            DeviceOrder = deviceOrder,
            LightingSettings = lightingSettings,
            DevicePath = devicePath
        };

        _logger.LogDebug("Merge parsed: DevicePath='{Path}', DeviceOrder=[{Order}], LightingSettings={HasLighting}",
            devicePath, string.Join(",", deviceOrder), lightingSettings is not null);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        var repoRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }

    /// <summary>
    /// Extrait le LightingSetting actif depuis LightingSettings JSON.
    /// Speed et Brightness sont lus depuis le JSON et convertis via leurs fonctions de mapping.
    /// Speed JSON : 25/50/75/100 (%) → API : délai inverse, 0=rapide, 200=lent.
    /// Brightness JSON : 0–100 (%) → API : 0=max, 100=min sur firmware GA II.
    /// </summary>
    private LightingSetting ExtractActiveLightingSetting(
        JsonObject allSettings, int targetMode, int port, string label)
    {
        foreach (var entry in allSettings)
        {
            var node = entry.Value;
            if (node?["Mode"]?.GetValue<int>() != targetMode) continue;

            var speedNode = node["Speed"];
            int? speedPercent = (speedNode is not null && speedNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                ? speedNode.GetValue<int>()
                : null;

            var brightnessNode = node["Brightness"];
            int? brightnessPercent = (brightnessNode is not null && brightnessNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                ? brightnessNode.GetValue<int>()
                : null;

            int? apiSpeed = SpeedPercentToApiValue(speedPercent);
            int apiBrightness = BrightnessPercentToApiValue(brightnessPercent);

            int direction = node["Direction"] is not null && node["Direction"]!.GetValueKind() != System.Text.Json.JsonValueKind.Null
                ? node["Direction"]!.GetValue<int>()
                : 0;

            _logger.LogDebug(
                "  [{Label}] mode {Mode} -> '{Key}' | SpeedJSON={SpeedPct}% → API={ApiSpeed} | BrightnessJSON={BrightPct}% → API={ApiBrightness} | Direction={Direction}",
                label, targetMode, entry.Key, speedPercent, apiSpeed, brightnessPercent, apiBrightness, direction);

            return new LightingSetting
            {
                Port       = port,
                Mode       = targetMode,
                Speed      = apiSpeed,
                Direction  = direction,
                Brightness = apiBrightness,
                Colors     = ExtractColors(node["Colors"]?.AsArray())
            };
        }

        _logger.LogWarning("  [{Label}] mode {Mode} not found -- using defaults.", label, targetMode);
        return new LightingSetting
        {
            Port       = port,
            Mode       = targetMode,
            Speed      = null,
            Brightness = 0
        };
    }

    private static int ResolveMinSpeed(JsonObject profiles, int targetMode)
    {
        foreach (var entry in profiles)
        {
            if (entry.Value?["Mode"]?.GetValue<int>() != targetMode) continue;
            var phaseInfos = entry.Value["PhaseInfos"]?.AsObject();
            if (phaseInfos is null) break;
            foreach (var pi in phaseInfos)
                return pi.Value?["MinSpeed"]?.GetValue<int>() ?? 210;
        }
        return 210;
    }

    private FanCurveConfig? ExtractActiveFanCurve(
        JsonObject? profiles, int targetMode, string label, int minSpeed, bool isGaII)
    {
        if (profiles is null) return null;

        JsonNode? activeProfileNode = null;
        string? activeKey = null;

        foreach (var entry in profiles)
        {
            if (entry.Value?["Mode"]?.GetValue<int>() == targetMode)
            {
                activeKey = entry.Key;
                activeProfileNode = entry.Value;
                break;
            }
        }

        if (activeProfileNode is null)
        {
            _logger.LogWarning("[{Label}] Fan/Pump mode {Mode} not found.", label, targetMode);
            return null;
        }

        _logger.LogDebug("[{Label}] active fan profile: '{Key}' (mode {Mode})", label, activeKey, targetMode);

        var phaseInfos = activeProfileNode["PhaseInfos"]?.AsObject();
        if (phaseInfos is null) return null;

        JsonNode? phaseInfo = null;
        foreach (var pi in phaseInfos) { phaseInfo = pi.Value; break; }
        if (phaseInfo is null) return null;

        var phases = new List<FanPhase>();

        if (isGaII)
            phases.Add(new FanPhase { Temperature = 0, Speed = minSpeed });

        foreach (var phaseNode in phaseInfo["Phases"]?.AsArray() ?? new JsonArray())
        {
            if (phaseNode is null) continue;
            phases.Add(new FanPhase
            {
                Temperature = phaseNode["Temperature"]?.GetValue<int>() ?? 0,
                Speed = phaseNode["Speed"]?.GetValue<int>() ?? 0
            });
        }

        if (isGaII)
        {
            var maxSpeed = phaseInfo["MaxSpeed"]?.GetValue<int>() ?? 2100;
            phases.Add(new FanPhase { Temperature = 100, Speed = maxSpeed });
        }

        return new FanCurveConfig
        {
            MaxSpeed  = phaseInfo["MaxSpeed"]?.GetValue<int>() ?? 100,
            Reference = activeProfileNode["RPMReferenceSource"]?.GetValue<int>() ?? 0,
            Phases    = phases
        };
    }

    private AioLightingConfig? ExtractAioLighting(JsonObject? screenProfiles, int targetMode)
    {
        if (screenProfiles is null) return null;

        JsonNode? activeNode = null;
        string? activeKey = null;

        foreach (var entry in screenProfiles)
        {
            if (entry.Value?["Mode"]?.GetValue<int>() == targetMode)
            {
                activeKey = entry.Key;
                activeNode = entry.Value;
                break;
            }
        }

        if (activeNode is null)
        {
            _logger.LogWarning("[AIO] ScreenLED mode {Mode} not found.", targetMode);
            return null;
        }

        _logger.LogDebug("[AIO] active screen profile: '{Key}' (mode {Mode})", activeKey, targetMode);

        var isDynamic = activeNode["IsDynamicMode"]?.GetValue<bool>() ?? false;
        var sensorType = 1;
        var highValue = 60;
        var lowValue = 30;

        if (isDynamic)
        {
            var dynSettings = activeNode["DynamicSettings"]?.AsObject();
            if (dynSettings is not null)
                foreach (var sensorEntry in dynSettings)
                {
                    sensorType = ResolveSensorType(sensorEntry.Key);
                    highValue = sensorEntry.Value?["HighValue"]?.GetValue<int>() ?? 60;
                    lowValue = sensorEntry.Value?["LowValue"]?.GetValue<int>() ?? 30;
                    break;
                }
        }

        AioLightingSection sourceSection;

        if (!isDynamic)
        {
            sourceSection = ExtractAioSection(activeNode["Static"]);
        }
        else
        {
            var dynSettings = activeNode["DynamicSettings"]?.AsObject();
            JsonNode? highNode = null;
            if (dynSettings is not null)
                foreach (var sensorEntry in dynSettings)
                { highNode = sensorEntry.Value?["High"]; break; }

            sourceSection = ExtractAioSection(highNode);

            if (sourceSection.Colors.Count == 0)
                sourceSection = ExtractAioSection(activeNode["Static"]);
        }

        _logger.LogDebug(
            "[AIO] source section: {Count} color(s), Speed={Speed}, Brightness={Brightness}, Direction={Direction}",
            sourceSection.Colors.Count, sourceSection.Speed, sourceSection.Brightness, sourceSection.Direction);

        return new AioLightingConfig
        {
            Mode          = targetMode,
            IsDynamicMode = isDynamic,
            SensorType    = sensorType,
            Range = new SensorRange
            {
                HighValue = highValue,
                LowValue  = lowValue,
                MaxValue  = 100,
                MinValue  = 0
            },
            Static      = sourceSection,
            DynamicHigh = sourceSection,
            DynamicLow  = sourceSection
        };
    }

    /// <summary>
    /// AIO / ScreenLEDLighting :
    ///   Speed et Brightness lus depuis le JSON et convertis via les fonctions de mapping.
    ///   Fallback : Speed=null (firmware default), Brightness=0 (max intensité).
    /// </summary>
    private AioLightingSection ExtractAioSection(JsonNode? node)
    {
        if (node is null)
        {
            return new AioLightingSection
            {
                Speed      = SpeedPercentToApiValue(null),
                Brightness = BrightnessPercentToApiValue(null)
            };
        }

        var speedNode = node["Speed"];
        int? speedPercent = (speedNode is not null && speedNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
            ? speedNode.GetValue<int>()
            : null;

        var brightnessNode = node["Brightness"];
        int? brightnessPercent = (brightnessNode is not null && brightnessNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
            ? brightnessNode.GetValue<int>()
            : null;

        return new AioLightingSection
        {
            Speed      = SpeedPercentToApiValue(speedPercent),
            Brightness = BrightnessPercentToApiValue(brightnessPercent),
            Direction  = node["Direction"] is not null ? node["Direction"]!.GetValue<int>() : 0,
            Colors     = ExtractColors(node["Colors"]?.AsArray())
        };
    }

    private static int ResolveSensorType(string key) => key switch
    {
        "CPUTemperature" => 1,
        "GPUTemperature" => 2,
        "CPULoad"        => 3,
        "GPULoad"        => 4,
        "PumpRPM"        => 5,
        "CoolantTemp"    => 6,
        _                => 1
    };

    private static List<LightingColor> ExtractColors(JsonArray? colorsArray)
    {
        if (colorsArray is null) return new List<LightingColor>();

        var result = new List<LightingColor>();
        foreach (var colorNode in colorsArray)
        {
            if (colorNode is null) continue;

            var a = colorNode["A"]?.GetValue<int>() ?? 255;
            var r = colorNode["R"]?.GetValue<int>() ?? 0;
            var g = colorNode["G"]?.GetValue<int>() ?? 0;
            var b = colorNode["B"]?.GetValue<int>() ?? 0;

            var colorContextNode = colorNode["ColorContext"];
            var colorContext = (colorContextNode is not null && colorContextNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                               ? colorContextNode.GetValue<string>()
                               : null;

            result.Add(new LightingColor
            {
                ColorContext = colorContext,
                A            = a,
                R            = r,
                G            = g,
                B            = b,
                ScA          = 1.0,
                ScR          = LightingColor.ToLinear(r),
                ScG          = LightingColor.ToLinear(g),
                ScB          = LightingColor.ToLinear(b)
            });
        }
        return result;
    }
}
