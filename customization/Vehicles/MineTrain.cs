// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class MineTrainSettings
    {
        // Original artwork ships in this release; newer vehicle models are deferred.
        public const bool NewDesign = false;
        public const string VehicleKey = "MineTrain";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 600;
        public const double CargoCapacityKg = 250;
        // Automatically unload cargo when crossing a Dumping Rail.
        public const bool AllowDumping = false;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 12000;
        public const double TractionNewtons = 5500;
        public const double BrakingNewtons = 6000;
        public const double IntendedTrainMassKg = 8000;
        public const double DurabilityHours = 200;
    }
}
