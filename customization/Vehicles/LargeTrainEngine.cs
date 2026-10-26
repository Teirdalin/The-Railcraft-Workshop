// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeTrainEngineSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "LargeTrainEngine";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 7000;
        public const double CargoCapacityKg = 1000;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 8;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 1500000;
        public const double TractionNewtons = 900000;
        public const double BrakingNewtons = 900000;
        public const double IntendedTrainMassKg = 100000;
        public const double DurabilityHours = 200;
    }
}

