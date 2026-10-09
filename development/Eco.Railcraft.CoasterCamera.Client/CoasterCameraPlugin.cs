using BepInEx;
using BepInEx.Unity.IL2CPP;
using Eco.CameraControl;
using Eco.CameraControl.Internal;
using HarmonyLib;
using UnityEngine;

namespace Eco.Railcraft.CoasterCamera.Client;

/// <summary>
/// Railworks local-view correction. The server remains authoritative over
/// the coaster pose; this never writes a vehicle, player, or rail transform.
/// </summary>
[BepInPlugin("railworks.coaster-camera", "Railworks Workshop Coaster Camera", "0.2.67")]
public sealed class CoasterCameraPlugin : BasePlugin
{
    private Harmony? harmony;
    private readonly CoasterView view = new();
    private bool failed;
    private bool enabled = true;

    public override void Load()
    {
        var lateUpdate = AccessTools.Method(typeof(PlayerCameraController), "LateUpdate")
            ?? throw new MissingMethodException("Eco camera LateUpdate was not found; this game version is unsupported.");
        var afterUpdate = AccessTools.Method(typeof(CoasterCameraPlugin), nameof(BeforeCameraLateUpdate))
            ?? throw new MissingMethodException(nameof(BeforeCameraLateUpdate));
        harmony = new Harmony("railworks.coaster-camera");
        harmony.Patch(lateUpdate, prefix: new HarmonyMethod(afterUpdate),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CoasterCameraPlugin),nameof(AfterNativeCameraRender))));
        var render=AccessTools.Method(typeof(PlayerCameraController), "UpdateCamera")
            ?? throw new MissingMethodException("Eco camera UpdateCamera was not found.");
        harmony.Patch(render, prefix: new HarmonyMethod(AccessTools.Method(typeof(CoasterCameraPlugin), nameof(BeforeCameraRender))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CoasterCameraPlugin), nameof(AfterNativeCameraRender))));
        var input=AccessTools.Method(typeof(PlayerCameraBehaviorFirstPerson),nameof(PlayerCameraBehaviorFirstPerson.UpdateCameraInput))
            ?? throw new MissingMethodException("Eco first-person input was not found.");
        harmony.Patch(input,
            prefix:new HarmonyMethod(AccessTools.Method(typeof(CoasterCameraPlugin),nameof(BeforeCameraInput))),
            postfix:new HarmonyMethod(AccessTools.Method(typeof(CoasterCameraPlugin),nameof(AfterCameraInput))));
        Active = this;
        Log.LogInfo("RAILWORKS_COASTER_CAMERA_READY: first-person coaster seats only; Shift+F9 toggles the camera.");
    }

    public override bool Unload()
    {
        Active = null;
        harmony?.UnpatchSelf();
        view.Reset();
        return true;
    }

    private static CoasterCameraPlugin? Active;

    private static void BeforeCameraInput(PlayerCameraBehaviorFirstPerson __instance,out bool __state)
    {
        __state=false;
        var plugin=Active;if(plugin==null || plugin.failed || !plugin.enabled)return;
        try{__state=plugin.view.PrepareInput(__instance);}
        catch(Exception error){plugin.Fail(error);}
    }
    private static void AfterCameraInput(PlayerCameraBehaviorFirstPerson __instance,bool __state)
    {
        var plugin=Active;if(!__state || plugin==null || plugin.failed)return;
        try{plugin.view.AcceptInput(CoasterNativeLook.Read(__instance));}
        catch(Exception error){plugin.Fail(error);}
    }
    private void Fail(Exception error)
    {
        failed=true;view.Reset();
        Log.LogError("Coaster camera disabled after an error; native Eco camera continues unchanged: "+error);
    }

    private static void BeforeCameraRender(PlayerCameraController __instance)
    {
        var plugin=Active;
        if(plugin==null || plugin.failed || !plugin.enabled)return;
        try { plugin.view.PrepareRender(__instance); }
        catch(Exception error) { plugin.view.Release(__instance); plugin.Fail(error); }
    }

    private static void AfterNativeCameraRender(PlayerCameraController __instance)
    {
        var plugin=Active;
        if(plugin==null || plugin.failed || !plugin.enabled)return;
        try { plugin.view.CaptureNative(__instance); plugin.view.Apply(__instance); }
        catch(Exception error) { plugin.Fail(error); }
    }

    private static void BeforeCameraLateUpdate(PlayerCameraController __instance)
    {
        var plugin = Active;
        if (plugin == null || plugin.failed) return;
        try
        {
            if (Input.GetKeyDown(KeyCode.F9) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                plugin.enabled = !plugin.enabled;
                plugin.view.Release(__instance);
                plugin.Log.LogInfo("Coaster camera " + (plugin.enabled ? "enabled" : "disabled") + ".");
            }

        }
        catch (Exception error)
        {
            plugin.Fail(error);
        }
    }
}

internal sealed class CoasterView
{
    private int mountId = -1;
    private bool active;
    private bool capturePending;
    private readonly CoasterCameraFrame transport = new();
    private Quaternion output = Quaternion.identity;
    private Vector3 localEye;

    internal void Reset()
    {
        mountId = -1;
        active = false;
        capturePending = false;
    }

    internal bool PrepareInput(PlayerCameraBehaviorFirstPerson behavior)
    {
        var player=Player.obj;var camera=player==null?null:player.CameraController;
        if(!active || player==null || player.Mount==null || player.Mount.GetInstanceID()!=mountId
            || camera==null || camera.CurrentCameraMode!=PlayerCameraController.CameraMode.FirstPerson
            || camera.CurrentCameraBehavior == null || camera.CurrentCameraBehavior.Pointer != behavior.Pointer)return false;
        // Let Eco apply its own sensitivity, pitch limits and accepted input to
        // a level, seat-local look. The coaster's inversion never enters these
        // Euler angles; only the final transported view uses the body frame.
        CoasterNativeLook.Seed(behavior,Unity(transport.LocalLook));
        return true;
    }
    internal void AcceptInput(Quaternion look)=>transport.SetLook(Numeric(look));

    internal void PrepareRender(PlayerCameraController camera)
    {
        var player=Player.obj;var mount=player==null?null:player.Mount;
        var owner=mount==null?null:mount.GetComponentInParent<Mountable>();
        if(player!=null && player.CameraController!=camera)return;
        if(player==null || mount==null || owner==null
            || !owner.name.StartsWith("RollerCoasterCartObject",StringComparison.Ordinal)
            || camera.CurrentCameraMode!=PlayerCameraController.CameraMode.FirstPerson)
        { Release(camera);return; }
        if(mountId!=mount.GetInstanceID())
        { mountId=mount.GetInstanceID();active=false;capturePending=true; }
    }

    internal bool Apply(PlayerCameraController camera)
    {
        var player = Player.obj;
        if (player == null) { Reset(); return false; }
        if (player.CameraController != camera) return false;
        var mount = player?.Mount;
        var owner = mount == null ? null : mount.GetComponentInParent<Mountable>();
        if (mount == null || owner == null
            || !owner.name.StartsWith("RollerCoasterCartObject", StringComparison.Ordinal)
            || camera.CurrentCameraMode != PlayerCameraController.CameraMode.FirstPerson
            || camera.CameraTransform == null)
        {
            Release(camera);
            return false;
        }

        var frame = owner.transform.rotation;
        var id = mount.GetInstanceID();
        if (mountId != id)
        {
            mountId = id; active = false; capturePending = true;
            // Let Eco establish the mounted input state before taking ownership.
            return false;
        }
        if(capturePending)return false;

        // Eco runs its normal input and camera pipeline. Apply the transported
        // final pose after every native UpdateCamera call, before rendering;
        // never skip the method that delivers mouse input.
        active = true;

        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, .1f);
        output = Unity(transport.StepOnce(Numeric(frame),dt,Time.frameCount));
        var eye=CoasterCameraFrame.EyePosition(Numeric(owner.transform.position),Numeric(frame),Numeric(localEye));
        camera.CameraTransform.position=new Vector3(eye.X,eye.Y,eye.Z);
        camera.CameraTransform.rotation = output;

        return true;
    }
    internal void CaptureNative(PlayerCameraController camera)
    {
        if(!capturePending)return;
        var player=Player.obj; var mount=player==null?null:player.Mount;
        var owner=mount==null?null:mount.GetComponentInParent<Mountable>();
        if(player==null || player.CameraController!=camera || mount==null || owner==null
            || mount.GetInstanceID()!=mountId || camera.CurrentCameraMode!=PlayerCameraController.CameraMode.FirstPerson
            || camera.CameraTransform==null){Reset();return;}
        var frame=owner.transform.rotation;
        var body=owner.GetComponent<Rigidbody>();
        var reverse=body!=null && Vector3.Dot(body.velocity,owner.transform.forward)<-.1f;
        transport.EnterForward(Numeric(frame),reverse?-1:1); output=Unity(transport.Desired(Numeric(frame)));
        var firstPerson=camera.CurrentCameraBehavior?.TryCast<PlayerCameraBehaviorFirstPerson>();
        if(firstPerson!=null)
            CoasterNativeLook.Seed(firstPerson,Unity(transport.LocalLook));
        // A native camera transition can still be ahead of the bumper on
        // entry. The occupied seat, not that transient camera, owns the eye.
        localEye=UnityPosition(CoasterCameraFrame.MountEye(Numeric(owner.transform.InverseTransformPoint(mount.transform.position))));
        capturePending=false; active=true;
    }
    internal void Release(PlayerCameraController? camera)
    {
        if(active && camera?.CurrentCameraBehavior != null)
            camera.CurrentCameraBehavior.SetCameraRotation(output.eulerAngles);
        Reset();
    }
    private static System.Numerics.Quaternion Numeric(Quaternion q)=>new(q.x,q.y,q.z,q.w);
    private static System.Numerics.Vector3 Numeric(Vector3 v)=>new(v.x,v.y,v.z);
    private static Quaternion Unity(System.Numerics.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    private static Vector3 UnityPosition(System.Numerics.Vector3 p)=>new(p.X,p.Y,p.Z);
}
