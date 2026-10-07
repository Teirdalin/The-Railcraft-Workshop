// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargePassengerCarSettings
    {
        public const string VehicleKey = "LargePassengerCar";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 4500;
        public const double CargoCapacityKg = 500;
        public const int StorageSlots = 8;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 6500;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}
