namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// Options lues depuis appsettings.json → section "ProfileRules".
/// Pointe vers le fichier data/rules/rules.json qui contient
/// le mapping process → nom de profil.
/// </summary>
public sealed class ProfileRuleOptions
{
    public const string SectionName = "ProfileRules";

    /// <summary>
    /// Chemin vers le fichier rules.json.
    /// Relatif au répertoire de travail du service (ex: "data/rules/rules.json").
    /// </summary>
    public string RulesFile { get; set; } = "data/rules/rules.json";
}
