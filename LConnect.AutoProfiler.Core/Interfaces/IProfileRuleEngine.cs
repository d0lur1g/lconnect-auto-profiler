namespace LConnect.AutoProfiler.Core.Interfaces;

public interface IProfileRuleEngine
{
    /// <summary>Retourne le nom du profil associé à un processus, ou le profil par défaut.</summary>
    string GetProfileNameForProcess(string processName);

    /// <summary>Retourne le nom du profil par défaut défini dans rules.json.</summary>
    string GetDefaultProfileName();
}
