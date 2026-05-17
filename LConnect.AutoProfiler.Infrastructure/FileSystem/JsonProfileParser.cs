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
/// Lit un fichier JSON fully_decoded L-Connect et construit les 5 DeviceConfig :
///   Datas[SubType=16789504] (GA II, vid_0cf2) → LightingSetting + SetFanSpeed
///   Datas[SubType=16846849] (AIO, vid_0416)   → ScreenLEDLighting + PumpSpeed + FanSpeed
/// </summary>
public sealed class JsonProfileParser : IProfileParser
{
    private const int SubTypeGaII = 16789504;
    private const int SubTypeAio  = 16846849;

    private readonly ProfileParserOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<JsonProfileParser> _logger;

    public JsonProfileParser(
        IOptions<ProfileParserOptions> options,
        IHostEnvironment env,
        ILogger<JsonProfileParser> logger)
    {
        _options = options.Value;
        _env     = env;
        _logger  = logger;
    }

    public async Task<LightingProfile> ParseProfileAsync(string profileName)
    {
        var dir      = ResolvePath(_options.ProfilesDirectory);
        var filePath = Path.Combine(dir, $"{profileName}.json");

        if (!File.Exists(filePath))
            throw new ProfileNotFoundException(profileName);

        var json    = await File.ReadAllTextAsync(filePath);
        var root    = JsonNode.Parse(json) ?? throw new InvalidDataException("Invalid JSON.");
        var profile = new LightingProfile { ProfileName = profileName };

        var datasNode = root["Datas"]?.AsArray() ?? new JsonArray();

        foreach (var dataEntry in datasNode)
        {
            if (dataEntry is null) continue;

            var subType = dataEntry["SubType"]?.GetValue<int>() ?? -1;

            if (subType == SubTypeGaII)
                ParseGaII(dataEntry, profile);
            else if (subType == SubTypeAio)
                ParseAio(dataEntry, profile);
            // MainType 1 et 2 ignorés (MergeLightingSetting, etc.)
        }

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s).",
            profileName, profile.Devices.Count);

        return profile;
    }

    // =========================================================================
    // GA II : LightingSetting + SetFanSpeed
    // =========================================================================

    private void ParseGaII(JsonNode dataEntry, LightingProfile profile)
    {
        var devicePath  = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
        var subProfiles = dataEntry["Data"]?["SubProfiles"]?.AsArray();
        if (subProfiles is null || subProfiles.Count == 0) return;

        // --- LightingSetting ---
        var lightingConfig = new DeviceConfig
        {
            DevicePath = devicePath,
            DeviceType = "LightingSetting",
            Settings   = new List<LightingSetting>()
        };

        // --- SetFanSpeed ---
        var fanConfig = new DeviceConfig
        {
            DevicePath = devicePath,
            DeviceType = "SetFanSpeed",
            FanGroups  = new List<FanGroupConfig>()
        };

        int groupIndex = 0;
        foreach (var groupNode in subProfiles)
        {
            if (groupNode is null) { groupIndex++; continue; }

            var groupName   = groupNode["GroupName"]?.GetValue<string>() ?? string.Empty;
            var activeInner = groupNode["LightingModeInner"]?.GetValue<int>() ?? 0;
            var activeOuter = groupNode["LightingModeOuter"]?.GetValue<int>() ?? 0;

            _logger.LogDebug("GA II Group[{Index}] '{Name}': Inner={Inner}, Outer={Outer}",
                groupIndex, groupName, activeInner, activeOuter);

            // Lighting
            var allSettings = groupNode["LightingSettings"]?.AsObject();
            if (allSettings is not null)
            {
                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeInner, port: groupIndex * 2,     label: $"{groupName} Inner"));
                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeOuter, port: groupIndex * 2 + 1, label: $"{groupName} Outer"));
            }

            // Fan speed (RPMSetting)
            var rpmSetting = groupNode["RPMSetting"];
            if (rpmSetting is not null)
            {
                var activeMode   = rpmSetting["Mode"]?.GetValue<int>() ?? 1;
                var fanProfiles  = rpmSetting["Profiles"]?.AsObject();
                var fanCurve     = ExtractActiveFanCurve(fanProfiles, activeMode, groupName, rpmSetting);
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
    // AIO : ScreenLEDLighting + PumpSpeed + FanSpeed
    // =========================================================================

    private void ParseAio(JsonNode dataEntry, LightingProfile profile)
    {
        var devicePath = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
        var data       = dataEntry["Data"];
        if (data is null) return;

        // --- ScreenLEDLighting ---
        var screenProfiles   = data["ScreenLEDProfiles"]?.AsObject();
        var activeScreenMode = data["ScreenLEDMode"]?.GetValue<int>() ?? 1;
        var aioLighting      = ExtractAioLighting(screenProfiles, activeScreenMode);
        if (aioLighting is not null)
        {
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "ScreenLEDLighting",
                AioLighting = aioLighting
            });
        }

        // --- PumpSpeed ---
        var pumpSetting   = data["Pump"];
        var activePumpMode = pumpSetting?["Mode"]?.GetValue<int>() ?? 10;
        var pumpProfiles  = pumpSetting?["Profiles"]?.AsObject();
        var pumpCurve     = ExtractActiveFanCurve(pumpProfiles, activePumpMode, "Pump", pumpSetting);
        if (pumpCurve is not null)
        {
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "PumpSpeed",
                FanCurve   = pumpCurve
            });
        }

        // --- FanSpeed ---
        var fanSetting   = data["Fan"];
        var activeFanMode = fanSetting?["Mode"]?.GetValue<int>() ?? 1;
        var fanProfiles  = fanSetting?["Profiles"]?.AsObject();
        var fanCurve     = ExtractActiveFanCurve(fanProfiles, activeFanMode, "AIO Fan", fanSetting);
        if (fanCurve is not null)
        {
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "FanSpeed",
                FanCurve   = fanCurve
            });
        }
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

    private LightingSetting ExtractActiveLightingSetting(
        JsonObject allSettings, int targetMode, int port, string label)
    {
        foreach (var entry in allSettings)
        {
            var node = entry.Value;
            if (node?["Mode"]?.GetValue<int>() != targetMode) continue;

            _logger.LogDebug("  [{Label}] mode {Mode} -> '{Key}'", label, targetMode, entry.Key);

            return new LightingSetting
            {
                Port       = port,
                Mode       = targetMode,
                Speed      = node["Speed"]?.GetValue<int?>()      ?? 75,
                Direction  = node["Direction"]?.GetValue<int?>()  ?? 0,
                Brightness = node["Brightness"]?.GetValue<int?>() ?? 100,
                Colors     = ExtractColors(node["Colors"]?.AsArray())
            };
        }

        _logger.LogWarning("  [{Label}] mode {Mode} not found -- using defaults.", label, targetMode);
        return new LightingSetting { Port = port, Mode = targetMode };
    }

    /// <summary>
    /// Extrait la FanCurveConfig active depuis un objet Profiles L-Connect.
    /// Le discriminant est le champ "Mode" de chaque profil enfant.
    /// </summary>
    private FanCurveConfig? ExtractActiveFanCurve(
        JsonObject? profiles, int targetMode, string label, JsonNode? settingNode)
    {
        if (profiles is null) return null;

        // Clé du profil actif = première clé dont le "Mode" == targetMode
        string? activeKey = null;
        JsonNode? activeProfileNode = null;

        foreach (var entry in profiles)
        {
            if (entry.Value?["Mode"]?.GetValue<int>() == targetMode)
            {
                activeKey         = entry.Key;
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

        // On prend la première clé de PhaseInfos (ex: "Size120mm|CPU|Curve" ou "None|CPU|Curve")
        var phaseInfos = activeProfileNode["PhaseInfos"]?.AsObject();
        if (phaseInfos is null) return null;

        JsonNode? phaseInfo = null;
        foreach (var pi in phaseInfos)
        {
            phaseInfo = pi.Value;
            break;
        }
        if (phaseInfo is null) return null;

        var maxSpeed = phaseInfo["MaxSpeed"]?.GetValue<int>() ?? 100;
        var phases   = new List<FanPhase>();

        foreach (var phaseNode in phaseInfo["Phases"]?.AsArray() ?? new JsonArray())
        {
            if (phaseNode is null) continue;
            phases.Add(new FanPhase
            {
                Temperature = phaseNode["Temperature"]?.GetValue<int>() ?? 0,
                Speed       = phaseNode["Speed"]?.GetValue<int>()       ?? 0
            });
        }

        return new FanCurveConfig
        {
            MaxSpeed  = maxSpeed,
            Reference = activeProfileNode["RPMReferenceSource"]?.GetValue<int>() ?? 0,
            Phases    = phases
        };
    }

    /// <summary>
    /// Extrait l'AioLightingConfig active depuis ScreenLEDProfiles.
    /// Le discriminant est le champ "Mode" de chaque profil enfant.
    /// </summary>
    private AioLightingConfig? ExtractAioLighting(JsonObject? screenProfiles, int targetMode)
    {
        if (screenProfiles is null) return null;

        JsonNode? activeNode = null;
        string?  activeKey  = null;

        foreach (var entry in screenProfiles)
        {
            if (entry.Value?["Mode"]?.GetValue<int>() == targetMode)
            {
                activeKey  = entry.Key;
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

        var isDynamic   = activeNode["IsDynamicMode"]?.GetValue<bool>() ?? false;
        var staticNode  = activeNode["Static"];
        var dynSettings = activeNode["DynamicSettings"];

        // SensorType : 1=CPUTemp, 2=GPUTemp, 3=CPULoad, 4=GPULoad (première clé de DynamicSettings)
        var sensorType = 1;
        JsonNode? highNode = null;
        JsonNode? lowNode  = null;
        if (isDynamic && dynSettings is not null)
        {
            foreach (var sensorEntry in dynSettings.AsObject())
            {
                sensorType = ResolveSensorType(sensorEntry.Key);
                highNode   = sensorEntry.Value?["High"];
                lowNode    = sensorEntry.Value?["Low"];
                break;
            }
        }

        var highValue = dynSettings?.AsObject()
            .FirstOrDefault().Value?[ResolveSensorKey(sensorType)]?["HighValue"]?.GetValue<int>() ?? 60;
        var lowValue  = dynSettings?.AsObject()
            .FirstOrDefault().Value?[ResolveSensorKey(sensorType)]?["LowValue"]?.GetValue<int>()  ?? 30;

        return new AioLightingConfig
        {
            Mode         = targetMode,
            IsDynamicMode = isDynamic,
            SensorType   = sensorType,
            Range        = new SensorRange { HighValue = highValue, LowValue = lowValue },
            Static       = ExtractAioSection(staticNode),
            DynamicHigh  = ExtractAioSection(highNode),
            DynamicLow   = ExtractAioSection(lowNode)
        };
    }

    private static AioLightingSection ExtractAioSection(JsonNode? node)
    {
        if (node is null) return new AioLightingSection();
        return new AioLightingSection
        {
            Speed      = node["Speed"]?.GetValue<int?>()      ?? 75,
            Brightness = node["Brightness"]?.GetValue<int?>() ?? 100,
            Direction  = node["Direction"]?.GetValue<int?>()  ?? 0,
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

    private static string ResolveSensorKey(int sensorType) => sensorType switch
    {
        1 => "CPUTemperature",
        2 => "GPUTemperature",
        3 => "CPULoad",
        4 => "GPULoad",
        5 => "PumpRPM",
        6 => "CoolantTemp",
        _ => "CPUTemperature"
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

            result.Add(new LightingColor
            {
                ColorContext = null,
                A   = a,
                R   = r,
                G   = g,
                B   = b,
                ScA = 1.0,
                ScR = Math.Pow(r / 255.0, 2.2),
                ScG = Math.Pow(g / 255.0, 2.2),
                ScB = Math.Pow(b / 255.0, 2.2),
            });
        }
        return result;
    }
}
