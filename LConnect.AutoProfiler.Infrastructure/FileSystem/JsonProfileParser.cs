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
/// Structure attendue :
///   root.Datas[i].Metadata          → DevicePath (chemin HID)
///   root.Datas[i].Data.SubProfiles[] → groupes (GA II, BOTTOM, BACK, Port4…)
///     SubProfiles[j].LightingMode        → mode global actif
///     SubProfiles[j].LightingModeInner   → mode inner actif
///     SubProfiles[j].LightingModeOuter   → mode outer actif
///     SubProfiles[j].LightingSettings    → tous les effets disponibles
/// &lt;/summary&gt;
public sealed class JsonProfileParser : IProfileParser
{
    private readonly ProfileParserOptions _options;
    private readonly ILogger&lt;JsonProfileParser&gt; _logger;

    public JsonProfileParser(
        IOptions&lt; ProfileParserOptions&gt; options,
        ILogger&lt;JsonProfileParser&gt; logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<LightingProfile> ParseProfileAsync(string profileName)
    {
        var dir = ResolvePath(_options.ProfilesDirectory);
        var filePath = Path.Combine(dir, $"{profileName}.json");

        if (!File.Exists(filePath))
            throw new ProfileNotFoundException(profileName);

        var json = await File.ReadAllTextAsync(filePath);
        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("Invalid JSON.");
        var profile = new LightingProfile { ProfileName = profileName };

        var datasNode = root["Datas"]?.AsArray() ?? new JsonArray();

        foreach (var dataEntry in datasNode)
        {
            if (dataEntry is null) continue;

            // Le DevicePath est le chemin HID unique du contrôleur
            var devicePath = dataEntry["Metadata"]?.GetValue & lt; string&gt; () ?? string.Empty;

            var subProfiles = dataEntry["Data"]?["SubProfiles"]?.AsArray();
            if (subProfiles is null) continue;

            foreach (var groupNode in subProfiles)
            {
                if (groupNode is null) continue;

                var groupName = groupNode["GroupName"]?.GetValue & lt; string&gt; () ?? string.Empty;
                var activeMode = groupNode["LightingMode"]?.GetValue & lt; int&gt; () ?? 0;
                var activeInner = groupNode["LightingModeInner"]?.GetValue & lt; int&gt; () ?? 0;
                var activeOuter = groupNode["LightingModeOuter"]?.GetValue & lt; int&gt; () ?? 0;

                _logger.LogDebug(
                    "Parsing group '{Group}' on device '{Device}': Mode={Mode}, Inner={Inner}, Outer={Outer}",
                    groupName, devicePath, activeMode, activeInner, activeOuter);

                var allSettings = groupNode["LightingSettings"]?.AsObject();
                if (allSettings is null) continue;

                var deviceConfig = new DeviceConfig
                {
                    DevicePath = $"{devicePath}::{groupName}",
                    DeviceType = "LightingSetting",
                    Settings = new List& lt; LightingSetting & gt; ()
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
        }

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s).",
            profileName, profile.Devices.Count);

        return profile;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
                Port = port,
                Mode = targetMode,
                Speed = node["Speed"]?.GetValue & lt; int & gt; () ?? 75,
                Direction = node["Direction"]?.GetValue & lt; int & gt; () ?? 0,
                Brightness = node["Brightness"]?.GetValue & lt; int & gt; () ?? 100,
                Colors = ExtractColors(node["Colors"]?.AsArray())
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
                R = colorNode["R"]?.GetValue & lt; int & gt; () ?? 0,
                G = colorNode["G"]?.GetValue & lt; int & gt; () ?? 0,
                B = colorNode["B"]?.GetValue & lt; int & gt; () ?? 0,
                ScR = colorNode["ScR"]?.GetValue & lt; double & gt; () ?? 0,
                ScG = colorNode["ScG"]?.GetValue & lt; double & gt; () ?? 0,
                ScB = colorNode["ScB"]?.GetValue & lt; double & gt; () ?? 0,
            });
        }
        return result;
    }
}