// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeCargoCarSettings
    {
        public const string VehicleKey = "LargeCargoCar";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 5000;
        public const double CargoCapacityKg = 20000;
        public const int StorageSlots = 48;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 8000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}
