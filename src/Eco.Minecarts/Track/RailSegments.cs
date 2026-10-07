using System.Numerics;

namespace Eco.Minecarts.Track;

public readonly record struct RailPose(
    Vector3 Position,
    Vector3 Tangent,
    double Grade,
    double Curvature,
    double CantRadians = 0);

public interface IRailSegment
{
    string Id { get; }
    double Length { get; }
    RailPose Sample(double distance);
}

public sealed class StraightRailSegment : IRailSegment
{
    private readonly Vector3 start;
    private readonly Vector3 tangent;

    public StraightRailSegment(string id, Vector3 start, Vector3 end)
    {
        this.Id = id;
        this.start = start;
        var delta = end - start;
        this.Length = delta.Length();
        if (this.Length <= 0) throw new ArgumentException("A rail segment must have length.", nameof(end));
        this.tangent = Vector3.Normalize(delta);
    }

    public string Id { get; }
    public double Length { get; }

    public RailPose Sample(double distance)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Sample");
        var s = Math.Clamp(distance, 0, this.Length);
        return new RailPose(
            this.start + this.tangent * (float)s,
            this.tangent,
            this.tangent.Y,
            0);
    }
}

/// <summary>A level circular arc around the +Y axis. Positive sweep turns left.</summary>
public sealed class ArcRailSegment : IRailSegment
{
    private readonly Vector3 center;
    private readonly double radius;
    private readonly double startRadians;
    private readonly double sweepRadians;

    public ArcRailSegment(
        string id,
        Vector3 center,
        double radius,
        double startRadians,
        double sweepRadians)
    {
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (sweepRadians == 0) throw new ArgumentOutOfRangeException(nameof(sweepRadians));
        this.Id = id;
        this.center = center;
        this.radius = radius;
        this.startRadians = startRadians;
        this.sweepRadians = sweepRadians;
        this.Length = radius * Math.Abs(sweepRadians);
    }

    public string Id { get; }
    public double Length { get; }

    public RailPose Sample(double distance)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Sample");
        var fraction = Math.Clamp(distance / this.Length, 0, 1);
        var angle = this.startRadians + this.sweepRadians * fraction;
        var sign = Math.Sign(this.sweepRadians);
        var position = this.center + new Vector3(
            (float)(Math.Cos(angle) * this.radius),
            0,
            (float)(Math.Sin(angle) * this.radius));
        var tangent = Vector3.Normalize(new Vector3(
            (float)(-Math.Sin(angle) * sign),
            0,
            (float)(Math.Cos(angle) * sign)));
        return new RailPose(position, tangent, 0, 1 / this.radius);
    }
}

public sealed class RailPath
{
    private readonly IReadOnlyList<IRailSegment> segments;

    public RailPath(IEnumerable<IRailSegment> segments)
    {
        this.segments = segments.ToArray();
        if (this.segments.Count == 0) throw new ArgumentException("A rail path needs at least one segment.", nameof(segments));
        this.Length = this.segments.Sum(segment => segment.Length);
    }

    public double Length { get; }

    /// <summary>Resolves distance while preserving overshoot across any number of segment boundaries.</summary>
    public (IRailSegment Segment, double LocalDistance, RailPose Pose) Sample(double pathDistance)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Sample");
        var remaining = Math.Clamp(pathDistance, 0, this.Length);
        foreach (var segment in this.segments)
        {
            if (remaining <= segment.Length)
                return (segment, remaining, segment.Sample(remaining));
            remaining -= segment.Length;
        }

        var last = this.segments[^1];
        return (last, last.Length, last.Sample(last.Length));
    }
}
