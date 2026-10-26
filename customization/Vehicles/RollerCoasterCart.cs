// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class RollerCoasterCartSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "RollerCoasterCart";
        public const int CarriedItemWeightGrams = 12000;
        public const double EmptyMassKg = 300;
        public const double CargoCapacityKg = 0;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 1;
        public const double MaximumSpeedMetresPerSecond = 40;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 7000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}


