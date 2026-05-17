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

        // Sélection du bon payload selon le type
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

        try
        {
            var response = await _http.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("✓ [{Type}] OK — {Path}", config.DeviceType, config.DevicePath);
            else
                _logger.LogWarning("✗ [{Type}] HTTP {Status} — {Body}",
                    config.DeviceType,
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "L-Connect injoignable à {Url}", _options.BaseUrl);
        }
    }
}