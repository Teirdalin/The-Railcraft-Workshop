using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Rail Curve")]
public sealed class RailTurnComponent : WorldObjectComponent
{
    private readonly object gate=new();
    private RailCell? registeredCell;
    private bool destroyed;

    public override void PostInitialize()
    {
        base.PostInitialize();
        Register();
    }
    private void Register()
    {
        lock(gate)
            if(!destroyed&&!Parent.IsDestroyed&&registeredCell==null)
                registeredCell=RailInfrastructure.Register(Parent);
    }
    private void Unregister(bool destroying=false)
    {
        RailCell? removed=null;
        lock(gate)
        {
            if(destroying)destroyed=true;
            if(registeredCell is {} cell&&RailInfrastructure.Remove(Parent,cell))removed=cell;
            registeredCell=null;
        }
        // Notify after removal and outside our gate: power callbacks may read
        // the topology and enter their own connection/drive locks.
        if(removed is {} oldCell)RailPowerConnectionComponent.RailRemoved(oldCell);
    }
    public override void OnBeforeObjectMoved()
    {
        base.OnBeforeObjectMoved();
        Unregister();
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        Register();
    }
    public override void Destroy()
    {
        Unregister(destroying:true);
        base.Destroy();
    }
}
