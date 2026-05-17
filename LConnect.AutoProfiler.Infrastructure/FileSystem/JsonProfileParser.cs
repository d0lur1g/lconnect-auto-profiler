using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        var lightingConfig = new DeviceConfig
        {
            DevicePath = devicePath,
            DeviceType = "LightingSetting",
            Settings   = new List<LightingSetting>()
        };

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

            var allSettings = groupNode["LightingSettings"]?.AsObject();
            if (allSettings is not null)
            {
                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeInner, port: groupIndex * 2,     label: $"{groupName} Inner"));
                lightingConfig.Settings!.Add(
                    ExtractActiveLightingSetting(allSettings, activeOuter, port: groupIndex * 2 + 1, label: $"{groupName} Outer"));
            }

            var rpmSetting = groupNode["RPMSetting"];
            if (rpmSetting is not null)
            {
                var activeMode  = rpmSetting["Mode"]?.GetValue<int>() ?? 1;
                var minSpeed    = rpmSetting["Profiles"]?.AsObject() is { } prof
                                  ? ResolveMinSpeed(prof, activeMode)
                                  : 210;
                var fanProfiles = rpmSetting["Profiles"]?.AsObject();
                var fanCurve    = ExtractActiveFanCurve(fanProfiles, activeMode, groupName, minSpeed, isGaII: true);
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

        var screenProfiles   = data["ScreenLEDProfiles"]?.AsObject();
        var activeScreenMode = data["ScreenLEDMode"]?.GetValue<int>() ?? 1;
        var aioLighting      = ExtractAioLighting(screenProfiles, activeScreenMode);
        if (aioLighting is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath  = devicePath,
                DeviceType  = "ScreenLEDLighting",
                AioLighting = aioLighting
            });

        var pumpSetting    = data["Pump"];
        var activePumpMode = pumpSetting?["Mode"]?.GetValue<int>() ?? 10;
        var pumpCurve      = ExtractActiveFanCurve(pumpSetting?["Profiles"]?.AsObject(), activePumpMode, "Pump", minSpeed: 0, isGaII: false);
        if (pumpCurve is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "PumpSpeed",
                FanCurve   = pumpCurve
            });

        var fanSetting    = data["Fan"];
        var activeFanMode = fanSetting?["Mode"]?.GetValue<int>() ?? 1;
        var fanCurve      = ExtractActiveFanCurve(fanSetting?["Profiles"]?.AsObject(), activeFanMode, "AIO Fan", minSpeed: 0, isGaII: false);
        if (fanCurve is not null)
            profile.Devices.Add(new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "FanSpeed",
                FanCurve   = fanCurve
            });
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

            var speedNode = node["Speed"];
            var speed     = (speedNode is not null && speedNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                            ? speedNode.GetValue<int>()
                            : 0;

            return new LightingSetting
            {
                Port       = port,
                Mode       = targetMode,
                Speed      = speed,
                Direction  = node["Direction"] is not null ? node["Direction"]!.GetValue<int>() : 0,
                Brightness = 0,
                Colors     = ExtractColors(node["Colors"]?.AsArray())
            };
        }

        _logger.LogWarning("  [{Label}] mode {Mode} not found -- using defaults.", label, targetMode);
        return new LightingSetting { Port = port, Mode = targetMode };
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
        string?  activeKey         = null;

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
                Speed       = phaseNode["Speed"]?.GetValue<int>()       ?? 0
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
        var dynSettings = activeNode["DynamicSettings"]?.AsObject();

        var sensorType = 1;
        JsonNode? highNode = null;
        JsonNode? lowNode  = null;
        var highValue = 60;
        var lowValue  = 30;

        if (isDynamic && dynSettings is not null)
        {
            foreach (var sensorEntry in dynSettings)
            {
                sensorType = ResolveSensorType(sensorEntry.Key);
                highNode   = sensorEntry.Value?["High"];
                lowNode    = sensorEntry.Value?["Low"];
                highValue  = sensorEntry.Value?["HighValue"]?.GetValue<int>() ?? 60;
                lowValue   = sensorEntry.Value?["LowValue"]?.GetValue<int>()  ?? 30;
                break;
            }
        }

        // Extraire les 3 sections depuis le JSON
        var staticSection  = ExtractAioSection(staticNode);
        var highSection    = ExtractAioSection(highNode);
        var lowSection     = ExtractAioSection(lowNode);

        // -----------------------------------------------------------------------
        // L'API L-Connect exige que Static, DynamicHigh et DynamicLow soient
        // toujours remplis avec des couleurs, même en mode statique.
        // (Confirmé par Program.cs référence : les 3 sections reçoivent
        //  toujours les mêmes couleurs.)
        //
        // Règle de propagation :
        //  - Mode statique : staticNode est la source, high/low reçoivent
        //    ses couleurs s'ils sont vides.
        //  - Mode dynamique : si staticNode est absent/vide, Static
        //    reçoit les couleurs de DynamicHigh.
        //  - Fallback final : toute section vide reçoit les couleurs de
        //    la première section non vide.
        // -----------------------------------------------------------------------
        var referenceColors = staticSection.Colors.Count > 0
            ? staticSection.Colors
            : highSection.Colors.Count > 0
                ? highSection.Colors
                : lowSection.Colors;

        if (staticSection.Colors.Count == 0)
            staticSection = staticSection with { Colors = referenceColors };

        if (highSection.Colors.Count == 0)
            highSection = highSection with
            {
                Colors     = referenceColors,
                Speed      = staticSection.Speed,
                Brightness = staticSection.Brightness,
                Direction  = staticSection.Direction
            };

        if (lowSection.Colors.Count == 0)
            lowSection = lowSection with
            {
                Colors     = referenceColors,
                Speed      = staticSection.Speed,
                Brightness = staticSection.Brightness,
                Direction  = staticSection.Direction
            };

        _logger.LogDebug(
            "[AIO] Static={SC} color(s), DynamicHigh={HC} color(s), DynamicLow={LC} color(s)",
            staticSection.Colors.Count, highSection.Colors.Count, lowSection.Colors.Count);

        return new AioLightingConfig
        {
            Mode          = targetMode,
            IsDynamicMode = isDynamic,
            SensorType    = sensorType,
            Range         = new SensorRange
            {
                HighValue = highValue,
                LowValue  = lowValue,
                MaxValue  = 100,
                MinValue  = 0
            },
            Static      = staticSection,
            DynamicHigh = highSection,
            DynamicLow  = lowSection
        };
    }

    private static AioLightingSection ExtractAioSection(JsonNode? node)
    {
        if (node is null) return new AioLightingSection();

        var speedNode = node["Speed"];
        var speed     = (speedNode is not null && speedNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                        ? speedNode.GetValue<int>()
                        : 0;

        return new AioLightingSection
        {
            Speed      = speed,
            Brightness = 100,
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

            result.Add(new LightingColor
            {
                ColorContext = null,
                A   = a,
                R   = r,
                G   = g,
                B   = b,
                ScA = 1.0,
                ScR = LightingColor.ToLinear(r),
                ScG = LightingColor.ToLinear(g),
                ScB = LightingColor.ToLinear(b)
            });
        }
        return result;
    }
}
