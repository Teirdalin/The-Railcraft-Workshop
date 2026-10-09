# Railworks Workshop

This is the server package. Joining players need no local installation to use
the Railworks vehicles, tracks, models, animations, or server gameplay.

Stop the server and back up its existing mod files. Copy `Railworks.dll` and
`Railworks.unity3d` together into the server's `Mods/UserCode` folder. On a fresh
installation, also copy the `Vehicles` folder there. On an update, preserve
existing vehicle settings and add any missing vehicle files.

Vehicle settings are editable C# files in `Vehicles`. This release ships the
original vehicle artwork only. NewDesign remains for settings compatibility;
the newer vehicle models are preserved outside the public bundle for future
updates. Keep your existing capacity, weight, speed, and other custom values.

The improved coaster loop camera is distributed separately in the optional
Railworks Workshop BepInEx Camera ZIP. Each player who wants it must install the
matching BepInEx IL2CPP loader and camera plugin on their own Eco client.
Joining the server does not install the improved camera. Players without the
plugin use Eco's normal camera. Keep client plugins out of server `Mods/UserCode`.
