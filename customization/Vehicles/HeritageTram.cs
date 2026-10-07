// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class HeritageTramSettings
    {
        public const string VehicleKey = "HeritageTram";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 1250;
        public const double CargoCapacityKg = 100;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 20;
        public const double PowerWatts = 9000;
        public const double TractionNewtons = 7000;
        public const double BrakingNewtons = 9000;
        public const double IntendedTrainMassKg = 2500;
        public const double DurabilityHours = 200;
    }
}
