using System.Threading.Tasks;
using LConnect.AutoProfiler.Core.Models;

namespace LConnect.AutoProfiler.Core.Interfaces;

public interface ILConnectApiClient
{
    Task ApplyLightingAsync(DeviceConfig config);
}
