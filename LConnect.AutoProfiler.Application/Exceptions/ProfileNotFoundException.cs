using System;

namespace LConnect.AutoProfiler.Application.Exceptions;

/// <summary>
/// Levée lorsque le fichier JSON d'un profil demandé est introuvable.
/// </summary>
public sealed class ProfileNotFoundException : Exception
{
    public string ProfileName { get; }

    public ProfileNotFoundException(string profileName)
        : base($"Profile '{profileName}' could not be found.")
    {
        ProfileName = profileName;
    }
}
