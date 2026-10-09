using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Authoring only. Use the same seated hip datum as the existing cab fit
    // check; this is geometric validation, not a substitute for live avatar QA.
    public static class RailRiderFit
    {
        public const float SeatedHipHeight = .64f;
        // Raise the avatar attachment, leaving the bench and dismount deck in place.
        public const float RiderLift = .10f;
        // Tram passengers were still visibly sunk into the bench. Keep this
        // separate from train and coaster seats, and leave standing spots alone.
        public const float TramRiderLift = .20f;
        public static float Lift(GameObject root) => root.name == "RollerCoasterCartObject" ? 0
            : root.name == "HeritageTramObject" ? TramRiderLift : RiderLift;
        public const float SlatSurfaceOffset = .015f;
        public const float CoasterCushionCentre = .91f;
        public const float CoasterCushionThickness = .13f;

        static Transform Bench(GameObject root, MountSpot seat) => root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "Passenger bench")
            .OrderBy(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(seat.transform.position.x, seat.transform.position.z)))
            .FirstOrDefault();
        static float Surface(GameObject root, MountSpot seat)
        {
            if (root.name == "RollerCoasterCartObject") return CoasterCushionCentre + CoasterCushionThickness / 2;
            var bench = Bench(root, seat);
            if (bench == null) throw new Exception(root.name + ": passenger seat has no bench");
            return root.transform.InverseTransformPoint(bench.position).y + bench.localScale.y / 2 + SlatSurfaceOffset;
        }
        public static void Apply(GameObject root)
        {
            var mounts = root.GetComponent<Mountable>();
            if (mounts == null || !RailVehicleDetail.Handles(root)) return;
            var cab = root.transform.Find("CabFittings");
            if (cab != null)
            {
                var cushion = cab.Find("Operator cushion");
                var seat = mounts.seats[1].transform;
                var point = seat.position;
                point.y = cushion.GetComponent<Renderer>().bounds.max.y - SeatedHipHeight + RiderLift;
                seat.position = point;
                var floor = cab.Find("Cab floor");
                var floorY = floor.localPosition.y + floor.localScale.y / 2;
                var roof = cab.Find("Cab roof");
                var desired = floorY + 2.12f + RiderLift;
                var lift = Mathf.Max(0, desired - roof.localPosition.y);
                roof.localPosition += Vector3.up * lift;
                foreach (var post in cab.Cast<Transform>().Where(t => t.name == "Cab window post"))
                {
                    post.localPosition += Vector3.up * (lift / 2);
                    post.localScale += Vector3.up * lift;
                }
                var vehicle = root.GetComponent<Vehicle>();
                var size = vehicle.size; size.y = Mathf.Max(size.y, floorY + 2.3f + RiderLift); vehicle.size = size;
            }
            foreach (var seat in mounts.seats.Skip(1).Where(s => (int)s.overrideAvatarState == 3 && s.name.StartsWith("PassengerSeat")))
            {
                var p = seat.transform.localPosition;
                p.y = Surface(root, seat) - SeatedHipHeight + Lift(root);
                seat.transform.localPosition = p;
                // The tram benches face outwards from the central boarding area.
                seat.transform.localRotation = Quaternion.Euler(0, root.name == "HeritageTramObject" && p.z < 0 ? 180 : 0, 0);
            }
            if (root.name == "PassengerCarObject" || root.name == "LargePassengerCarObject")
            {
                // Eco retains a tall eye origin even in its sitting animation.
                // Keep the existing two-metre mount-to-roof clearance after
                // bringing the hips onto the finished slats. The curved roof
                // has a 45 mm underside and 140 mm crown above its origin.
                var roof = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Coach roof");
                var highestSeat = mounts.seats.Skip(1).Max(s => s.transform.localPosition.y);
                var desired = highestSeat + 2f + .045f;
                var lift = Mathf.Max(0, desired - roof.localPosition.y);
                roof.localPosition += Vector3.up * lift;
                foreach (var post in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Window pillar"))
                {
                    post.localPosition += Vector3.up * (lift / 2);
                    post.localScale += Vector3.up * lift;
                }
                var vehicle = root.GetComponent<Vehicle>();
                var size = vehicle.size;
                size.y = Mathf.Max(size.y, desired + .15f);
                vehicle.size = size;
            }
        }
        public static int Verify(GameObject root)
        {
            if(root.transform.Find("PreparedVehicleArt")!=null){RailPreparedVehicleFit.Verify(root);return root.GetComponent<Mountable>().seats.Length-1;}
            if (!RailVehicleDetail.Handles(root)) return 0;
            var mounts = root.GetComponent<Mountable>();
            if (root.transform.Find("CabFittings") != null) StandingCabAssetBuilder.Verify(root);
            var checkedSeats = 0;
            foreach (var seat in mounts.seats.Skip(1))
            {
                if (!seat.setAsParent || seat.exitPosition == null || seat.alternativeExitPosition == null)
                    throw new Exception(root.name + ": rider attachment or exit missing");
                if ((int)seat.overrideAvatarState != 3 || !seat.name.StartsWith("PassengerSeat")) continue;
                var p = seat.transform.localPosition;
                if (Mathf.Abs(p.y + SeatedHipHeight - Lift(root) - Surface(root, seat)) > .002f)
                    throw new Exception(root.name + ": seated attachment does not match requested lift " + seat.name);
                if (root.name == "HeritageTramObject")
                {
                    var roof = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Canopy roof");
                    if (roof.GetComponent<Renderer>().bounds.min.y - seat.transform.position.y < 1.85f)
                        throw new Exception(root.name + ": raised tram passenger lacks canopy clearance " + seat.name);
                }
                var direction = root.name == "HeritageTramObject" && p.z < 0 ? Vector3.back : Vector3.forward;
                if (Vector3.Dot(seat.transform.localRotation * Vector3.forward, direction) < .999f)
                    throw new Exception(root.name + ": passenger faces into backrest " + seat.name);
                if (root.name == "PassengerCarObject" || root.name == "LargePassengerCarObject")
                {
                    var ceiling = root.transform.Find("VehicleDetail/Roof").GetComponentsInChildren<Renderer>(true).Min(r => r.bounds.min.y);
                    if (ceiling - seat.transform.position.y < 2f - .002f)
                        throw new Exception(root.name + ": finished curved roof lacks seated camera clearance");
                }
                checkedSeats++;
            }
            return checkedSeats;
        }
    }
}
