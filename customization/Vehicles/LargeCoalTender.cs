// Edit values and restart Eco. Keep names and VehicleKey unchanged.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeCoalTenderSettings
    {
        public const bool NewDesign = false;
        public const string VehicleKey = "LargeCoalTender";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 3500;
        public const double CargoCapacityKg = 12000;
        public const bool AllowDumping = false;
        // Additional native item tags; the tender always requires burnable fuel.
        public const string CargoFilterTags = "Burnable Fuel";
        // Feed compatible fuel from cargo to coupled locomotives.
        public const bool SuppliesLocomotiveFuel = true;
        public const int StorageSlots = 32;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 6000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}

