using LConnect.AutoProfiler.Core.Models;

namespace LConnect.AutoProfiler.Core.Interfaces;

public interface IProfileParser
{
    LightingProfile ParseProfile(string jsonContent);
}
