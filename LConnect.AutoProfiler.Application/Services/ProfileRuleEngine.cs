using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LConnect.AutoProfiler.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// Charge data/rules/rules.json et détermine quel profil appliquer
/// en fonction du processus actif.
///
/// Le chemin RulesFile est résolu relativement à la RACINE DU REPO
/// (ContentRootPath = Host/, donc on remonte d'un niveau).
/// En production (service installé), ContentRootPath = dossier d'installation,
/// le chemin relatif fonctionne directement sans remonter.
/// </summary>
public sealed class ProfileRuleEngine : IProfileRuleEngine
{
    private readonly IOptionsMonitor<ProfileRuleOptions> _optionsMonitor;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ProfileRuleEngine> _logger;

    private RulesFileContent? _cache;
    private string? _loadedFromPath;

    public ProfileRuleEngine(
        IOptionsMonitor<ProfileRuleOptions> optionsMonitor,
        IHostEnvironment env,
        ILogger<ProfileRuleEngine> logger)
    {
        _optionsMonitor = optionsMonitor;
        _env            = env;
        _logger         = logger;

        _optionsMonitor.OnChange(_ => _cache = null);
    }

    public string GetProfileNameForProcess(string processName)
    {
        var rules = LoadRules();

        foreach (var (key, profileName) in rules.Mappings)
        {
            if (string.Equals(key, processName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Rule match: '{Process}' -> '{Profile}'", processName, profileName);
                return profileName;
            }
        }

        _logger.LogDebug("No rule for '{Process}', using default '{Default}'.",
            processName, rules.Default);

        return rules.Default;
    }

    // -- Private --------------------------------------------------------------

    private string ResolvePath(string relativePath)
    {
        // Si chemin absolu, on le prend tel quel
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        // En dev : ContentRootPath = .../LConnect.AutoProfiler.Host
        // On remonte à la racine du repo pour trouver data/
        var repoRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }

    private RulesFileContent LoadRules()
    {
        var path = ResolvePath(_optionsMonitor.CurrentValue.RulesFile);

        if (_cache is not null && _loadedFromPath == path)
            return _cache;

        if (!File.Exists(path))
        {
            _logger.LogWarning("Rules file not found: '{Path}'. Using empty rules.", path);
            _cache          = new RulesFileContent();
            _loadedFromPath = path;
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(path);
            _cache = JsonSerializer.Deserialize<RulesFileContent>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling        = JsonCommentHandling.Skip,
                AllowTrailingCommas        = true
            }) ?? new RulesFileContent();

            _loadedFromPath = path;
            _logger.LogInformation(
                "Rules loaded from '{Path}': {Count} mapping(s), default='{Default}'.",
                path, _cache.Mappings.Count, _cache.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load rules file '{Path}'. Using empty rules.", path);
            _cache          = new RulesFileContent();
            _loadedFromPath = path;
        }

        return _cache;
    }

    // -- DTO interne ----------------------------------------------------------

    private sealed class RulesFileContent
    {
        [JsonPropertyName("default")]
        public string Default { get; set; } = string.Empty;

        [JsonPropertyName("mappings")]
        public Dictionary<string, string> Mappings { get; set; } = new();
    }
}
