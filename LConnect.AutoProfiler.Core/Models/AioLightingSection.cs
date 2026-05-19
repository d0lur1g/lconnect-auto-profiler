using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

/// <summary>
/// Représente une section d'éclairage AIO (Static, DynamicHigh, DynamicLow)
/// pour le type HTTP ScreenLEDLighting.
///
/// Speed      : valeur interne 0–100 issue du profil L-Connect.
///              NE PAS envoyer telle quelle à l'API — l'API interprète Speed
///              comme un délai (relation inverse : 0 = max rapide, 255 = max lent).
///              La conversion est appliquée dans AioLightingPayload.ConvertSpeed()
///              côté Infrastructure au moment de la sérialisation.
///              int.MinValue = mode sans animation (StaticColor) — converti en null.
///
/// Brightness : valeur brute 0–100, même convention que l'API.
///
/// Direction  : 0 = pas de direction spécifique.
/// </summary>
public class AioLightingSection
{
    public List<LightingColor> Colors { get; set; } = new();
    public int? Speed      { get; set; }
    public int  Brightness { get; set; }
    public int  Direction  { get; set; } = 0;
}
