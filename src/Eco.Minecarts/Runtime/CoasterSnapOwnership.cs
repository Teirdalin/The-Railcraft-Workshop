using Eco.Gameplay.Auth;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Math;
using Eco.Shared.Voxel;
using Eco.World.Blocks;

namespace Eco.Minecarts.Runtime;

// Simple terrain blocks can be shared instances. A cell lease, revoked by the
// native destruction callback, distinguishes our helper from a later replacement.
internal sealed class CoasterSnapOwnership(CoasterRailObject owner)
{
    private sealed record Lease(CoasterSnapOwnership Owner,WrappedWorldPosition3i Cell,Type Type,int End);
    private static readonly object Gate=new();
    private static readonly Dictionary<WrappedWorldPosition3i,Lease> Owned=new();
    private readonly List<Lease> ends=new(2);
    private bool attached,destroyed;
    private User? movingOwner;

    private void Flag(int end,bool value)
    {
        if((end==0?owner.EntrySnapOwned:owner.ExitSnapOwned)==value)return;
        if(end==0)owner.EntrySnapOwned=value;else owner.ExitSnapOwned=value;
        owner.SetDirty();
    }
    internal void Attach(bool create=false,bool checkAccess=false)
    {
        lock(Gate)
        {
            if(destroyed||owner.IsDestroyed||attached||owner is CoasterStationObject)return;
            attached=true;
            var saved=new[]{owner.EntrySnapOwned,owner.ExitSnapOwned};
            Flag(0,false);Flag(1,false);
            var index=0;
            foreach(var (position,type) in CoasterSnapHelpers.Ends(owner.PathKey,owner.Position,owner.Rotation))
            {
                var end=index++;
                if(!WrappedWorldPosition3i.TryCreate(position,out var cell)||Owned.ContainsKey(cell))continue;
                if(create)
                {
                    if(Eco.World.World.GetBlock(cell) is not EmptyBlock)continue;
                    if(checkAccess)
                    {
                        // Native move tools authorize the body. Helpers lie outside
                        // that footprint; never extend them onto another owner's plot.
                        var user=movingOwner;
                        if(user==null||!ServiceHolder<IAuthManager>.Obj.IsAuthorized(((Vector3i)cell).XZ.ToPlotPos(),user,AccessType.FullAccess,null,out _))continue;
                    }
                    Eco.World.World.SetBlock(type,cell);
                }
                else if(!saved[end])continue;
                // Loading adopts only the exact saved orientation. Missing helpers
                // and blocks placed by players are never repaired automatically.
                if(Eco.World.World.GetBlock(cell)?.GetType()!=type)continue;
                var lease=new Lease(this,cell,type,end);
                Owned.Add(cell,lease);ends.Add(lease);Flag(end,true);
            }
        }
    }
    internal void Detach(bool destroying=false)
    {
        lock(Gate)
        {
            // Moving across a deed boundary can change Owners before the after
            // callback. Authorization must use the source owner, not the recipient.
            if(!destroying)movingOwner=owner.Owners?.FirstUser()??owner.Creator;
            if(destroying)destroyed=true;
            foreach(var lease in ends.ToArray())
            {
                if(!Owned.TryGetValue(lease.Cell,out var current)||!ReferenceEquals(lease,current))continue;
                Owned.Remove(lease.Cell);
                if(Eco.World.World.GetBlock(lease.Cell)?.GetType()==lease.Type)
                    Eco.World.World.DeleteBlock(lease.Cell);
            }
            ends.Clear();attached=false;Flag(0,false);Flag(1,false);
        }
    }
    internal static void Replaced(Vector3i position)
    {
        if(!WrappedWorldPosition3i.TryCreate(position,out var cell))return;
        lock(Gate)
        {
            if(!Owned.Remove(cell,out var lease))return;
            lease.Owner.ends.Remove(lease);lease.Owner.Flag(lease.End,false);
        }
    }
}
