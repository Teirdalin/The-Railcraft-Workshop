# Railworks Workshop coaster camera

This is the optional BepInEx camera package for individual players. It is
separate from the server mod. Joining a Railworks server does not install it.
The ZIP contains the camera plugin, not the BepInEx loader or Eco game assemblies.

Install the matching BepInEx 6 IL2CPP loader for your Eco client first (the tested
Windows x64 loader is BepInEx bleeding-edge build 788). Close Eco and extract this
ZIP into the Eco client folder containing `Eco.exe`. The DLL belongs at
`BepInEx/plugins/Eco.Railcraft.CoasterCamera.Client.dll`.
Keep this DLL out of server `Mods/UserCode` and the Unity asset bundle.
To remove the camera, close Eco and remove that plugin DLL. Other BepInEx
plugins and the Railworks server mod can remain installed.

Its code runs on each riding client; joining players can use all Railworks
vehicles without installing the camera component, using Eco's normal camera.
It alters only the local first-person view while mounted in a
`RollerCoasterCartObject`. During steep pitch and inversion it carries the
view in the replicated cart body's quaternion frame, smoothing the frame to
reduce network-pose jitter. Eco's normal first-person input applies sensitivity
and pitch limits to a level seat-local look, so you can look around during
inversions without sending the loop angle through native pitch clamping.
The eye is anchored over the occupied mount using a seated head-height datum,
slightly behind the seat's hip position. It follows the current cart directly,
independently of view direction, without capturing a transient native camera
position during mounting. The plugin starts looking forward in the individual car
frame. Eco runs its original UpdateCamera and input processing on every callback;
the plugin applies the transported final view afterward, before rendering.
Body smoothing advances once per rendered frame even when Eco makes multiple
camera callbacks. Native camera control resumes on dismount, view
change, or when the plugin is disabled. It does not modify rail physics, avatar position,
vehicle transforms, third-person view, other vehicles, or server code.

Press **Shift+F9** to toggle the coaster camera. Camera switching, dismounting,
free look, and other vehicles retain native controls. Local installation is
required for every player using the improved camera.
