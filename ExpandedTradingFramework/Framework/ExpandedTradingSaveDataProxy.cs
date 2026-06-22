namespace ExpandedTradingFramework.Framework
{
    public sealed class ExpandedTradingSaveDataProxy
    {
        public int SaveVersion = 1;
        public List<string> CompletedNonRepeatableTradeIds = [];
        public Dictionary<string, int> CompletionCounts = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> AppearanceRolls = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SelectedNormalTradeIds = [];
        public string LastNormalVanillaInventorySignature = "";
        public string ActiveCustomTradeId = "";
        public int ActiveCustomTradeMultiplier = 1;
    }
}