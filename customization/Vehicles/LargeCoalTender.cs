// Railworks Workshop: edit values, save, and restart Eco. No build tools needed.
// Keep this filename, namespace, class name and VehicleKey unchanged.
// Weights in inventory are grams; vehicle/cargo physics masses are kilograms.
namespace RailworksWorkshop.VehicleSettings
{
    public static class LargeCoalTenderSettings
    {
        // False: original artwork. True: newer artwork.
        public const bool NewDesign = false;
        public const string VehicleKey = "LargeCoalTender";
        public const int CarriedItemWeightGrams = 15000;
        public const double EmptyMassKg = 3500;
        public const double CargoCapacityKg = 12000;
        // Automatically unload cargo when crossing a Dumping Rail.
        public const bool AllowDumping = false;
        public const int StorageSlots = 32;
        public const double MaximumSpeedMetresPerSecond = 30;
        public const double PowerWatts = 0;
        public const double TractionNewtons = 0;
        public const double BrakingNewtons = 6000;
        public const double IntendedTrainMassKg = 0;
        public const double DurabilityHours = 200;
    }
}
