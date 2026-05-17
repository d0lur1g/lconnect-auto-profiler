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
/// Lit un fichier JSON fully_decoded L-Connect et construit les DeviceConfig
/// en reproduisant exactement la logique du Program.cs de référence :
///
///   - Une seule requête LightingSetting par devicePath (contrôleur)
///   - Les ports sont calculés : Inner = groupIndex*2, Outer = groupIndex*2+1
///   - ScR/ScG/ScB = Math.Pow(canal/255.0, 2.2) recalculé depuis R/G/B
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

            // Chemin HID brut du contrôleur (sera encodé Base64+URL par le client HTTP)
            var devicePath  = dataEntry["Metadata"]?.GetValue<string>() ?? string.Empty;
            var subProfiles = dataEntry["Data"]?["SubProfiles"]?.AsArray();
            if (subProfiles is null || subProfiles.Count == 0) continue;

            // Une seule requête LightingSetting par devicePath
            // contenant tous les ports de tous les groupes
            var deviceConfig = new DeviceConfig
            {
                DevicePath = devicePath,
                DeviceType = "LightingSetting",
                Settings   = new List<LightingSetting>()
            };

            int groupIndex = 0;
            foreach (var groupNode in subProfiles)
            {
                if (groupNode is null) continue;

                var groupName   = groupNode["GroupName"]?.GetValue<string>() ?? string.Empty;
                var activeInner = groupNode["LightingModeInner"]?.GetValue<int>() ?? 0;
                var activeOuter = groupNode["LightingModeOuter"]?.GetValue<int>() ?? 0;

                _logger.LogDebug(
                    "Group[{Index}] '{Name}': Inner={Inner}, Outer={Outer}",
                    groupIndex, groupName, activeInner, activeOuter);

                var allSettings = groupNode["LightingSettings"]?.AsObject();
                if (allSettings is null) { groupIndex++; continue; }

                // Port pair = Inner, port impair = Outer (identique au Program.cs)
                deviceConfig.Settings.Add(
                    ExtractActiveSetting(allSettings, activeInner, port: groupIndex * 2,     label: $"{groupName} Inner"));
                deviceConfig.Settings.Add(
                    ExtractActiveSetting(allSettings, activeOuter, port: groupIndex * 2 + 1, label: $"{groupName} Outer"));

                groupIndex++;
            }

            if (deviceConfig.Settings.Count > 0)
                profile.Devices.Add(deviceConfig);
        }

        _logger.LogInformation("Profile '{Name}' parsed: {DeviceCount} device(s).",
            profileName, profile.Devices.Count);

        return profile;
    }

    // -------------------------------------------------------------------------

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

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

            _logger.LogDebug("  [{Label}] mode {Mode} -> '{Key}'", label, targetMode, entry.Key);

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

        _logger.LogWarning("  [{Label}] mode {Mode} not found -- using defaults.", label, targetMode);
        return new LightingSetting { Port = port, Mode = targetMode };
    }

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
                // Recalcul gamma 2.2 identique au Program.cs de référence
                ScR = Math.Pow(r / 255.0, 2.2),
                ScG = Math.Pow(g / 255.0, 2.2),
                ScB = Math.Pow(b / 255.0, 2.2),
            });
        }
        return result;
    }
}
