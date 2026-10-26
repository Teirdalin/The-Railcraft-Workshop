// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class MinecartSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "Minecart";
        public const int CarriedItemWeightGrams = 9000;
        public const double EmptyMassKg = 280;
        public const double CargoCapacityKg = 5000;
        // Rock, ore, crushed rock and crushed ore stacks; 1 disables the bonus.
        public const int BulkCargoStackMultiplier = 5;
        public const bool AllowDumping = true;
        // Comma-separated item tags; empty allows all cargo.
        public const string CargoFilterTags = "";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = false;
        public const int StorageSlots = 12;
        public const double MaximumSpeedMetresPerSecond = 18;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 4800;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}

