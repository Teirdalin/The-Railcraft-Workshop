// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class WoodenMinecartSettings
    {
        // Original artwork ships in this release; newer vehicle models are deferred.
        public const bool NewDesign = false;
        public const string VehicleKey = "WoodenMinecart";
        public const int CarriedItemWeightGrams = 10000;
        public const double EmptyMassKg = 80;
        public const double CargoCapacityKg = 400;
        // Automatically unload cargo when crossing a Dumping Rail.
        public const bool AllowDumping = true;
        public const int StorageSlots = 8;
        public const double MaximumSpeedMetresPerSecond = 12;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 1300;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 40;
    }
}
