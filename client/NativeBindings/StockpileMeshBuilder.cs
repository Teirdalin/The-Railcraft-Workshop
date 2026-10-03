using UnityEngine;

// Serialized authoring surface of Eco 0.14.1.1's existing native Eco.Client
// component. The game supplies Start/subscriptions/mesh generation; this is
// not a shipped custom runtime or an inventory simulation.
public class StockpileMeshBuilder : SubscribableBehavior
{
    public Vector3Int contentDimensions;
}
