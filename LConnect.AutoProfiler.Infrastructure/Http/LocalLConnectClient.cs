using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Interfaces;
using LConnect.AutoProfiler.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LConnect.AutoProfiler.Infrastructure.Http;

public sealed class LocalLConnectClient : ILConnectApiClient
{
    private readonly HttpClient _http;
    private readonly LConnectApiOptions _options;
    private readonly ILogger<LocalLConnectClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public LocalLConnectClient(
        HttpClient http,
        IOptions<LConnectApiOptions> options,
        ILogger<LocalLConnectClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ApplyAsync(DeviceConfig config)
    {
        var encodedPath = Uri.EscapeDataString(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(config.DevicePath)));

        var url = $"{_options.BaseUrl}?action=Device&devicePath={encodedPath}&type={config.DeviceType}";

        object payload = config.DeviceType switch
        {
            "LightingSetting" => (object)(config.Settings
                ?? throw new InvalidOperationException("Settings requis pour LightingSetting")),

            "ScreenLEDLighting" => config.AioLighting
                ?? throw new InvalidOperationException("AioLighting requis pour ScreenLEDLighting"),

            "SetFanSpeed" => (object)(config.FanGroups
                ?? throw new InvalidOperationException("FanGroups requis pour SetFanSpeed")),

            "PumpSpeed" or "FanSpeed" => config.FanCurve
                ?? throw new InvalidOperationException($"FanCurve requis pour {config.DeviceType}"),

            _ => throw new NotSupportedException($"DeviceType inconnu : {config.DeviceType}")
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("POST {Url} | Payload: {Payload}", url, json);

        await PostAsync(url, json, content, config.DeviceType, config.DevicePath);
    }

    public async Task SendMergeOrderAsync(string devicePath, MergeOrderConfig mergeOrder)
    {
        var encodedPath = Uri.EscapeDataString(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(devicePath)));

        // Étape 1 : MergeOrder — envoie l'ordre des devices [0,1,2,3]
        var urlOrder = $"{_options.BaseUrl}?action=Device&devicePath={encodedPath}&type=MergeOrder";
        var jsonOrder = JsonSerializer.Serialize(mergeOrder.DeviceOrder, JsonOpts);
        var contentOrder = new StringContent(jsonOrder, Encoding.UTF8, "application/json");
        _logger.LogDebug("POST {Url} | Payload: {Payload}", urlOrder, jsonOrder);
        await PostAsync(urlOrder, jsonOrder, contentOrder, "MergeOrder", devicePath);

        // Étape 2 : LightingSetting du MergeOrder — DÉSACTIVÉ temporairement pour test.
        // Ce payload (Port=0, Mode=1, Speed=50, Brightness=50, Colors=[]) écrase le LightingSetting
        // GA II appliqué juste avant et éteint les LEDs.
        // À réactiver uniquement si le MergeOrder nécessite un mode de fusion spécifique.
        //
        // if (mergeOrder.LightingSettings is not null)
        // {
        //     var urlLighting = $"{_options.BaseUrl}?action=Device&devicePath={encodedPath}&type=LightingSetting";
        //     var jsonLighting = JsonSerializer.Serialize(mergeOrder.LightingSettings, JsonOpts);
        //     var contentLighting = new StringContent(jsonLighting, Encoding.UTF8, "application/json");
        //     _logger.LogDebug("POST {Url} | Payload: {Payload}", urlLighting, jsonLighting);
        //     await PostAsync(urlLighting, jsonLighting, contentLighting, "LightingSetting", devicePath);
        // }

        _logger.LogDebug("[MergeOrder] LightingSetting step skipped (disabled for test).");
    }

    private async Task PostAsync(string url, string json, StringContent content, string type, string devicePath)
    {
        try
        {
            var response = await _http.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("✓ [{Type}] OK — {Path}", type, devicePath);
            else
                _logger.LogWarning("✗ [{Type}] HTTP {Status} — {Body}",
                    type,
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "L-Connect injoignable à {Url}", _options.BaseUrl);
        }
    }
}
