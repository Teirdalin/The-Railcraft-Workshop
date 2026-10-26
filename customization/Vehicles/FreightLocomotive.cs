// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class FreightLocomotiveSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "FreightLocomotive";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 2200;
        public const double CargoCapacityKg = 400;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 6;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 600000;
        public const double TractionNewtons = 450000;
        public const double BrakingNewtons = 450000;
        public const double IntendedTrainMassKg = 50000;
        public const double DurabilityHours = 200;
    }
}

