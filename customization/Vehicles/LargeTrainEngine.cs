// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeTrainEngineSettings
    {
        public const string VehicleKey = "LargeTrainEngine";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 7000;
        public const double CargoCapacityKg = 1000;
        public const int StorageSlots = 8;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 250000;
        public const double TractionNewtons = 70000;
        public const double BrakingNewtons = 40000;
        public const double IntendedTrainMassKg = 100000;
        public const double DurabilityHours = 200;
    }
}
