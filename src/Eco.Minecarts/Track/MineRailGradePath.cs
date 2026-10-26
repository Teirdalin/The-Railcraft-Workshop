using System.Numerics;

namespace Eco.Minecarts.Track;

// Standard Rail and Powered Chain Rail share the same one-cell grade paths.
// Each eased transition spans two cells, so its pitch never exceeds 45 degrees.
public sealed record MineRailGradePath(string Shape, string Name, string Description, string? NextShape, int NextHeight, CoasterPath Path)
{
    public static readonly MineRailGradePath[] All = Build();
    private static readonly IReadOnlyDictionary<string, MineRailGradePath> ByShape = All.ToDictionary(p => p.Shape);
    public static bool TryFind(string shape, out MineRailGradePath path) => ByShape.TryGetValue(shape, out path!);
    public static MineRailGradePath Find(string shape) => ByShape[shape];
    private static MineRailGradePath[] Build()
    {
        var result = new List<MineRailGradePath>();
        void Add(string shape, string name, string description, Func<float, float> height, Func<float, float> slope, string? next, int nextHeight = 0)
        {
            var path = CoasterPath.Section("MineRail" + shape, t =>
            {
                var tangent = Vector3.Normalize(new Vector3(0, slope(t), 1));
                var up = Vector3.Normalize(Vector3.UnitY - tangent * Vector3.Dot(Vector3.UnitY, tangent));
                return (new Vector3(0, .15f + height(t), t - .5f), up, tangent);
            }, false);
            result.Add(new(shape, name, description, next, nextHeight, path));
        }
        Add("Grade45", "45 Degree Slope", "Rises one block for each block forward. Reverse traversal descends. Use entry and exit transitions at flat track.", t => t, _ => 1, "Grade45", 1);
        Add("GradeEntry1", "45 Degree Entry 1/2", "First cell of the smooth transition from level track into a 45 degree mine slope.", t => t*t/4, t => t/2, "GradeEntry2");
        Add("GradeEntry2", "45 Degree Entry 2/2", "Second entry cell. Continue with the 45 degree slope one block higher.", t => .25f+t/2+t*t/4, t => .5f+t/2, "Grade45", 1);
        Add("GradeExit1", "45 Degree Exit 1/2", "First cell easing a 45 degree slope onto level track.", t => t-t*t/4, t => 1-t/2, "GradeExit2");
        Add("GradeExit2", "45 Degree Exit 2/2", "Second exit cell. Continue with level track one block higher.", t => .75f+t/2-t*t/4, t => .5f-t/2, "Straight", 1);
        Add("GradeCrest1", "45 Degree Crest 1/2", "First cell of a smooth hilltop joining a 45 degree ascent to a 45 degree descent.", t => t-t*t/2, t => 1-t, "GradeCrest2");
        Add("GradeCrest2", "45 Degree Crest 2/2", "Second hilltop cell. The next descending slope is rotated 180 degrees and placed one block lower.", t => .5f-t*t/2, t => -t, null);
        Add("GradeDip1", "45 Degree Dip 1/2", "First cell of a smooth valley joining a 45 degree descent to a 45 degree ascent.", t => 1-t+t*t/2, t => -1+t, "GradeDip2");
        Add("GradeDip2", "45 Degree Dip 2/2", "Second valley cell. Continue with an ascending 45 degree slope one block higher.", t => .5f+t*t/2, t => t, "Grade45", 1);
        return result.ToArray();
    }
}
