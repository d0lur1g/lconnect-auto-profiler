using System;
using System.Collections.Generic;
using LConnect.AutoProfiler.Core.Models;

namespace LConnect.AutoProfiler.Infrastructure.Http;

/// <summary>
/// DTO de sérialisation pour ScreenLEDLighting.
/// Convertit Speed (0–100 interne) → valeur API (échelle inverse 0–255).
/// L'API interprète Speed comme un délai : 0 = le plus rapide, 255 = le plus lent.
/// </summary>
internal sealed record AioLightingSectionPayload(
    List<LightingColor> Colors,
    int? Speed,
    int Brightness,
    int Direction)
{
    /// <summary>
    /// Mappe Speed% [0–100] → délai API [255–0].
    /// int.MinValue (StaticColor) ou valeur négative → null (pas de vitesse applicable).
    /// </summary>
    public static int? ConvertSpeed(int? raw) => raw switch
    {
        null             => null,
        int.MinValue     => null,
        int v when v < 0 => null,
        int v            => (int)Math.Round((100 - Math.Clamp(v, 0, 100)) / 100.0 * 255)
    };

    public static AioLightingSectionPayload From(AioLightingSection s) => new(
        s.Colors,
        ConvertSpeed(s.Speed),
        s.Brightness,
        s.Direction);
}

internal sealed record AioLightingPayload(
    int Mode,
    bool IsDynamicMode,
    int SensorType,
    AioLightingSectionPayload Static,
    AioLightingSectionPayload DynamicHigh,
    AioLightingSectionPayload DynamicLow,
    SensorRange Range)
{
    public static AioLightingPayload From(AioLightingConfig c) => new(
        c.Mode,
        c.IsDynamicMode,
        c.SensorType,
        AioLightingSectionPayload.From(c.Static),
        AioLightingSectionPayload.From(c.DynamicHigh),
        AioLightingSectionPayload.From(c.DynamicLow),
        c.Range);
}
