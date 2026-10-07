namespace Eco.Minecarts.Runtime;

// Runtime-only state: saves continue to use the existing vehicle fields.
internal sealed class RailMotionSleepClock
{
    internal const int ActiveIntervalMs = 50;
    internal const int SleepingIntervalMs = 250;
    private DateTime? quietSince;
    private DateTime nextProbe;
    internal bool IsSleeping { get; private set; }

    // Lift tracks keep polling so their mechanical-load registration and any
    // change of power remain current. Stations keep their dispatch clock.
    internal static bool RailCanSleep(bool handbrake, bool chain, bool station, float grade) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Idle maintenance/RailCanSleep"); return !chain && !station && (handbrake || Math.Abs(grade) < .0001f); }

    internal bool Observe(bool eligible, DateTime now)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Idle maintenance/Observe");
        if (!eligible) { this.Wake(); return false; }
        this.quietSince ??= now;
        if (!this.IsSleeping && now - this.quietSince.Value >= TimeSpan.FromSeconds(1))
        {
            this.IsSleeping = true;
            this.nextProbe = now.AddSeconds(1);
        }
        return this.IsSleeping;
    }

    internal bool NeedsProbe(DateTime now)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Idle maintenance/NeedsProbe");
        if (!this.IsSleeping) return true;
        if (now < this.nextProbe) return false;
        this.nextProbe = now.AddSeconds(1);
        return true;
    }

    internal bool Wake()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Idle maintenance/Wake");
        var wasSleeping = this.IsSleeping;
        this.IsSleeping = false;
        this.quietSince = null;
        return wasSleeping;
    }
}
