using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LConnect.AutoProfiler.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// Charge data/rules/rules.json et détermine quel profil appliquer
/// en fonction du processus actif.
///
/// Le fichier est rechargé à chaud via IOptionsMonitor : toute modification
/// de appsettings.json (RulesFile) est prise en compte sans redémarrage.
/// </summary>
public sealed class ProfileRuleEngine : IProfileRuleEngine
{
    private readonly IOptionsMonitor<ProfileRuleOptions> _optionsMonitor;
    private readonly ILogger<ProfileRuleEngine> _logger;

    // Cache en mémoire, invalidé si le chemin change dans appsettings
    private RulesFileContent? _cache;
    private string? _loadedFromPath;

    public ProfileRuleEngine(
        IOptionsMonitor<ProfileRuleOptions> optionsMonitor,
        ILogger<ProfileRuleEngine> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger         = logger;

        // Invalide le cache si appsettings.json change
        _optionsMonitor.OnChange(_ => _cache = null);
    }

    public string GetProfileNameForProcess(string processName)
    {
        var rules = LoadRules();

        // Recherche exacte, insensible à la casse
        foreach (var (key, profileName) in rules.Mappings)
        {
            if (string.Equals(key, processName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Rule match: '{Process}' → '{Profile}'", processName, profileName);
                return profileName;
            }
        }

        _logger.LogDebug("No rule for '{Process}', using default '{Default}'.",
            processName, rules.Default);

        return rules.Default;
    }

    // ── Private ─────────────────────────────────────────────────────────────

    private RulesFileContent LoadRules()
    {
        var path = _optionsMonitor.CurrentValue.RulesFile;

        // Retourne le cache si le fichier source n'a pas changé
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

    // ── DTO interne (structure de rules.json) ─────────────────────────────────────

    private sealed class RulesFileContent
    {
        [JsonPropertyName("default")]
        public string Default { get; set; } = string.Empty;

        [JsonPropertyName("mappings")]
        public Dictionary<string, string> Mappings { get; set; } = new();
    }
}
