using System.Numerics;

namespace Eco.Minecarts.Track;

public enum RailPieceKind
{
    Straight,
    CurveLeft90R3,
    Slope1x4,
    BufferStop,
}

public readonly record struct GridPoint(int X, int Y, int Z)
{
    public static GridPoint operator +(GridPoint left, GridPoint right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}

public readonly record struct RailEndpoint(GridPoint Position, Vector2 OutwardPlanarDirection);

/// <summary>
/// A placed track module. QuarterTurns rotates clockwise around +Y in 90 degree steps.
/// Slope pieces always rise from socket A to B; traversing them in reverse descends.
/// </summary>
public sealed record PlacedRailPiece(
    string Id,
    RailPieceKind Kind,
    GridPoint Origin,
    int QuarterTurns = 0)
{
    public IReadOnlyList<RailEndpoint> Endpoints
    {
        get
        {
            RailEndpoint[] local = this.Kind switch
            {
                RailPieceKind.Straight =>
                [Endpoint(0, 0, 0, 0, -1), Endpoint(0, 0, 1, 0, 1)],
                RailPieceKind.CurveLeft90R3 =>
                [Endpoint(0, 0, 0, 0, -1), Endpoint(3, 0, 3, 1, 0)],
                RailPieceKind.Slope1x4 =>
                [Endpoint(0, 0, 0, 0, -1), Endpoint(0, 1, 4, 0, 1)],
                RailPieceKind.BufferStop =>
                [Endpoint(0, 0, 0, 0, -1)],
                _ => throw new ArgumentOutOfRangeException(),
            };

            return local.Select(this.Transform).ToArray();
        }
    }

    private static RailEndpoint Endpoint(int x, int y, int z, float dx, float dz) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Endpoint"); return new(new GridPoint(x, y, z), Vector2.Normalize(new Vector2(dx, dz))); }

    private RailEndpoint Transform(RailEndpoint endpoint)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Transform");
        var turns = ((this.QuarterTurns % 4) + 4) % 4;
        var point = endpoint.Position;
        var direction = endpoint.OutwardPlanarDirection;
        for (var i = 0; i < turns; i++)
        {
            point = new GridPoint(point.Z, point.Y, -point.X);
            direction = new Vector2(direction.Y, -direction.X);
        }

        return endpoint with { Position = this.Origin + point, OutwardPlanarDirection = direction };
    }
}

public readonly record struct TrackConnection(
    string PieceA,
    int EndpointA,
    string PieceB,
    int EndpointB,
    GridPoint Position);

/// <summary>
/// Builds exact socket-to-socket connectivity for placed modules. It deliberately
/// ignores vertical tangent discontinuity at the first-generation slope sockets;
/// the cart pose interpolator owns that small transition.
/// </summary>
public sealed class TrackGraph
{
    private const float OpposedDirectionTolerance = -0.999f;
    private readonly Dictionary<(string Piece, int Endpoint), TrackConnection> connections;

    private TrackGraph(
        IReadOnlyDictionary<string, PlacedRailPiece> pieces,
        Dictionary<(string Piece, int Endpoint), TrackConnection> connections)
    {
        this.Pieces = pieces;
        this.connections = connections;
    }

    public IReadOnlyDictionary<string, PlacedRailPiece> Pieces { get; }
    public IEnumerable<TrackConnection> Connections => this.connections.Values.Distinct();

    public static TrackGraph Build(IEnumerable<PlacedRailPiece> source)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Build");
        var pieces = source.ToDictionary(piece => piece.Id, StringComparer.Ordinal);
        var sockets = pieces.Values
            .SelectMany(piece => piece.Endpoints.Select((endpoint, index) => (piece.Id, Index: index, Endpoint: endpoint)))
            .GroupBy(socket => socket.Endpoint.Position);
        var connections = new Dictionary<(string Piece, int Endpoint), TrackConnection>();

        foreach (var socketGroup in sockets)
        {
            var candidates = socketGroup.ToArray();
            for (var i = 0; i < candidates.Length; i++)
            for (var j = i + 1; j < candidates.Length; j++)
            {
                var a = candidates[i];
                var b = candidates[j];
                if (Vector2.Dot(a.Endpoint.OutwardPlanarDirection, b.Endpoint.OutwardPlanarDirection) > OpposedDirectionTolerance)
                    continue;

                var aKey = (a.Id, a.Index);
                var bKey = (b.Id, b.Index);
                if (connections.ContainsKey(aKey) || connections.ContainsKey(bKey))
                    throw new InvalidOperationException($"Ambiguous rail junction at {socketGroup.Key}. Turnouts require an explicit switch piece.");

                var connection = new TrackConnection(a.Id, a.Index, b.Id, b.Index, socketGroup.Key);
                connections.Add(aKey, connection);
                connections.Add(bKey, connection);
            }
        }

        return new TrackGraph(pieces, connections);
    }

    public bool TryGetNeighbor(string pieceId, int endpointIndex, out (string PieceId, int EndpointIndex) neighbor)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/TryGetNeighbor");
        if (!this.connections.TryGetValue((pieceId, endpointIndex), out var connection))
        {
            neighbor = default;
            return false;
        }

        neighbor = connection.PieceA == pieceId && connection.EndpointA == endpointIndex
            ? (connection.PieceB, connection.EndpointB)
            : (connection.PieceA, connection.EndpointA);
        return true;
    }
}
