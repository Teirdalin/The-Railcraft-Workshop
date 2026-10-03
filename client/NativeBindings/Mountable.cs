using UnityEngine;

// Serialization-only authoring mirror of Eco 0.14.1.1's existing Eco.Client type.
// The game supplies all runtime mounting/network behavior, not this file.
[DefaultExecutionOrder(int.MaxValue)]
public class Mountable : SubscribableBehavior
{
    public MountSpot[] seats = new MountSpot[0];
    public bool handleDismount = true;
    public bool blockDismountWhenNoExit = true;
    public bool freezeWhenUnmounted = false;
}
