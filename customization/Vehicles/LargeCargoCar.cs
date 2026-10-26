// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeCargoCarSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "LargeCargoCar";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 5000;
        public const double CargoCapacityKg = 20000;
        public const bool AllowDumping = true;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 48;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 8000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}

