using System;
using System.Threading;
using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LConnect.AutoProfiler.Application.Services;

/// <summary>
/// BackgroundService central.
/// Réagit aux changements de fenêtre active et applique le bon profil RGB/ventilation.
/// </summary>
public sealed class ProfileOrchestrator : BackgroundService
{
    private readonly IWindowMonitor       _windowMonitor;
    private readonly IProfileRuleEngine   _ruleEngine;
    private readonly IProfileParser       _parser;
    private readonly ILConnectApiClient   _apiClient;
    private readonly ILogger<ProfileOrchestrator> _logger;

    private string _lastProcessName = string.Empty;

    public ProfileOrchestrator(
        IWindowMonitor     windowMonitor,
        IProfileRuleEngine ruleEngine,
        IProfileParser     parser,
        ILConnectApiClient apiClient,
        ILogger<ProfileOrchestrator> logger)
    {
        _windowMonitor = windowMonitor;
        _ruleEngine    = ruleEngine;
        _parser        = parser;
        _apiClient     = apiClient;
        _logger        = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _windowMonitor.OnForegroundProcessChanged += OnForegroundProcessChanged;
        _windowMonitor.StartMonitoring();

        _logger.LogInformation("ProfileOrchestrator started — watching foreground window…");

        // Bloquer proprement jusqu'à l'arrêt du service
        return Task.Delay(Timeout.Infinite, stoppingToken)
                   .ContinueWith(_ =>
                   {
                       _windowMonitor.StopMonitoring();
                       _logger.LogInformation("ProfileOrchestrator stopped.");
                   }, TaskScheduler.Default);
    }

    private async void OnForegroundProcessChanged(object? sender, string processName)
    {
        // Évite de renvoyer exactement le même profil en boucle
        if (string.Equals(processName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
            return;

        _lastProcessName = processName;
        _logger.LogDebug("Foreground process changed → {Process}", processName);

        try
        {
            var profileName = _ruleEngine.GetProfileNameForProcess(processName);

            if (string.IsNullOrEmpty(profileName))
            {
                _logger.LogWarning("No profile mapped for process: {Process}", processName);
                return;
            }

            _logger.LogInformation("Applying profile '{Profile}' for '{Process}'", profileName, processName);

            var profile = await _parser.ParseProfileAsync(profileName);

            foreach (var deviceConfig in profile.Devices)
                await _apiClient.ApplyLightingAsync(deviceConfig);
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
}
