// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class RailroadHandcarSettings
    {
        // Original artwork ships in this release; newer vehicle models are deferred.
        public const bool NewDesign = false;
        public const string VehicleKey = "RailroadHandcar";
        public const int CarriedItemWeightGrams = 10000;
        public const double EmptyMassKg = 180;
        public const double CargoCapacityKg = 100;
        // Automatically unload cargo when crossing a Dumping Rail.
        public const bool AllowDumping = false;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 8;
        public const double PowerWatts = 450;
        public const double TractionNewtons = 700;
        public const double BrakingNewtons = 2500;
        public const double IntendedTrainMassKg = 900;
        public const double DurabilityHours = 200;
    }
}
