// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class PassengerLocomotiveSettings
    {
        public const string VehicleKey = "PassengerLocomotive";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 1100;
        public const double CargoCapacityKg = 150;
        public const int StorageSlots = 4;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 38000;
        public const double TractionNewtons = 5000;
        public const double BrakingNewtons = 8500;
        public const double IntendedTrainMassKg = 5000;
        public const double DurabilityHours = 200;
    }
}
