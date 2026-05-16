namespace LConnect.AutoProfiler.Infrastructure.FileSystem;

/// <summary>
/// Options de configuration pour le parser JSON.
/// Binding depuis appsettings.json → ProfileParser:ProfilesDirectory
/// </summary>
public sealed class ProfileParserOptions
{
    public const string SectionName = "ProfileParser";

    /// <summary>
    /// Chemin absolu ou relatif vers le dossier contenant les fichiers JSON de profil.
    /// Exemple : "C:\\Profiles" ou "./profiles"
    /// </summary>
    public string ProfilesDirectory { get; set; } = "./profiles";
}
