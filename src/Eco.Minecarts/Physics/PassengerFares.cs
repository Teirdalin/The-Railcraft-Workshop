using Eco.Shared.Serialization;

namespace Eco.Minecarts.Physics;

[Serialized]
public sealed class PassengerTicket
{
    [Serialized] public string ServiceId { get; set; } = "";
    [Serialized] public int UserId { get; set; }
    [Serialized] public long ExpiresUtcTicks { get; set; }
    // Empty means every eligible passenger car of this service, not a seat.
    [Serialized, ThreadSafe] public List<int> CarIds { get; set; } = new();
    [Serialized, ThreadSafe] public List<Guid> CarObjects { get; set; } = new();
    public PassengerTicket Copy() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/Copy"); return new() { ServiceId=ServiceId,UserId=UserId,ExpiresUtcTicks=ExpiresUtcTicks,CarIds=CarIds.ToList(),CarObjects=CarObjects.ToList() }; }
}

public static class PassengerFareRules
{
    // Production boarding uses persistent object identities, never recycled handles.
    public static bool HasTicket(IEnumerable<PassengerTicket> tickets, string serviceId, int userId, Guid carId, long nowUtcTicks) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/HasTicket"); return tickets.Any(t=>t.ServiceId==serviceId && t.UserId==userId && t.ExpiresUtcTicks>nowUtcTicks
            && (t.CarObjects.Count>0?t.CarObjects.Contains(carId):t.CarIds.Count==0)); }
    public static bool SafeToEndRide(float speed) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/SafeToEndRide"); return float.IsFinite(speed) && Math.Abs(speed)<=.1f; }
    public static bool HasTicket(IEnumerable<PassengerTicket> tickets, string serviceId, int userId, int carId, long nowUtcTicks) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/HasTicket"); return tickets.Any(t => t.CarObjects.Count==0 && t.ServiceId==serviceId && t.UserId==userId && t.ExpiresUtcTicks>nowUtcTicks && (t.CarIds.Count==0 || t.CarIds.Contains(carId))); }
    public static bool TryDuration(string text, out float minutes)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/TryDuration");
        minutes=0; text=text.Trim().ToLowerInvariant();
        var multiplier=text.EndsWith('d') ? 1440f : text.EndsWith('h') ? 60f : 1f;
        if(text.EndsWith('d') || text.EndsWith('h') || text.EndsWith('m')) text=text[..^1].Trim();
        if(!float.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)) return false;
        minutes=value*multiplier;
        return float.IsFinite(minutes) && minutes>=1 && minutes<=525600;
    }
    public static bool ValidPrice(float price, bool itemPayment) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fare rules/ValidPrice"); return float.IsFinite(price) && price>=.01f && price<=1000000
        && (!itemPayment || (price>=1 && price==MathF.Truncate(price))); }
}
