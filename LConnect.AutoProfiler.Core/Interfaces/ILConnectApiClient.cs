using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Models;

namespace LConnect.AutoProfiler.Core.Interfaces;

/// <summary>
/// Communique avec le service local L-Connect via HTTP
/// pour appliquer les configurations matérielles (RGB, ventilation, pompe…).
/// </summary>
public interface ILConnectApiClient
{
    Task ApplyLightingAsync(DeviceConfig config);
}
