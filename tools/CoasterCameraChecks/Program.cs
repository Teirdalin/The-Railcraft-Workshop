using System.Numerics;
using Eco.Railcraft.CoasterCamera.Client;
static float Angle(Quaternion a,Quaternion b)=>2*MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a,b)),0,1));
static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
foreach(var reverse in new[]{1,-1})foreach(var yaw in new[]{0f,.8f}){
    var baseFrame=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
    var look=Quaternion.CreateFromYawPitchRoll(.2f,-.1f,0);
    var transport=new CoasterCameraFrame();transport.Enter(baseFrame,baseFrame*look);
    var previous=baseFrame*look;
    for(int i=1;i<=720;i++){
        var frame=baseFrame*Quaternion.CreateFromAxisAngle(Vector3.UnitX,reverse*i*MathF.PI/180);
        // Network quaternions can change sign without changing orientation.
        if(i%17==0)frame=new(-frame.X,-frame.Y,-frame.Z,-frame.W);
        var actual=transport.Step(frame,1f/60);
        Check(float.IsFinite(actual.W)&&Math.Abs(actual.LengthSquared()-1)<.00001,"Non-finite or invalid camera frame");
        Check(Angle(previous,actual)<.05,"Loop camera flipped at the vertical/inverted seam");previous=actual;
        Check(Angle(transport.Desired(frame),Quaternion.Normalize(frame*look))<.001,"Camera lost the seat-relative view");
    }
    for(int i=0;i<90;i++)previous=transport.Step(baseFrame,1f/60);
    Check(Angle(previous,baseFrame*look)<.001,"Loop exit did not preserve the chosen view");
    var inverted=baseFrame*Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI);
    for(int i=0;i<120;i++)previous=transport.Step(inverted,1f/60);
    Check(Vector3.Dot(Vector3.Transform(Vector3.UnitY,previous),-Vector3.UnitY)>.98,"Camera never became upside down at the top of the loop");
    transport.Enter(baseFrame,look);Check(Angle(transport.Step(baseFrame,0),look)<.001,"Re-entry retained an old ride frame");
}
// Mouse look changes in a stable seat frame even while the body is inverted.
// Changing look must not orbit the eye, and moving the cart must carry it.
var centre=new Vector3(12,30,45);var eye=new Vector3(.25f,1.4f,-.3f);
var invertedFrame=Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI);
var freeLook=new CoasterCameraFrame();freeLook.Enter(invertedFrame,invertedFrame);
var changedLook=Quaternion.CreateFromYawPitchRoll(.7f,-.3f,0);
freeLook.SetLook(changedLook);
Check(Angle(freeLook.Step(invertedFrame,0),Quaternion.Normalize(invertedFrame*changedLook))<.001,"Inverted mouse look was ignored or delayed");
foreach(var angle in new[]{0f,MathF.PI/2,MathF.PI,3*MathF.PI/2,2*MathF.PI}){
    var frame=Quaternion.CreateFromAxisAngle(Vector3.UnitX,angle);
    var position=CoasterCameraFrame.EyePosition(centre,frame,eye);
    Check(Vector3.Distance(Vector3.Transform(position-centre,Quaternion.Inverse(frame)),eye)<.00001,"Loop eye drifted from its seat-relative anchor");
    var moved=CoasterCameraFrame.EyePosition(centre+new Vector3(3,-2,1),frame,eye);
    Check(Vector3.Distance(moved-position,new Vector3(3,-2,1))<.00001,"Camera position lagged the moving cart");
    freeLook.SetLook(Quaternion.CreateFromYawPitchRoll(-.5f,.4f,0));
    Check(Vector3.Distance(position,CoasterCameraFrame.EyePosition(centre,frame,eye))<.00001,"Mouse look moved the eye around the cart");
}
Console.WriteLine("COASTER_CAMERA_FRAME_OK: full loops in both directions, inverted free look, immediate accepted input, stable seat-local eye position and moving cart translation; native runtime input and ride feel still require live acceptance.");
foreach(var seat in new[]{new Vector3(-.34f,.3602f,-.15125f),new Vector3(.34f,.3602f,-.15125f),new Vector3(-.25f,.55f,-.1f)}){
    var anchored=CoasterCameraFrame.MountEye(seat);
    Check(Math.Abs(anchored.X-seat.X)<.00001f&&anchored.Z<seat.Z&&anchored.Z>seat.Z-.15f,"Ride eye is ahead of its occupied seat");
    Check(anchored.Y>seat.Y+1&&anchored.Y<seat.Y+1.3f,"Ride eye lost seated head height");
    foreach(var angle in new[]{0f,MathF.PI/2,MathF.PI,3*MathF.PI/2}){
        var frame=Quaternion.CreateFromAxisAngle(Vector3.UnitX,angle);
        var world=CoasterCameraFrame.EyePosition(centre,frame,anchored);
        Check(Vector3.Distance(Vector3.Transform(world-centre,Quaternion.Inverse(frame)),anchored)<.00001f,"Occupied seat eye drifted through inversion");
    }
}
Console.WriteLine("COASTER_CAMERA_SEAT_ANCHOR_OK: both occupied seats and original design stay behind the seat datum through full inversion without capturing a transient native bumper position.");

var once = new CoasterCameraFrame(); once.Enter(Quaternion.Identity,Quaternion.Identity);
var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX,.8f);
var first=once.StepOnce(turn,1f/60,10);
for(int i=0;i<8;i++)Check(Angle(first,once.StepOnce(turn,1f/60,10))<.001,"Multiple callbacks advanced camera smoothing in one frame");
once.SetLook(changedLook);
Check(Angle(once.StepOnce(turn,1f/60,10),Quaternion.Normalize(first*changedLook))<.001,"Input between callbacks was delayed");
Check(Angle(once.StepOnce(turn,1f/60,11),Quaternion.Normalize(first*changedLook))>.001,"Next frame did not advance body smoothing");
Console.WriteLine("COASTER_CAMERA_OWNERSHIP_OK: one body smoothing step per rendered frame, immediate input between camera callbacks; native final application intercepted while mounted.");

foreach(var sign in new[]{1,-1}){
 var frame=Quaternion.CreateFromYawPitchRoll(.7f,.4f,.2f);var start=new CoasterCameraFrame();start.EnterForward(frame,sign);
 Check(Vector3.Distance(Vector3.Transform(Vector3.UnitZ,start.Desired(frame)),Vector3.Transform(Vector3.UnitZ*sign,frame))<.00001,"Default camera inherited native downward pitch or wrong travel direction");
 var input=Quaternion.CreateFromYawPitchRoll(.6f,-.2f,0);start.SetLook(input);
 foreach(var pitch in new[]{.4f,1.6f,3.2f,5f,6.3f}){var own=Quaternion.CreateFromYawPitchRoll(.7f,pitch,.2f);Check(Angle(start.Desired(own),Quaternion.Normalize(own*input))<.001,"Ride geometry reset accepted mouse look");}
}
Console.WriteLine("COASTER_CAMERA_FORWARD_INPUT_OK: forward/reverse entry ignores native downward pitch; accepted seat-local look persists through inversion and independent car frames.");
