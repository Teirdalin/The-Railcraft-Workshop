// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class MineTrainSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "MineTrain";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 600;
        public const double CargoCapacityKg = 250;
        public const bool AllowDumping = false;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 210000;
        public const double TractionNewtons = 240000;
        public const double BrakingNewtons = 240000;
        public const double IntendedTrainMassKg = 12000;
        public const double DurabilityHours = 200;
    }
}

