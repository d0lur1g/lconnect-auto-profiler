using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LConnect.AutoProfiler.Infrastructure.Windows;

/// <summary>
/// Surveille la fenêtre active via P/Invoke (Win32 API).
/// Poll toutes les 150 ms avec debounce 200 ms — consommation CPU négligeable (~0%).
/// </summary>
public sealed class Win32WindowMonitor : IWindowMonitor, IDisposable
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ── Constantes ────────────────────────────────────────────────────

    /// <summary>Fréquence de détection de la fenêtre active.</summary>
    private const int PollIntervalMs = 150;

    /// <summary>
    /// Durée pendant laquelle le processus détecté doit être stable avant
    /// de déclencher l'événement. Évite de spammer l'API lors d'un Alt+Tab rapide.
    /// </summary>
    private const int DebounceMs = 200;

    // ── Membres ────────────────────────────────────────────────────
    private readonly ILogger<Win32WindowMonitor> _logger;
    private CancellationTokenSource? _cts;
    private string _lastProcessName  = string.Empty;
    private string _pendingProcess   = string.Empty;
    private int    _pendingTicksMs   = 0;

    public event EventHandler<string>? OnForegroundProcessChanged;

    public Win32WindowMonitor(ILogger<Win32WindowMonitor> logger)
    {
        _logger = logger;
    }

    // ── API publique ───────────────────────────────────────────────────
    public void StartMonitoring()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => PollLoop(_cts.Token));
        _logger.LogInformation("Win32WindowMonitor: polling started (interval={Poll}ms, debounce={Debounce}ms).",
            PollIntervalMs, DebounceMs);
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _logger.LogInformation("Win32WindowMonitor: polling stopped.");
    }

    public string GetCurrentForegroundProcessName()
        => ResolveProcessName(GetForegroundWindow());

    // ── Boucle interne ──────────────────────────────────────────────────
    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var current = GetCurrentForegroundProcessName();

                if (string.IsNullOrEmpty(current))
                {
                    _pendingProcess = string.Empty;
                    _pendingTicksMs = 0;
                }
                else if (string.Equals(current, _pendingProcess, StringComparison.OrdinalIgnoreCase))
                {
                    // Même processus en attente : incrémenter le compteur de stabilité
                    _pendingTicksMs += PollIntervalMs;

                    if (_pendingTicksMs >= DebounceMs
                        && !string.Equals(current, _lastProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastProcessName = current;
                        _pendingTicksMs  = 0;
                        _logger.LogDebug("Foreground (stable) → {Process}", current);
                        OnForegroundProcessChanged?.Invoke(this, current);
                    }
                }
                else
                {
                    // Nouveau processus détecté — relancer le debounce
                    _pendingProcess = current;
                    _pendingTicksMs = PollIntervalMs; // un tick déjà écoulé
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Win32WindowMonitor: error during poll.");
            }

            await Task.Delay(PollIntervalMs, token);
        }
    }

    private static string ResolveProcessName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;

        GetWindowThreadProcessId(hWnd, out var pid);

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName + ".exe";
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose() => _cts?.Dispose();
}
