// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class HeritageTramSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "HeritageTram";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 1250;
        public const double CargoCapacityKg = 100;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 20;
        public const double PowerWatts = 9000;
        public const double TractionNewtons = 7000;
        public const double BrakingNewtons = 9000;
        public const double IntendedTrainMassKg = 2500;
        public const double DurabilityHours = 200;
    }
}

