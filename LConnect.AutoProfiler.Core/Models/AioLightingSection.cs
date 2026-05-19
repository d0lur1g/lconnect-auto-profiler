using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

/// <summary>
/// Représente une section d’éclairage AIO (Static, DynamicHigh, DynamicLow)
/// pour le type HTTP ScreenLEDLighting.
///
/// Speed      : valeur brute 0–100, identique à celle stockée dans le profil L-Connect
///              et attendue telle quelle par l’API locale.
///              Confirmé par capture Wireshark : L-Connect envoie 25, 75, 100 directement.
///
/// Brightness : valeur brute 0–100, même convention.
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
