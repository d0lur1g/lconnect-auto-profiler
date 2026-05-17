using System.Collections.Generic;

namespace LConnect.AutoProfiler.Core.Models;

public class FanCurveConfig
{
    public int MaxSpeed { get; set; }
    public int Reference { get; set; } = 0;
    public List<FanPhase> Phases { get; set; } = new();
}