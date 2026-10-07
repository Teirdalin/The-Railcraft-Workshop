namespace Eco.Minecarts.Physics;

public sealed record IndustrialTrackDefinition(string Key, string Name, string Shape, bool Chain)
{
    public bool Bend => Shape.StartsWith("IndustrialBend", StringComparison.Ordinal);
    public int Footprint => Bend ? 7 : 3;
    public static readonly IndustrialTrackDefinition[] All = new[] { false, true }.SelectMany(chain =>
        new[] { "Straight", "IndustrialBend", "IndustrialBendLeft", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop1", "RampTop2", "RampTop3", "RampTop4" }
        .Select(shape => new IndustrialTrackDefinition((chain ? "IndustrialChain" : "IndustrialTrack") + shape.Replace("Industrial", ""),
            "Industrial " + (chain ? "Chain " : "") + (shape == "IndustrialBend" ? "Right Bend" : shape == "IndustrialBendLeft" ? "Left Bend" : shape), shape, chain))).ToArray();
    public static IndustrialTrackDefinition Find(string key) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Find"); return All.Single(d => d.Key == key); }
}
