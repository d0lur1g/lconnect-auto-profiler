using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LConnect.AutoProfiler.Application.Exceptions;
using LConnect.AutoProfiler.Core.Interfaces;
using LConnect.AutoProfiler.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// BackgroundService central.
/// Réagit aux changements de fenêtre active et applique le bon profil RGB/ventilation.
/// </summary>
public sealed class ProfileOrchestrator : BackgroundService
{
    private readonly IWindowMonitor _windowMonitor;
    private readonly IProfileRuleEngine _ruleEngine;
    private readonly IProfileParser _parser;
    private readonly ILConnectApiClient _apiClient;
    private readonly ILogger<ProfileOrchestrator> _logger;

    private string _lastProcessName = string.Empty;
    private string _lastProfileName = string.Empty;

    public ProfileOrchestrator(
        IWindowMonitor windowMonitor,
        IProfileRuleEngine ruleEngine,
        IProfileParser parser,
        ILConnectApiClient apiClient,
        ILogger<ProfileOrchestrator> logger)
    {
        _windowMonitor = windowMonitor;
        _ruleEngine    = ruleEngine;
        _parser        = parser;
        _apiClient     = apiClient;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _windowMonitor.OnForegroundProcessChanged += OnForegroundProcessChanged;
        _windowMonitor.StartMonitoring();

        _logger.LogInformation("ProfileOrchestrator started — watching foreground window…");

        var current = _windowMonitor.GetCurrentForegroundProcessName();
        await ApplyProfileForProcessAsync(current);

        await Task.Delay(Timeout.Infinite, stoppingToken)
                  .ContinueWith(_ =>
                  {
                      _windowMonitor.StopMonitoring();
                      _logger.LogInformation("ProfileOrchestrator stopped.");
                  }, TaskScheduler.Default);
    }

    private async void OnForegroundProcessChanged(object? sender, string processName)
    {
        if (string.Equals(processName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
            return;

        _lastProcessName = processName;
        await ApplyProfileForProcessAsync(processName);
    }

    private async Task ApplyProfileForProcessAsync(string processName)
    {
        try
        {
            var profileName = _ruleEngine.GetProfileNameForProcess(processName);

            if (string.IsNullOrEmpty(profileName))
            {
                _logger.LogWarning("[Focus] '{Process}' → aucun profil défini (default vide).", processName);
                return;
            }

            if (string.Equals(profileName, _lastProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Focus] '{Process}' → '{Profile}' (déjà actif, ignoré)",
                    string.IsNullOrEmpty(processName) ? "(bureau/système)" : processName, profileName);
                return;
            }

            _lastProfileName = profileName;

            _logger.LogInformation("[Focus] '{Process}' → '{Profile}'",
                string.IsNullOrEmpty(processName) ? "(bureau/système)" : processName,
                profileName);

            var profile = await _parser.ParseProfileAsync(profileName);
            await ApplyProfileAsync(profile);
        }
        catch (ProfileNotFoundException ex)
        {
            _logger.LogError(ex, "[Focus] Profil introuvable : {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Focus] Erreur inattendue pour '{Process}'", processName);
        }
    }

    /// <summary>
    /// Applique le profil dans l'ordre :
    ///
    ///   ÉTAPE 1 — Éclairage en PREMIER (GA II + AIO en parallèle)
    ///     LightingSetting   (GA II)
    ///     ScreenLEDLighting (AIO)
    ///
    ///   ÉTAPE 2 — MergeOrder (si présent dans le profil)
    ///
    ///   ÉTAPE 3 — Ventilateurs (GA II + AIO en parallèle)
    ///     SetFanSpeed (GA II)
    ///     PumpSpeed   (AIO)
    ///     FanSpeed    (AIO)
    /// </summary>
    private async Task ApplyProfileAsync(LightingProfile profile)
    {
        // Étape 1 : éclairage
        var lightingDevices = profile.Devices
            .Where(d => d.DeviceType is "LightingSetting" or "ScreenLEDLighting")
            .ToList();

        if (lightingDevices.Count > 0)
            await Task.WhenAll(lightingDevices.Select(d => _apiClient.ApplyAsync(d)));

        // Étape 2 : MergeOrder
        if (profile.MergeOrder is not null)
        {
            var devicePath = profile.MergeOrder.DevicePath ?? string.Empty;
            await _apiClient.SendMergeOrderAsync(devicePath, profile.MergeOrder);
        }

        // Étape 3 : ventilateurs
        var fanDevices = profile.Devices
            .Where(d => d.DeviceType is "SetFanSpeed" or "PumpSpeed" or "FanSpeed")
            .ToList();

        if (fanDevices.Count > 0)
            await Task.WhenAll(fanDevices.Select(d => _apiClient.ApplyAsync(d)));
    }
}
