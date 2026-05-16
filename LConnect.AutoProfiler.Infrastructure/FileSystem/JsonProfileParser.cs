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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Infrastructure.FileSystem;

/// <summary>
/// Lit un fichier JSON décodé L-Connect et extrait UNIQUEMENT
/// les paramètres du mode actif (LightingMode, Inner, Outer, couleurs, vitesse).
/// </summary>
public sealed class JsonProfileParser : IProfileParser
{
    private readonly ProfileParserOptions _options;
    private readonly ILogger<JsonProfileParser> _logger;

    public JsonProfileParser(
        IOptions<ProfileParserOptions> options,
        ILogger<JsonProfileParser> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<LightingProfile> ParseProfileAsync(string profileName)
    {
        var filePath = Path.Combine(_options.ProfilesDirectory, $"{profileName}.json");

        if (!File.Exists(filePath))
            throw new ProfileNotFoundException(profileName);

        var json    = await File.ReadAllTextAsync(filePath);
        var root    = JsonNode.Parse(json) ?? throw new InvalidDataException("Invalid JSON.");
        var profile = new LightingProfile { ProfileName = profileName };

        // Parcourt les groupes de contrôleurs (GA II, BOTTOM, BACK, Port4…)
        var groupsNode = root["FanGroups"]?.AsArray()
                      ?? root["LightingGroups"]?.AsArray()
                      ?? new JsonArray();

        foreach (var groupNode in groupsNode)
        {
            if (groupNode is null) continue;

            var devicePath  = groupNode["DevicePath"]?.GetValue<string>() ?? string.Empty;
            var deviceType  = groupNode["DeviceType"]?.GetValue<string>() ?? "LightingSetting";
            var activeMode  = groupNode["LightingMode"]?.GetValue<int>() ?? 0;
            var activeInner = groupNode["LightingModeInner"]?.GetValue<int>() ?? 0;
            var activeOuter = groupNode["LightingModeOuter"]?.GetValue<int>() ?? 0;

            _logger.LogDebug("Parsing group '{DevicePath}': Mode={Mode}, Inner={Inner}, Outer={Outer}",
                devicePath, activeMode, activeInner, activeOuter);

            var allSettings = groupNode["LightingSettings"]?.AsObject();
            if (allSettings is null) continue;

            var deviceConfig = new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = deviceType,
                Settings   = new List<LightingSetting>()
            };

            // Extraction du mode Global actif
            deviceConfig.Settings.Add(
                ExtractActiveSetting(allSettings, activeMode, port: 0, label: "Global"));

            // Extraction du mode Inner actif
            deviceConfig.Settings.Add(
                ExtractActiveSetting(allSettings, activeInner, port: 1, label: "Inner"));

            // Extraction du mode Outer actif
            deviceConfig.Settings.Add(
                ExtractActiveSetting(allSettings, activeOuter, port: 2, label: "Outer"));

            profile.Devices.Add(deviceConfig);
        }

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s).",
            profileName, profile.Devices.Count);

        return profile;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private LightingSetting ExtractActiveSetting(
        JsonObject allSettings, int targetMode, int port, string label)
    {
        // Cherche dans les clés du noeud LightingSettings l'entrée dont "Mode" == targetMode
        foreach (var entry in allSettings)
        {
            var node = entry.Value;
            if (node?["Mode"]?.GetValue<int>() != targetMode) continue;

            _logger.LogDebug("  [{Label}] Found mode {Mode} → key '{Key}'", label, targetMode, entry.Key);

            return new LightingSetting
            {
                Port       = port,
                Mode       = targetMode,
                Speed      = node["Speed"]?.GetValue<int>() ?? 75,
                Direction  = node["Direction"]?.GetValue<int>() ?? 0,
                Brightness = node["Brightness"]?.GetValue<int>() ?? 100,
                Colors     = ExtractColors(node["Colors"]?.AsArray())
            };
        }

        _logger.LogWarning("  [{Label}] Mode {Mode} not found in LightingSettings — using defaults.", label, targetMode);
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
                R   = colorNode["R"]?.GetValue<int>()    ?? 0,
                G   = colorNode["G"]?.GetValue<int>()    ?? 0,
                B   = colorNode["B"]?.GetValue<int>()    ?? 0,
                ScR = colorNode["ScR"]?.GetValue<double>() ?? 0,
                ScG = colorNode["ScG"]?.GetValue<double>() ?? 0,
                ScB = colorNode["ScB"]?.GetValue<double>() ?? 0,
            });
        }
        return result;
    }
}
