// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class RailroadHandcarSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "RailroadHandcar";
        public const int CarriedItemWeightGrams = 10000;
        public const double EmptyMassKg = 180;
        public const double CargoCapacityKg = 100;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 8;
        public const double PowerWatts = 450;
        public const double TractionNewtons = 700;
        public const double BrakingNewtons = 2500;
        public const double IntendedTrainMassKg = 900;
        public const double DurabilityHours = 200;
    }
}

