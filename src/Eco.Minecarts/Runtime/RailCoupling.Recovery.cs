namespace Eco.Minecarts.Runtime;

public sealed partial class RailCouplingComponent
{
    internal static bool WithRecoveryTopology(Func<bool> action)
    {
        if(!Monitor.TryEnter(LinkGate))return false;
        try{return action();}finally{Monitor.Exit(LinkGate);}
    }
    internal void ResetRecoveryTrail(){motionTrail=null;trailTravelSign=0;}
    internal RailCouplingComponent[] RecoveryOrder()
    {
        var members=Group();
        var first=members.Where(c=>c.EndAvailable(1)||c.RearAvailable)
            .OrderBy(c=>c.Parent.GetComponent<MinecartMotionComponent>().CoasterDepartureOrder).ThenBy(c=>c.Parent.ID).FirstOrDefault();
        if(first==null)return [];
        var result=new List<RailCouplingComponent>();RailCouplingComponent? previous=null;
        for(var next=first;next!=null&&result.Count<members.Length;)
        {
            if(result.Contains(next))return [];
            result.Add(next);
            var following=new[]{next.Partner(-1),next.Partner(1)}.FirstOrDefault(c=>c!=null&&c!=previous);
            previous=next;next=following;
        }
        return result.Count==members.Length?result.ToArray():[];
    }
}
