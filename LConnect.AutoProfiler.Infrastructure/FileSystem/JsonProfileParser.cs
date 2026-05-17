using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
/// Lit un fichier JSON fully_decoded L-Connect et extrait UNIQUEMENT
/// les paramètres du mode actif (LightingMode, Inner, Outer, couleurs, vitesse).
///
/// Le chemin ProfilesDirectory est résolu relativement à la racine du repo
/// (ContentRootPath = Host/, donc on remonte d'un niveau).
/// Si le chemin est absolu (prod), il est utilisé tel quel.
/// </summary>
public sealed class JsonProfileParser : IProfileParser
{
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

            var devicePath  = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
            var subProfiles = dataEntry["Data"]?["SubProfiles"]?.AsArray();
            if (subProfiles is null) continue;

            foreach (var groupNode in subProfiles)
            {
                if (groupNode is null) continue;

                var groupName   = groupNode["GroupName"]?.GetValue<string>() ?? string.Empty;
                var activeMode  = groupNode["LightingMode"]?.GetValue<int>()      ?? 0;
                var activeInner = groupNode["LightingModeInner"]?.GetValue<int>() ?? 0;
                var activeOuter = groupNode["LightingModeOuter"]?.GetValue<int>() ?? 0;

                _logger.LogDebug(
                    "Parsing group '{Group}' on device '{Device}': Mode={Mode}, Inner={Inner}, Outer={Outer}",
                    groupName, devicePath, activeMode, activeInner, activeOuter);

                var allSettings = groupNode["LightingSettings"]?.AsObject();
                if (allSettings is null) continue;

                var deviceConfig = new DeviceConfig
                {
                    DevicePath = $"{devicePath}::{groupName}",
                    DeviceType = "LightingSetting",
                    Settings   = new List<LightingSetting>()
                };

                deviceConfig.Settings.Add(ExtractActiveSetting(allSettings, activeMode,  port: 0, label: "Global"));
                deviceConfig.Settings.Add(ExtractActiveSetting(allSettings, activeInner, port: 1, label: "Inner"));
                deviceConfig.Settings.Add(ExtractActiveSetting(allSettings, activeOuter, port: 2, label: "Outer"));

                profile.Devices.Add(deviceConfig);
            }
        }

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s).",
            profileName, profile.Devices.Count);

        return profile;
    }

    // -- Helpers --------------------------------------------------------------

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        // En dev : ContentRootPath = .../LConnect.AutoProfiler.Host
        // On remonte d'un niveau pour atteindre la racine du repo
        var repoRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }

    private LightingSetting ExtractActiveSetting(
        JsonObject allSettings, int targetMode, int port, string label)
    {
        foreach (var entry in allSettings)
        {
            var node = entry.Value;
            if (node?["Mode"]?.GetValue<int>() != targetMode) continue;

            _logger.LogDebug("  [{Label}] Found mode {Mode} -> key '{Key}'", label, targetMode, entry.Key);

            return new LightingSetting
            {
                Port       = port,
                Mode       = targetMode,
                Speed      = node["Speed"]?.GetValue<int>()      ?? 75,
                Direction  = node["Direction"]?.GetValue<int>()  ?? 0,
                Brightness = node["Brightness"]?.GetValue<int>() ?? 100,
                Colors     = ExtractColors(node["Colors"]?.AsArray())
            };
        }

        _logger.LogWarning("  [{Label}] Mode {Mode} not found in LightingSettings -- using defaults.", label, targetMode);
        return new LightingSetting { Port = port, Mode = targetMode };
    }

    private static List<LightingColor> ExtractColors(JsonArray? colorsArray)
    {
        if (colorsArray is null) return new List<LightingColor>();

        var result = new List<LightingColor>();
        foreach (var colorNode in colorsArray)
        {
            if (colorNode is null) continue;
            result.Add(new LightingColor
            {
                R   = colorNode["R"]?.GetValue<int>()      ?? 0,
                G   = colorNode["G"]?.GetValue<int>()      ?? 0,
                B   = colorNode["B"]?.GetValue<int>()      ?? 0,
                ScR = colorNode["ScR"]?.GetValue<double>() ?? 0,
                ScG = colorNode["ScG"]?.GetValue<double>() ?? 0,
                ScB = colorNode["ScB"]?.GetValue<double>() ?? 0,
            });
        }
        return result;
    }
}
