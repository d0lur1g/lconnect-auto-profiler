namespace LConnect.AutoProfiler.Core.Models;

public sealed class LightingSetting
{
    public int  Port       { get; init; }
    public int  Mode       { get; init; }

    /// <summary>
    /// Vitesse de l'animation. Null si le mode ne supporte pas de vitesse (ex: StaticColor).
    /// Valeur brute issue du JSON profil L-Connect, passée telle quelle à l'API.
    /// </summary>
    public int? Speed      { get; init; }

    public int  Direction  { get; init; }

    /// <summary>
    /// Luminosité. Valeur brute issue du JSON profil L-Connect, passée telle quelle à l'API.
    /// </summary>
    public int  Brightness { get; init; }

    public List<LightingColor> Colors { get; init; } = new();
}
