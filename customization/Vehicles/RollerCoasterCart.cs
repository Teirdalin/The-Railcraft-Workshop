// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class RollerCoasterCartSettings
    {
        // Original artwork ships in this release; newer vehicle models are deferred.
        public const bool NewDesign = false;
        public const string VehicleKey = "RollerCoasterCart";
        public const int CarriedItemWeightGrams = 12000;
        public const double EmptyMassKg = 300;
        public const double CargoCapacityKg = 0;
        // Automatically unload cargo when crossing a Dumping Rail.
        public const bool AllowDumping = false;
        public const int StorageSlots = 1;
        public const double MaximumSpeedMetresPerSecond = 40;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 7000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}
