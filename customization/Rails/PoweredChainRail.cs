// Edit values and restart Eco. Keep names and RailKey unchanged.
namespace RailworksWorkshop.RailSettings
{
    public static class PoweredChainRailSettings
    {
        public const string RailKey = "MinecartChain";
        public const int MetalBars = 3;
        public const int WoodBoards = 0;
        public const int HewnLogs = 2;
        public const int Wood = 0;
        public const int WoodenGears = 1;
        public const int SteelGears = 0;
        public const int Lubricant = 0;
        public const int Fabric = 0;
        public const int OutputCount = 4;
        public const float LaborCalories = 100f;
        public const float CraftMinutes = 1f;
        // Lift power and force; grid demand is 2 W per powered rail.
        public const double ChainLiftWattsPerBlock = 2500;
        public const double ChainTractionNewtons = 110000;
    }
}
