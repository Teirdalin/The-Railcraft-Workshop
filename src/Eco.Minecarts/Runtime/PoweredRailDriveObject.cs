using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

// Native Eco grid registration and footprint cleanup are identical for cable
// and chain drives. Demand/traction policies stay in their concrete definitions.
internal static class PoweredRailDrive
{
    internal static void Initialize(WorldObject drive,bool electrical)
    {
        drive.GetComponent<PowerConsumptionComponent>().Initialize(0);
        if(electrical)drive.GetComponent<PowerGridComponent>().Initialize(10,new ElectricPower());
        else drive.GetComponent<PowerGridComponent>().Initialize(5,new MechanicalPower());
    }
    internal static void RemoveFootprint(IEnumerable<Eco.Shared.Math.Vector3i>? occupied,Guid id)
    {
        if(occupied!=null)foreach(var cell in occupied)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block&&block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
    internal static bool ReadyExceptPower(PowerGridComponent grid)=>!grid.Parent.IsDestroyed &&
        grid.Parent.Components.All(c=>c==grid || c.Enabled && (c is not IOperatingWorldObjectComponent op || op.Operating));
}

internal sealed class PoweredRailRegistry<T> where T:WorldObject
{
    private readonly object gate=new();
    private readonly Dictionary<int,T> drives=[];
    private T[] ordered=[];
    internal T[] Snapshot=>Volatile.Read(ref ordered);
    internal IEnumerable<T> Values=>Snapshot;
    internal void Register(T drive)
    {lock(gate){drives[drive.ID]=drive;Volatile.Write(ref ordered,drives.Values.OrderBy(d=>d.ID).ToArray());}}
    internal void Remove(T drive)
    {lock(gate){drives.Remove(drive.ID);Volatile.Write(ref ordered,drives.Values.OrderBy(d=>d.ID).ToArray());}}
}

// Called under a drive's gate. Expiration is bounded and independent of the
// number of physics substeps; one consist receives one power share per drive.
internal sealed class PoweredRailContacts(double seconds)
{
    private readonly Dictionary<int,DateTime> contacts=[];
    private readonly List<int> stale=[];
    private DateTime nextExpiry;
    internal int Count=>contacts.Count;
    internal int Touch(int id,DateTime now)
    {Expire(now);contacts[id]=now;return contacts.Count;}
    internal void Expire(DateTime now)
    {
        if(now<nextExpiry)return;
        nextExpiry=now.AddMilliseconds(250);
        stale.Clear();
        foreach(var pair in contacts)if((now-pair.Value).TotalSeconds>seconds)stale.Add(pair.Key);
        foreach(var id in stale)contacts.Remove(id);
    }
}
