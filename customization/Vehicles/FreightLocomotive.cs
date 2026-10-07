// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class FreightLocomotiveSettings
    {
        public const string VehicleKey = "FreightLocomotive";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 2200;
        public const double CargoCapacityKg = 400;
        public const int StorageSlots = 6;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 75000;
        public const double TractionNewtons = 18000;
        public const double BrakingNewtons = 13000;
        public const double IntendedTrainMassKg = 25000;
        public const double DurabilityHours = 200;
    }
}
