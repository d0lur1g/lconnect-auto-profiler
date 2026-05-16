using System.Collections.Generic;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// Options de configuration injectées depuis appsettings.json.
/// Fait correspondre un nom d'exécutable à un nom de profil.
/// Exemple :
/// {
///   "ProfileRules": {
///     "Mappings": {
///       "cyberpunk2077.exe": "cyberpunk-pink-blue",
///       "default":           "girl-boy"
///     }
///   }
/// }
/// </summary>
public sealed class ProfileRuleOptions
{
    public const string SectionName = "ProfileRules";

    /// <summary>
    /// Clé : nom du processus (ex: "cyberpunk2077.exe") — insensible à la casse.
    /// Valeur : nom du profil JSON à charger.
    /// La clé spéciale "default" est appliquée si aucune règle ne correspond.
    /// </summary>
    public Dictionary<string, string> Mappings { get; set; } = new();
}
