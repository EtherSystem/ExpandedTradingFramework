using Il2CppTLD.Trader;

namespace ExpandedTradingFramework.Framework
{
    public enum CustomTradeSelectionMode
    {
        DrawPool,
        AppearanceChance
    }

    public enum CustomTradeDrawPool
    {
        AlwaysShown,
        Common,
        Rare,
        ExtraRare
    }

    public enum CustomTradeRewardType
    {
        None,
        GearItem
    }

    public enum CustomRewardStacking
    {
        Auto,
        Stackable,
        NonStackable
    }

    public enum CustomLiquidKind
    {
        None,
        PotableWater,
        NonPotableWater,
        Kerosene,
        Antiseptic
    }

    public enum CategoryIcon
    {
        Auto,
        GearItem,
        AnyHide,
        AnyHarvestPlant,
        AnyFishAnimalMeat,
        AnyFishMeat
    }

    public sealed class CustomTradeDefinition
    {
        public string Id = ""; // every IDs needs to be different
        public bool Enabled = true;
        public string Description = "";
        public bool IsSpecialRequest = false;
        // Optional special request UI overrides, these affect the Trader special-request card/selection display and the Journal Trader details panel
        public CustomSpecialRequestDisplay SpecialRequest = new();

        // DrawPool mode uses DrawPool and vanilla TraderTunables draw counts (some odd behavior can happen using this, but its the closest behavior to vanilla trades)
        // AppearanceChance mode ignores DrawPool and uses AppearanceChance as a direct normalized roll
        public CustomTradeSelectionMode SelectionMode = CustomTradeSelectionMode.DrawPool;
        public CustomTradeDrawPool DrawPool = CustomTradeDrawPool.Common;
        public float AppearanceChance = 1f;

        public int MinTrust = 0; // minimum trust needed for the trade to appear
        public int MaxPerTrade = 1; // maximum possible iterations of the trade in one go
        public bool Repeatable = true; // is the trade repeatable overall or not, false = unique
        public CustomTradeItem[] CostItems = [];

        // DisplayReward controls what the Trader UI shows. Auto display uses the physical reward when possible
        // Reward controls what is actually spawned in the exchange box. Reward.None() creates a completion-only trade
        public CustomTradeDisplay DisplayReward = CustomTradeDisplay.Auto();
        public CustomTradeReward Reward = new();

        // Optional callback invoked when the trade is completed. Useful for completion-only trades
        public Action<CustomTradeCompletionContext> OnCompleted;
    }


    public sealed class CustomSpecialRequestDisplay
    {
        public string Title = "";
        public string Description = "";
        public string RequirementText = "";
        public bool HideRequirementIcon = true;
        public string JournalTitle = "";
        public string JournalDescription = "";
        public string[] JournalTips = [];
    }

    public sealed class CustomTradeCompletionContext
    {
        public string TradeId = "";
        public int CompletionCount;
        public bool IsCompletionOnly;
        public CustomTradeDefinition Definition;
    }

    public sealed class CustomTradeItem
    {
        public ExchangeItemType ExchangeType = ExchangeItemType.GearAmount;

        // GearAmount / GearWeight
        public string GearName = "";

        // CategoryAmount / CategoryWeight. The framework creates a runtime category from these names
        public string CategoryName = "";
        public string[] CategoryGearNames = [];

        // Category icon selection
        // Auto uses the first allowed item icon
        // GearItem uses CategoryIconGearName as a direct inventory icon
        // AnyHide / AnyHarvestPlant / AnyFishAnimalMeat / AnyFishMeat reuse vanilla Trader category requirement icons
        public CategoryIcon CategoryIcon = CategoryIcon.Auto;
        public string CategoryIconGearName = "";
        public CustomTradeIcon DisplayIcon = CustomTradeIcon.None();

        // Liquid. Using vanilla LiquidType icon is used
        public CustomLiquidKind LiquidKind = CustomLiquidKind.None;
        public float LiquidLiters = 0f;

        // GearWeight / CategoryWeight. Value is expressed in kilograms, matching ItemWeight.FromKilograms(...)
        public float WeightKilograms = 0f;

        public int Amount = 1;
    }

    public sealed class CustomTradeDisplay
    {
        public string GearName = "";
        public int Amount = 1;

        public static CustomTradeDisplay Auto()
        {
            return new CustomTradeDisplay();
        }

        public static CustomTradeDisplay Gear(string gearName, int amount = 1)
        {
            return new CustomTradeDisplay { GearName = gearName ?? "", Amount = amount };
        }
    }

    public sealed class CustomTradeReward
    {
        public CustomTradeRewardType Type = CustomTradeRewardType.GearItem;
        public string GearName = "";
        public int Amount = 1;
        public CustomRewardStacking Stacking = CustomRewardStacking.Auto;

        public static CustomTradeReward Gear(string gearName, int amount = 1, CustomRewardStacking stacking = CustomRewardStacking.Auto)
        {
            return new CustomTradeReward { Type = CustomTradeRewardType.GearItem, GearName = gearName ?? "", Amount = amount, Stacking = stacking };
        }

        public static CustomTradeReward None()
        {
            return new CustomTradeReward { Type = CustomTradeRewardType.None, GearName = "", Amount = 0, Stacking = CustomRewardStacking.NonStackable };
        }

        public bool IsNone => Type == CustomTradeRewardType.None;
        public bool IsGearItem => Type == CustomTradeRewardType.GearItem;
    }
}