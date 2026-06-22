using Il2CppTLD.Gear;
using Il2CppTLD.Trader;

namespace ExpandedTradingFramework.Framework
{
    internal sealed class CustomTradeRuntime
    {
        internal CustomTradeDefinition Definition;
        internal TradeExchangeData TradeData;
        internal CustomTradeCostRuntime[] CostItems;
        internal GearItem DisplayRewardPrefab;
        internal GearItemData DisplayRewardGearItemData;
        internal string DisplayRewardReferenceKey;
        internal string DisplayRewardName;
        internal int DisplayRewardAmount;
        internal int DisplayRewardBaseUnits = 1;
        internal int VisibleDisplayRewardAmount => Math.Max(1, DisplayRewardAmount) * Math.Max(1, DisplayRewardBaseUnits);

        internal bool HasPhysicalReward;
        internal GearItem RewardPrefab;
        internal GearItemData RewardGearItemData;
        internal string RewardReferenceKey;
        internal string RewardDisplayName;
        internal int RewardBaseUnits = 1;
        internal int VisibleRewardAmount => Math.Max(1, Definition?.Reward?.Amount ?? 1) * Math.Max(1, RewardBaseUnits);
        internal bool RewardIsStackable;

        internal bool IsNormal => Definition != null && !Definition.IsSpecialRequest;
        internal bool IsSpecialRequest => Definition != null && Definition.IsSpecialRequest;
    }

    internal sealed class CustomTradeCostRuntime
    {
        internal CustomTradeDefinition TradeDefinition;
        internal CustomTradeItem Definition;
        internal string ReferenceKey;
        internal string DisplayName;
        internal Texture2D Icon;

        internal GearItemData GearItemData;
        internal GearItem CostPrefab;

        internal ExchangeItemCategory Category;
        internal List<GearItemData> CategoryAllowedData = [];
        internal string[] CategoryAllowedGearNames = [];
        internal CategoryIcon CategoryIcon = CategoryIcon.Auto;
        internal string CategoryIconGearName = "";
        internal bool HasCustomIcon;

        internal LiquidType LiquidType;
    }
}