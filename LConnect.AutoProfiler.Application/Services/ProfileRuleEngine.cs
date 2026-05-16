using System;
using LConnect.AutoProfiler.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// Détermine quel profil appliquer en fonction du processus actif.
/// Les règles sont configurées dans appsettings.json → ProfileRules:Mappings.
/// </summary>
public sealed class ProfileRuleEngine : IProfileRuleEngine
{
    private readonly ProfileRuleOptions _options;

    public ProfileRuleEngine(IOptions<ProfileRuleOptions> options)
    {
        _options = options.Value;
    }

    public string GetProfileNameForProcess(string processName)
    {
        // Recherche exacte (insensible à la casse)
        foreach (var (key, profileName) in _options.Mappings)
        {
            if (string.Equals(key, processName, StringComparison.OrdinalIgnoreCase))
                return profileName;
        }

        // Fallback sur le profil "default" s'il est défini
        return _options.Mappings.TryGetValue("default", out var defaultProfile)
            ? defaultProfile
            : string.Empty;
    }
}
