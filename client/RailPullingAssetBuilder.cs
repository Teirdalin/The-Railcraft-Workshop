using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Configure Eco's native walking-cart controller, not a custom client driver.
    public static class RailPullingAssetBuilder
    {
        public static void Configure(GameObject root, float emptyKg, float cargoKg)
        {
            var drive = root.GetComponent<RCCCarControllerV2>();
            drive.footPoweredCart = true;
            // RCC mode 2 distributes the existing total torque over all four
            // wheels. Front-only drive loses grip as the cart pitches uphill.
            drive._wheelTypeChoise = 2;
            drive.engineTorque = 3000f;
            drive.maxspeed = 5.4f; // km/h: 1.5 m/s walking pace.
            drive.autoGenerateGearCurves = false;
            drive.autoGenerateTargetSpeedsForChangingGear = false;
            drive.totalGears = 1;
            drive.currentGear = 0;
            drive.gearSpeed = new[] { 5.4f };
            drive.engineTorqueCurve = new[] { AnimationCurve.Linear(0, 1, 5.4f, 0) };
            var limit = root.GetComponent<LimitVelocity>() ?? root.AddComponent<LimitVelocity>();
            limit.MaxVelocity = 1.5f;
            limit.MaxAccel = 2f;
            limit.ignoreY = true; // Preserve gravity and vertical ramp motion.
            // Automobile slip/stability interventions can remove the walking
            // input or brake an individual wheel on narrow rail transitions.
            drive.TCS = false;
            drive.ESP = false;
            RailWheelSuspension.Configure(root, emptyKg, emptyKg + cargoKg + 160f);
            foreach (var wheel in root.GetComponentsInChildren<WheelCollider>(true))
            {
                // Damping sized for maximum cargo launched an empty cart.
                // Bound the damping impulse by EMPTY supported mass at the
                // native 50 Hz physics step; retain springs for rated payload.
                var suspension = wheel.suspensionSpring;
                suspension.damper = .5f * emptyKg / (4 * .02f);
                wheel.suspensionSpring = suspension;
                var friction = wheel.forwardFriction;
                friction.stiffness = 3f;
                wheel.forwardFriction = friction;
            }
            var occupied = root.GetComponentInChildren<MountSpotPulled>(true).occupiedCollider as BoxCollider;
            if (occupied == null) throw new InvalidOperationException("Missing native pulling collider: " + root.name);
            // The collider moves with the cart, not with the avatar's feet.
            // Keep torso collision while clearing the rising deck ahead of the
            // leading axle. This does not change the avatar or hand IK anchors.
            occupied.transform.localPosition = new Vector3(0, 1.20f, 1.15f);
            occupied.center = Vector3.zero;
            occupied.size = new Vector3(.5f, 1.10f, .5f);
            // MountSpotPulled enables this collider only when someone grabs the
            // handle. Native player/vehicle collision exclusions still need it
            // in the list while it is disabled in the authored prefab.
            root.GetComponent<Vehicle>().AllVehicleColliders = root.GetComponentsInChildren<Collider>(true);
        }

        public static void RefreshAndBuild()
        {
            RefreshBase();
            foreach (var spec in RailExpansionAssetBuilder.ReadCatalog().Vehicles.Where(s => s.Pullable))
                Refresh(spec.Key, spec.EmptyKg, spec.CargoKg);
            AssetDatabase.SaveAssets();
            Debug.Log("ECO_NATIVE_PULLING_ASSETS_OK: native collider list includes occupied puller collider; four-wheel drive, rated-load suspension, torso clearance.");
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECO_PULLING_SUSPENSION_REPORT")))
                PullingSuspensionProbe.Run();
        }
        public static void RefreshBase() => Refresh("Minecart", 280f, 2500f);
        private static void Refresh(string key, float emptyKg, float cargoKg)
        {
            var path = "Assets/EcoMinecarts/Prefabs/" + key + "Object.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("Missing pulling prefab: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Configure(root, emptyKg, cargoKg); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
