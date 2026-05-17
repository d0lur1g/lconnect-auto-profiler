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

        // Appliquer immédiatement le profil correspondant à la fenêtre déjà active
        var current = _windowMonitor.GetCurrentForegroundProcessName();
        if (!string.IsNullOrEmpty(current))
            await ApplyProfileForProcessAsync(current);
        else
            await ApplyProfileForProcessAsync(string.Empty); // déclenchera le default

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
        _logger.LogDebug("Foreground process changed → {Process}", processName);

        await ApplyProfileForProcessAsync(processName);
    }

    /// <summary>
    /// Détermine et applique le profil pour un nom de processus donné.
    /// Si le processus est vide ou inconnu, le profil par défaut est utilisé.
    /// </summary>
    private async Task ApplyProfileForProcessAsync(string processName)
    {
        try
        {
            var profileName = _ruleEngine.GetProfileNameForProcess(processName);

            if (string.IsNullOrEmpty(profileName))
            {
                _logger.LogWarning("No profile mapped for process '{Process}' and no default defined.", processName);
                return;
            }

            _logger.LogInformation("Applying profile '{Profile}' for '{Process}'", profileName, processName);

            var profile = await _parser.ParseProfileAsync(profileName);
            await ApplyProfileAsync(profile);
        }
        catch (ProfileNotFoundException ex)
        {
            _logger.LogError(ex, "Profile file not found: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while applying profile for '{Process}'", processName);
        }
    }

    /// <summary>
    /// Applique le profil en respectant l'ordre du Program.cs de référence :
    ///
    ///   ÉTAPE 1 — Éclairage en PREMIER (GA II + AIO en parallèle)
    ///     LightingSetting  (GA II)
    ///     ScreenLEDLighting (AIO)
    ///
    ///   ÉTAPE 2 — Ventilateurs après (GA II + AIO en parallèle)
    ///     SetFanSpeed (GA II)
    ///     PumpSpeed   (AIO)
    ///     FanSpeed    (AIO)
    ///
    /// GA II et AIO s'exécutent en parallèle entre eux à chaque étape,
    /// mais les deux étapes restent séquentielles : l'éclairage passe
    /// toujours en premier, avant que les fans ne mobilisent le device.
    /// </summary>
    private async Task ApplyProfileAsync(LightingProfile profile)
    {
        // Étape 1 : éclairage uniquement
        var lightingDevices = profile.Devices
            .Where(d => d.DeviceType is "LightingSetting" or "ScreenLEDLighting")
            .ToList();

        if (lightingDevices.Count > 0)
            await Task.WhenAll(lightingDevices.Select(d => _apiClient.ApplyAsync(d)));

        // Étape 2 : fans / pompe
        var fanDevices = profile.Devices
            .Where(d => d.DeviceType is "SetFanSpeed" or "PumpSpeed" or "FanSpeed")
            .ToList();

        if (fanDevices.Count > 0)
            await Task.WhenAll(fanDevices.Select(d => _apiClient.ApplyAsync(d)));
    }
}
