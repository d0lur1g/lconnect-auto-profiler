using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Models;

namespace LConnect.AutoProfiler.Core.Interfaces;

/// <summary>
/// Lit un fichier JSON de sauvegarde L-Connect et en extrait
/// uniquement les paramètres actifs (mode, couleurs, vitesse, ventilation).
/// </summary>
public interface IProfileParser
{
    Task<LightingProfile> ParseProfileAsync(string profileName);
}
