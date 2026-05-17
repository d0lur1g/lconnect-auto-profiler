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

/// <summary>
/// Envoie les requêtes POST vers l'API locale L-Connect (127.0.0.1:11021).
///
/// Encodage devicePath : Base64(UTF8) puis Uri.EscapeDataString
/// (identique à ce que fait L-Connect UI, confirmé par capture Fiddler).
///
/// Sérialisation JSON : PascalCase (PropertyNamingPolicy = null)
/// L-Connect attend Port/Mode/Colors/R/G/B en majuscules.
/// </summary>
public sealed class LocalLConnectClient : ILConnectApiClient
{
    private readonly HttpClient _http;
    private readonly LConnectApiOptions _options;
    private readonly ILogger<LocalLConnectClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,   // PascalCase
        WriteIndented        = false
    };

    public LocalLConnectClient(
        HttpClient                   http,
        IOptions<LConnectApiOptions> options,
        ILogger<LocalLConnectClient> logger)
    {
        _http    = http;
        _options = options.Value;
        _logger  = logger;
    }

    public async Task ApplyLightingAsync(DeviceConfig config)
    {
        // Encodage : Base64(UTF8(rawHidPath)) puis URL-encode
        var encodedPath = Uri.EscapeDataString(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(config.DevicePath)));

        var url     = $"{_options.BaseUrl}?action=Device&devicePath={encodedPath}&type={config.DeviceType}";
        var payload = JsonSerializer.Serialize(config.Settings, JsonOpts);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        _logger.LogDebug("POST {Url} -- Payload: {Payload}", url, payload);

        try
        {
            var response = await _http.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("\u2713 Applied [{Type}] for device '{Path}'",
                    config.DeviceType, config.DevicePath);
            else
                _logger.LogWarning("\u2717 L-Connect returned {Status} for [{Type}] on '{Path}'",
                    (int)response.StatusCode, config.DeviceType, config.DevicePath);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot reach L-Connect service at {Url}. Is it running?", _options.BaseUrl);
        }
    }
}
