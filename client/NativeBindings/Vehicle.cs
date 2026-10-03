using UnityEngine;
using TMPro;

// VehicleBase's serialized surface is flattened here; native Vehicle inherits it.
public class Vehicle : WorldObject
{
    public string vehicleControlHintsTemplate = "", UIName = "VehicleToolbar";
    public UnityEngine.Object VehicleUI;
    public bool hasLights, hasHonk;
    [SerializeField] private string wheelsGameobjectLayer = "IgnoreInteraction";
    public bool ignoreMoveThroughCosts, syncWheelPos;
    public TextMeshPro[] ExtraLicensePlates = new TextMeshPro[0];
    public TextMeshPro LicensePlate;
    public GameObject frontWheels;
    public Collider[] AllVehicleColliders = new Collider[0];
}
