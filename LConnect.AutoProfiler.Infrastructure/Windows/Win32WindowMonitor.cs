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
/// Poll toutes les secondes — consommation CPU négligeable (~0%).
/// </summary>
public sealed class Win32WindowMonitor : IWindowMonitor, IDisposable
{
    // ── Win32 P/Invoke ──────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ── Membres ─────────────────────────────────────────────────────────────
    private readonly ILogger<Win32WindowMonitor> _logger;
    private CancellationTokenSource? _cts;
    private string _lastProcessName = string.Empty;

    public event EventHandler<string>? OnForegroundProcessChanged;

    public Win32WindowMonitor(ILogger<Win32WindowMonitor> logger)
    {
        _logger = logger;
    }

    // ── API publique ─────────────────────────────────────────────────────────
    public void StartMonitoring()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => PollLoop(_cts.Token));
        _logger.LogInformation("Win32WindowMonitor: polling started.");
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _logger.LogInformation("Win32WindowMonitor: polling stopped.");
    }

    public string GetCurrentForegroundProcessName()
        => ResolveProcessName(GetForegroundWindow());

    // ── Boucle interne ───────────────────────────────────────────────────────
    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var current = GetCurrentForegroundProcessName();

                if (!string.Equals(current, _lastProcessName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(current))
                {
                    _lastProcessName = current;
                    _logger.LogDebug("Foreground → {Process}", current);
                    OnForegroundProcessChanged?.Invoke(this, current);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Win32WindowMonitor: error during poll.");
            }

            await Task.Delay(1000, token); // Poll toutes les secondes
        }
    }

    private static string ResolveProcessName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;

        GetWindowThreadProcessId(hWnd, out var pid);

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName + ".exe"; // ex: "cyberpunk2077.exe"
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose() => _cts?.Dispose();
}
