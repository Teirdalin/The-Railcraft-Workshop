// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargePassengerCarSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "LargePassengerCar";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 4500;
        public const double CargoCapacityKg = 500;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 8;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 6500;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}

