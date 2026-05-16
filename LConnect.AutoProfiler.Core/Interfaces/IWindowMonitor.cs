using System;

namespace LConnect.AutoProfiler.Core.Interfaces;

public interface IWindowMonitor
{
    event EventHandler<string> OnForegroundProcessChanged;
    void StartMonitoring();
    void StopMonitoring();
    string GetCurrentForegroundProcessName();
}
