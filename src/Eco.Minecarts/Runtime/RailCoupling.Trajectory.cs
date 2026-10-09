using System.Numerics;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class RailCouplingComponent
{
    internal Vector3? GroundedCouplerDirection()
    {
        foreach(var end in new[]{-1,1})
            if(Partner(end) is {} grounded && grounded.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell!=null)
            {
                var toward=grounded.Connector(PartnerEnd(end))-Parent.Position;
                if(toward.LengthSquared()>.0001f)return Vector3.Normalize(toward)*end;
            }
        return null;
    }
    private RailMotionTrail? motionTrail;
    private int trailTravelSign;
    internal void RecordMotion(VoxelRail? rail, float parameter, Vector3 point, Vector3 velocity)
    {
        if (!Vehicle.RailSpec.Coaster || !Linked || IsFollower)
        { motionTrail=null;trailTravelSign=0;return; }
        motionTrail ??= new();
        var travelSign=Vector3.Dot(Parent.Rotation.RotateVector(Vector3.UnitZ),velocity)<0?-1:1;
        if(rail!=null && velocity.LengthSquared()>.01f){
            if(trailTravelSign!=0 && trailTravelSign!=travelSign)motionTrail.Clear();
            trailTravelSign=travelSign;
        }
        if (motionTrail.Empty && rail is { } guided)
        {
            var motion = Parent.GetComponent<MinecartMotionComponent>();
            var forward = Parent.Rotation.RotateVector(Vector3.UnitZ);
            var sign = Vector3.Dot(velocity.LengthSquared() > .001f ? velocity : forward, guided.Profile.Tangent(parameter)) < 0 ? -1 : 1;
            var seeded = new List<RailMotionTrail.Pose>();
            var length = Math.Min(60, Group().Sum(c => c.Vehicle.RailSpec.Length + .10f) + 2);
            for (var behind = .10f; behind <= length; behind += .10f)
            {
                var cursor = RailPathCursor.Travel(guided, parameter, -sign * behind, motion.NextRail);
                if (cursor.Remaining != 0) break;
                seeded.Add(new(cursor.Rail.Point(cursor.Progress), cursor.Rail.Profile.Tangent(cursor.Progress) * sign * cursor.Orientation * Math.Max(.001f,velocity.Length()),
                    cursor.Rail.Profile.Up(cursor.Progress), cursor.Rail, cursor.Progress));
            }
            for (var i = seeded.Count - 1; i >= 0; i--) motionTrail.Add(seeded[i]);
        }
        motionTrail.Add(new(point, velocity, rail?.Profile.Up(parameter) ?? Parent.Rotation.RotateVector(Vector3.UnitY), rail, parameter));
    }

    private void FollowMotionTrajectory(Vector3 velocity)
    {
        var sign=velocity.LengthSquared()>.000001f? (Vector3.Dot(Parent.Rotation.RotateVector(Vector3.UnitZ),velocity)<0?-1:1)
            : trailTravelSign==0?1:trailTravelSign;
        var seen=new HashSet<int>{Parent.ID};
        void Visit(RailCouplingComponent parent,float offset,int bodySign)
        {
            foreach(var end in new[]{-1,1})
            {
                var child=parent.Partner(end);if(child==null || !seen.Add(child.Parent.ID))continue;
                var childEnd=parent.PartnerEnd(end);
                var nextOffset=offset+end*bodySign*(parent.Vehicle.CouplerOffset+child.Vehicle.CouplerOffset+.10f);
                var childSign=bodySign*-end*childEnd;
                if(!FollowTrajectory(parent,child,end,nextOffset,childSign,childEnd,velocity,out var correctedOffset))
                    FollowFreeBranch(parent,child,end,childEnd,velocity,parent.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell!=null);
                Visit(child,correctedOffset,childSign);
            }
        }
        Visit(this,0,sign);
    }

    private bool FollowTrajectory(RailCouplingComponent parent,RailCouplingComponent child,int parentEnd,float offset,
        int bodyTravelSign,int childEnd,Vector3 velocity,out float correctedOffset)
    {
        correctedOffset=offset;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        var childMotion=child.Parent.GetComponent<MinecartMotionComponent>();
        bool Sample(float at,out RailMotionTrail.Pose value)
            {
            value=default;
            return at<=0 ? motionTrail!=null && motionTrail.Behind(-at,motion.NextRail,out value)
                : PredictAhead(at,velocity,out value);
        }
        RailMotionTrail.Pose pose;
        if(!Sample(offset,out pose))
            pose=new(FreeFollowerPosition(parent,child,parentEnd,childEnd),velocity,child.Parent.Rotation.RotateVector(Vector3.UnitY),null,0);
        // Arc spacing and body-centre spacing diverge across a flight/rail
        // boundary, especially while one car settles on its own running gear.
        // Solve along that same spatial path using both actual contact heights.
        // Carry the corrected cursor into the next drawbar; never copy pitch.
        var spacing=parent.Vehicle.CouplerOffset+child.Vehicle.CouplerOffset+.10f;
        for(var iteration=0;pose.Rail!=null && !childMotion.AwaitingCoupledLanding && iteration<4;iteration++)
        {
            var centre=childMotion.TrajectoryContactPoint(pose,bodyTravelSign);
            var delta=centre-parent.Parent.Position;
            var length=delta.Length();
            var error=spacing-length;
            if(Math.Abs(error)<.005f || length<.001f)break;
            var direction=pose.Velocity.LengthSquared()>.000001f?Vector3.Normalize(pose.Velocity):Vector3.UnitZ;
            var derivative=Vector3.Dot(delta/length,direction);
            if(Math.Abs(derivative)<.2f)break;
            var trial=correctedOffset+Math.Clamp(error/derivative,-.35f,.35f);
            if(Math.Sign(trial)!=Math.Sign(offset) || Math.Abs(trial-offset)>.7f || !Sample(trial,out var next))break;
            correctedOffset=trial;pose=next;
        }
        // A path sample supplies this car's travel, not a shared contact flag.
        // Keep airborne drawbars attached while rotating each child around its
        // own connected end, then test its actual running gear against track.
        if(pose.Rail==null || childMotion.AwaitingCoupledLanding || childMotion.BoundRailCell==null)
        {
            var constrained=FreeFollowerPosition(parent,child,parentEnd,childEnd);
            var localVelocity=pose.Velocity;
            if(childMotion.AwaitingCoupledLanding)
            {
                // Once held by a landing car, actual movement of this car's
                // connected end replaces its stale pre-impact velocity. A
                // frozen downward vector suspends its running gear forever.
                var displacement=constrained-child.Parent.Position;
                if(displacement.LengthSquared()>.000001f && Vector3.Dot(displacement,velocity)>.000001f)
                    localVelocity=Vector3.Normalize(displacement)*Math.Max(.001f,velocity.Length());
                else if(childMotion.CurrentRailVelocity.LengthSquared()>.000001f)
                    localVelocity=childMotion.CurrentRailVelocity;
            }
            var own=child.Parent.Rotation;
            var rotation=new Quaternion(own.x,own.y,own.z,own.w);
            var parentRotation=parent.Parent.Rotation;
            var supportedAhead=offset>0 && (parent.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell!=null
                || TrackWorld.CaptureContact(parent.Parent.Position,new(parentRotation.x,parentRotation.y,parentRotation.z,parentRotation.w),velocity,
                    parent.Vehicle.RailSpec.Wheelbase/2,parent.Vehicle.ContactHalfSize.X)!=null);
            var contact=TrackWorld.CaptureContact(constrained,rotation,localVelocity,
                child.Vehicle.RailSpec.Wheelbase/2,child.Vehicle.ContactHalfSize.X,supportedAhead?child.Vehicle.RailSpec.Length:.45f);
            if(contact is {} hit)
            {
                var toRail=hit.Rail.Point(hit.T)-constrained;
                var up=hit.Rail.Profile.Up(hit.T);
                var plane=toRail-up*Vector3.Dot(toRail,up);
                // A departing endpoint behind the car is not another landing.
                if((hit.T<.001f || hit.T>.999f) && Vector3.Dot(plane,localVelocity)<-.0001f)contact=null;
            }
            if(contact is {} touched)
            {
                var tangent=touched.Rail.Profile.Tangent(touched.T);
                var direction=Vector3.Dot(localVelocity,tangent)<0?-1:1;
                pose=new(touched.Rail.Point(touched.T),tangent*direction*Math.Max(.001f,velocity.Length()),
                    touched.Rail.Profile.Up(touched.T),touched.Rail,touched.T);
            }
            else pose=new(constrained,localVelocity,Vector3.Transform(Vector3.UnitY,rotation),null,0);
        }
        var supportForward=Vector3.Zero;
        if(pose.Rail==null){
            if(child.Partner(-childEnd) is {} supported && supported.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell!=null){
                // The opposite grounded drawbar exerts a pitch constraint on
                // this suspended body. Use its OWN two connection points, not
                // the controlling car's rotation, and retain the angular limit.
                var head=parent.Connector(parentEnd)+parent.Parent.Rotation.RotateVector(Vector3.UnitZ)*parentEnd*.10f;
                var rear=supported.Connector(child.PartnerEnd(-childEnd))
                    +child.Parent.Rotation.RotateVector(Vector3.UnitZ)*childEnd*.10f;
                if(Vector3.DistanceSquared(head,rear)>.0001f)supportForward=Vector3.Normalize(head-rear)*childEnd;
            }
            else if(parent.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell!=null && velocity.LengthSquared()<.000001f
                && TrackWorld.Capture(child.Parent.Position,child.Parent.Rotation.RotateVector(Vector3.UnitZ),.65f,2f,coaster:true) is {} below){
                supportForward=below.Rail.Profile.Tangent(below.T);
                if(Vector3.Dot(supportForward,child.Parent.Rotation.RotateVector(Vector3.UnitZ))<0)supportForward=-supportForward;
            }
        }
        var travelDirection=pose.Rail is {} guided && Vector3.Dot(pose.Velocity,guided.Profile.Tangent(pose.Parameter))<0?-1:1;
        if(pose.Velocity.LengthSquared()>.00000001f)
            pose=pose with {Velocity=Vector3.Normalize(pose.Velocity)*velocity.Length()};
        childMotion.AcceptTrajectoryFollowerPose(Parent.ID,pose,bodyTravelSign,childEnd,travelDirection,true,supportForward);
        return true;
    }

    private bool PredictAhead(float metres, Vector3 velocity, out RailMotionTrail.Pose pose)
    {
        pose = default;
        if (motionTrail == null || !motionTrail.Behind(0, Parent.GetComponent<MinecartMotionComponent>().NextRail, out var start)
            || velocity.LengthSquared() < .0025f && start.Rail==null) return false;
        var motion = Parent.GetComponent<MinecartMotionComponent>();
        var point = start.Point;
        VoxelRail? departed = null;
        if (start.Rail is { } rail)
        {
            var direction=velocity.LengthSquared()>.000001f?velocity:start.Velocity;
            var sign = Vector3.Dot(direction, rail.Profile.Tangent(start.Parameter)) < 0 ? -1 : 1;
            var cursor = RailPathCursor.Travel(rail, start.Parameter, metres * sign, motion.NextRail);
            if (cursor.Remaining == 0)
            {
                pose = new(cursor.Rail.Point(cursor.Progress), cursor.Rail.Profile.Tangent(cursor.Progress) * sign * cursor.Orientation * Math.Max(.001f,velocity.Length()),
                    cursor.Rail.Profile.Up(cursor.Progress), cursor.Rail, cursor.Progress);
                return true;
            }
            metres = (float)Math.Abs(cursor.Remaining);
            point = cursor.Rail.Point(cursor.Progress); departed = cursor.Rail;
            velocity = cursor.Rail.Profile.Tangent(cursor.Progress) * sign * cursor.Orientation * velocity.Length();
        }
        var rotation = new Quaternion(Parent.Rotation.x, Parent.Rotation.y, Parent.Rotation.z, Parent.Rotation.w);
        var bodySign = Vector3.Dot(Parent.Rotation.RotateVector(Vector3.UnitZ), velocity) < 0 ? -1 : 1;
        for (var step = 0; step < 1200 && metres > .00001f; step++)
        {
            var seconds = Math.Min(.02, Math.Min(.05f, metres) / Math.Max(1, velocity.Length()));
            var next = AirMotion.Step(point, velocity, seconds);
            var travel = Vector3.Distance(point, next.Position);
            if (travel < .000001f) return false;
            var fraction = Math.Min(1, metres / travel);
            point = Vector3.Lerp(point, next.Position, fraction);
            velocity = Vector3.Lerp(velocity, next.Velocity, fraction);
            metres = Math.Max(0, metres - travel);
            rotation = AirMotion.FollowTrajectory(rotation, velocity, bodySign, seconds * fraction);
            if (velocity.Y <= 0 && TrackWorld.CaptureContact(point, rotation, velocity, Vehicle.RailSpec.Wheelbase / 2, Vehicle.ContactHalfSize.X) is { } hit
                && hit.Rail.Cell != departed?.Cell)
            {
                var sign = Vector3.Dot(velocity, hit.Rail.Profile.Tangent(hit.T)) < 0 ? -1 : 1;
                var cursor = RailPathCursor.Travel(hit.Rail, hit.T, metres * sign, motion.NextRail);
                if (cursor.Remaining != 0) return false;
                pose = new(cursor.Rail.Point(cursor.Progress), cursor.Rail.Profile.Tangent(cursor.Progress) * sign * cursor.Orientation * Math.Max(.001f,velocity.Length()),
                    cursor.Rail.Profile.Up(cursor.Progress), cursor.Rail, cursor.Progress);
                return true;
            }
        }
        if (metres > .00001f) return false;
        pose = new(point, velocity, Vector3.Transform(Vector3.UnitY, rotation), null, 0);
        return true;
    }
}
