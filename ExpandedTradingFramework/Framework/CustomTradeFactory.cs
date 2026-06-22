using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;
using Il2CppTLD.Trader;
using Il2CppAssetReferenceGearList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Gear.AssetReferenceGearItemData>;
using Il2CppConversationUnlockList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Trader.AssetReferenceTraderConversation>;
using Il2CppExchangeItemList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Trader.ExchangeItem>;
using Il2CppTradeExchangeUnlockList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Trader.AssetReferenceTradeExchange>;

namespace ExpandedTradingFramework.Framework
{
    internal static class CustomTradeFactory
    {
        internal static bool TryCreateRuntime(CustomTradeDefinition definition, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (definition == null || !definition.Enabled) return false;
            if (string.IsNullOrEmpty(definition.Id))
            {
                Core.Log?.Error("Skipped custom trade with empty Id.");
                return false;
            }

            if (definition.CostItems == null || definition.CostItems.Length == 0)
            {
                Core.Log?.Error($"Skipped custom trade '{definition.Id}' because it has no cost item.");
                return false;
            }

            CustomTradeReward reward = definition.Reward ?? CustomTradeReward.None();
            bool hasPhysicalReward = reward.Type == CustomTradeRewardType.GearItem;

            if (hasPhysicalReward && string.IsNullOrEmpty(reward.GearName))
            {
                Core.Log?.Error($"Skipped custom trade '{definition.Id}' because it has a GearItem reward without GearName.");
                return false;
            }

            string displayGearName = GetDisplayRewardGearName(definition, reward);
            int displayAmount = GetDisplayRewardAmount(definition, reward);
            if (string.IsNullOrEmpty(displayGearName))
            {
                Core.Log?.Error($"Skipped custom trade '{definition.Id}' because it has no DisplayReward gear. Reward.None() trades must set DisplayReward = CustomTradeDisplay.Gear(...).");
                return false;
            }

            if (!GearResolver.TryResolvePrefab(displayGearName, out GearItem displayPrefab))
            {
                Core.Log?.Error($"Skipped custom trade '{definition.Id}' because display reward '{displayGearName}' could not be resolved.");
                return false;
            }

            GearItemData displayData = displayPrefab.GearItemData;
            if (displayData == null)
            {
                Core.Log?.Error($"Skipped custom trade '{definition.Id}' because display reward '{displayGearName}' has no GearItemData.");
                return false;
            }

            int displayBaseUnits = GetDefaultStackUnits(displayPrefab);

            GearItem rewardPrefab = null;
            GearItemData rewardData = null;
            string rewardReferenceKey = "";
            string rewardDisplayName = "";
            int rewardBaseUnits = 1;
            bool rewardIsStackable = false;

            if (hasPhysicalReward)
            {
                if (!GearResolver.TryResolvePrefab(reward.GearName, out rewardPrefab))
                {
                    Core.Log?.Error($"Skipped custom trade '{definition.Id}' because reward '{reward.GearName}' could not be resolved.");
                    return false;
                }

                rewardData = rewardPrefab.GearItemData;
                if (rewardData == null)
                {
                    Core.Log?.Error($"Skipped custom trade '{definition.Id}' because reward '{reward.GearName}' has no GearItemData.");
                    return false;
                }

                bool prefabIsStackable = GearResolver.IsStackablePrefab(rewardPrefab);
                rewardIsStackable = ShouldStackReward(reward, prefabIsStackable);
                rewardBaseUnits = rewardIsStackable && reward.Amount > 1 ? 1 : GetDefaultStackUnits(rewardPrefab);
                rewardReferenceKey = GearResolver.GetReferenceKey(rewardData, $"ExpandedTradingFramework_{definition.Id}_{reward.GearName}_Reward");
                rewardDisplayName = GearResolver.GetDisplayName(rewardData, reward.GearName);

                if (reward.Stacking == CustomRewardStacking.Stackable && !prefabIsStackable)
                {
                    Core.Log?.Warning($"Custom trade '{definition.Id}' forces stackable reward, but '{reward.GearName}' prefab has no StackableItem component. It will be treated as non-stackable.");
                }
            }

            CustomTradeCostRuntime[] costRuntimeItems = new CustomTradeCostRuntime[definition.CostItems.Length];
            Il2CppExchangeItemList normalCosts = new();
            Il2CppExchangeItemList cheapCosts = new();

            for (int i = 0; i < definition.CostItems.Length; i++)
            {
                CustomTradeItem cost = definition.CostItems[i];
                if (!TryCreateCostRuntime(definition, cost, i, out CustomTradeCostRuntime costRuntime)) return false;

                costRuntimeItems[i] = costRuntime;
                normalCosts.Add(CreateExchangeItem(costRuntime));
                cheapCosts.Add(CreateExchangeItem(costRuntime));
            }

            string displayReferenceKey = GearResolver.GetReferenceKey(displayData, $"ExpandedTradingFramework_{definition.Id}_{displayGearName}_DisplayReward");
            string displayName = GearResolver.GetDisplayName(displayData, displayGearName);

            TradeExchangeData tradeData = new()
            {
                m_Description = new LocalizedString(),
                m_MaxExchangeInSingleTrade = Math.Max(1, definition.MaxPerTrade),
                m_GainedItem = new AssetReferenceGearItemData(displayReferenceKey),
                m_GainedItemAmount = Math.Max(1, displayAmount),
                m_UnlockedExchanges = new Il2CppTradeExchangeUnlockList(),
                m_UnlockedConversations = new Il2CppConversationUnlockList(),
                m_ExchangeItems = normalCosts,
                m_CheapExchangeItems = cheapCosts,
                m_TradeExchangeName = definition.Id,
                m_IsSpecialRequest = definition.IsSpecialRequest
            };

            runtime = new CustomTradeRuntime
            {
                Definition = definition,
                TradeData = tradeData,
                CostItems = costRuntimeItems,
                DisplayRewardPrefab = displayPrefab,
                DisplayRewardGearItemData = displayData,
                DisplayRewardReferenceKey = displayReferenceKey,
                DisplayRewardName = displayName,
                DisplayRewardAmount = Math.Max(1, displayAmount),
                DisplayRewardBaseUnits = GetDisplayBaseUnits(definition, reward, displayGearName, displayBaseUnits, rewardIsStackable),
                HasPhysicalReward = hasPhysicalReward,
                RewardPrefab = rewardPrefab,
                RewardGearItemData = rewardData,
                RewardReferenceKey = rewardReferenceKey,
                RewardDisplayName = rewardDisplayName,
                RewardBaseUnits = rewardBaseUnits,
                RewardIsStackable = rewardIsStackable
            };

            return true;
        }

        private static string GetDisplayRewardGearName(CustomTradeDefinition definition, CustomTradeReward reward)
        {
            string displayGearName = definition?.DisplayReward?.GearName ?? "";
            if (!string.IsNullOrEmpty(displayGearName)) return displayGearName;
            if (reward != null && reward.Type == CustomTradeRewardType.GearItem) return reward.GearName ?? "";
            return "";
        }

        private static int GetDisplayRewardAmount(CustomTradeDefinition definition, CustomTradeReward reward)
        {
            if (definition?.DisplayReward != null && !string.IsNullOrEmpty(definition.DisplayReward.GearName)) return Math.Max(1, definition.DisplayReward.Amount);
            if (reward != null && reward.Type == CustomTradeRewardType.GearItem) return Math.Max(1, reward.Amount);
            return 1;
        }

        private static int GetDisplayBaseUnits(CustomTradeDefinition definition, CustomTradeReward reward, string displayGearName, int displayBaseUnits, bool rewardIsStackable)
        {
            if (definition?.DisplayReward != null && !string.IsNullOrEmpty(definition.DisplayReward.GearName)) return Math.Max(1, displayBaseUnits);
            if (reward == null || reward.Type != CustomTradeRewardType.GearItem) return Math.Max(1, displayBaseUnits);
            if (rewardIsStackable && reward.Amount > 1) return 1;
            if (!string.IsNullOrEmpty(displayGearName) && string.Equals(displayGearName, reward.GearName, StringComparison.OrdinalIgnoreCase)) return GetRewardBaseUnitsForDisplay(reward, displayBaseUnits, rewardIsStackable);
            return Math.Max(1, displayBaseUnits);
        }

        private static int GetRewardBaseUnitsForDisplay(CustomTradeReward reward, int displayBaseUnits, bool rewardIsStackable)
        {
            if (reward == null || reward.Type != CustomTradeRewardType.GearItem) return Math.Max(1, displayBaseUnits);
            if (rewardIsStackable && reward.Amount > 1) return 1;
            return Math.Max(1, displayBaseUnits);
        }

        private static int GetDefaultStackUnits(GearItem prefab)
        {
            if (prefab == null) return 1;

            try
            {
                StackableItem stackableItem = prefab.GetStackableItem();
                if (stackableItem == null) return 1;
                return Math.Max(1, stackableItem.DefaultUnitsInItem);
            }
            catch { }

            return 1;
        }

        private static bool TryCreateCostRuntime(CustomTradeDefinition tradeDefinition, CustomTradeItem cost, int costIndex, out CustomTradeCostRuntime costRuntime)
        {
            costRuntime = null;
            if (cost == null)
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because cost #{costIndex} is invalid.");
                return false;
            }

            string referenceKey = $"ExpandedTradingFramework_{tradeDefinition.Id}_Cost_{costIndex}_{cost.ExchangeType}";
            costRuntime = new CustomTradeCostRuntime
            {
                TradeDefinition = tradeDefinition,
                Definition = cost,
                ReferenceKey = referenceKey,
                DisplayName = GetFallbackDisplayName(cost)
            };

            switch (cost.ExchangeType)
            {
                case ExchangeItemType.GearAmount:
                case ExchangeItemType.GearWeight:
                    return TryConfigureGearCost(tradeDefinition, costRuntime, costIndex);

                case ExchangeItemType.CategoryAmount:
                case ExchangeItemType.CategoryWeight:
                    return TryConfigureCategoryCost(tradeDefinition, costRuntime, costIndex);

                case ExchangeItemType.Liquid:
                    return TryConfigureLiquidCost(tradeDefinition, costRuntime, costIndex);

                default:
                    Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because cost #{costIndex} uses unsupported type: {cost.ExchangeType}. Supported types: GearAmount, GearWeight, CategoryAmount, CategoryWeight, Liquid.");
                    return false;
            }
        }

        private static bool ValidateWeightCost(CustomTradeDefinition tradeDefinition, CustomTradeCostRuntime costRuntime, int costIndex)
        {
            CustomTradeItem cost = costRuntime.Definition;
            if (cost.ExchangeType != ExchangeItemType.GearWeight && cost.ExchangeType != ExchangeItemType.CategoryWeight) return true;
            if (cost.WeightKilograms > 0f) return true;

            Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because weight cost #{costIndex} must set WeightKilograms above 0.");
            return false;
        }

        private static bool TryConfigureGearCost(CustomTradeDefinition tradeDefinition, CustomTradeCostRuntime costRuntime, int costIndex)
        {
            CustomTradeItem cost = costRuntime.Definition;
            if (!ValidateWeightCost(tradeDefinition, costRuntime, costIndex)) return false;
            if (string.IsNullOrEmpty(cost.GearName))
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because gear cost #{costIndex} has no GearName.");
                return false;
            }

            if (!GearResolver.TryResolvePrefab(cost.GearName, out GearItem costPrefab))
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because cost '{cost.GearName}' could not be resolved.");
                return false;
            }

            GearItemData costData = costPrefab.GearItemData;
            if (costData == null)
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because cost '{cost.GearName}' has no GearItemData.");
                return false;
            }

            costRuntime.CostPrefab = costPrefab;
            costRuntime.GearItemData = costData;
            costRuntime.DisplayName = GearResolver.GetDisplayName(costData, cost.GearName);
            costRuntime.HasCustomIcon = cost.DisplayIcon != null && cost.DisplayIcon.HasIcon;
            costRuntime.Icon = costRuntime.HasCustomIcon ? CustomTradeIconResolver.Resolve(cost.DisplayIcon, tradeDefinition.Id, $"requirement #{costIndex}") : GearResolver.GetInventoryIcon(costPrefab);
            return true;
        }

        private static bool TryConfigureCategoryCost(CustomTradeDefinition tradeDefinition, CustomTradeCostRuntime costRuntime, int costIndex)
        {
            CustomTradeItem cost = costRuntime.Definition;
            if (!ValidateWeightCost(tradeDefinition, costRuntime, costIndex)) return false;
            if (cost.CategoryGearNames == null || cost.CategoryGearNames.Length == 0)
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because category cost #{costIndex} has no CategoryGearNames.");
                return false;
            }

            string displayName = !string.IsNullOrEmpty(cost.CategoryName) ? cost.CategoryName : "Any matching item";
            ExchangeItemCategory category = new()
            {
                m_CategoryName = new LocalizedString(),
                m_AllowedItems = new Il2CppAssetReferenceGearList()
            };

            string requestedIconGearName = cost.CategoryIcon == CategoryIcon.GearItem ? cost.CategoryIconGearName : "";
            GearItem firstAllowedPrefab = null;
            GearItem fallbackIconPrefab = null;
            for (int i = 0; i < cost.CategoryGearNames.Length; i++)
            {
                string gearName = cost.CategoryGearNames[i];
                if (string.IsNullOrEmpty(gearName)) continue;

                if (!GearResolver.TryResolvePrefab(gearName, out GearItem prefab))
                {
                    Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because category cost '{displayName}' could not resolve allowed item '{gearName}'.");
                    return false;
                }

                GearItemData allowedData = prefab.GearItemData;
                if (allowedData == null)
                {
                    Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because allowed category item '{gearName}' has no GearItemData.");
                    return false;
                }

                string itemReferenceKey = GearResolver.GetReferenceKey(allowedData, $"{costRuntime.ReferenceKey}_{gearName}");
                category.m_AllowedItems.Add(new AssetReferenceGearItemData(itemReferenceKey));
                costRuntime.CategoryAllowedData.Add(allowedData);

                if (firstAllowedPrefab == null) firstAllowedPrefab = prefab;
                if (fallbackIconPrefab == null) fallbackIconPrefab = prefab;
                if (cost.CategoryIcon == CategoryIcon.GearItem && !string.IsNullOrEmpty(requestedIconGearName) && string.Equals(requestedIconGearName, gearName, StringComparison.OrdinalIgnoreCase)) fallbackIconPrefab = prefab;
            }

            if (costRuntime.CategoryAllowedData.Count <= 0)
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because category cost #{costIndex} has no valid CategoryGearNames.");
                return false;
            }

            if (cost.CategoryIcon == CategoryIcon.GearItem && !string.IsNullOrEmpty(requestedIconGearName) && !GearResolver.TryResolvePrefab(requestedIconGearName, out fallbackIconPrefab))
            {
                Core.Log?.Warning($"Custom trade '{tradeDefinition.Id}' category cost '{displayName}' could not resolve CategoryIconGearName '{requestedIconGearName}', using first allowed item icon instead.");
                fallbackIconPrefab = firstAllowedPrefab;
            }

            bool hasCustomIcon = cost.DisplayIcon != null && cost.DisplayIcon.HasIcon;
            category.m_CategoryIcon = hasCustomIcon ? CustomTradeIconResolver.Resolve(cost.DisplayIcon, tradeDefinition.Id, $"category requirement #{costIndex}") : GearResolver.GetInventoryIcon(fallbackIconPrefab);

            costRuntime.Category = category;
            costRuntime.CategoryAllowedGearNames = cost.CategoryGearNames;
            costRuntime.CategoryIcon = cost.CategoryIcon;
            costRuntime.CategoryIconGearName = requestedIconGearName;
            costRuntime.HasCustomIcon = hasCustomIcon;
            costRuntime.DisplayName = displayName;
            costRuntime.Icon = category.m_CategoryIcon;
            return costRuntime.CategoryAllowedData.Count > 0;
        }

        private static bool TryConfigureLiquidCost(CustomTradeDefinition tradeDefinition, CustomTradeCostRuntime costRuntime, int costIndex)
        {
            CustomTradeItem cost = costRuntime.Definition;
            if (cost.LiquidLiters <= 0f)
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because liquid cost #{costIndex} must set LiquidLiters above 0.");
                return false;
            }

            if (!LiquidResolver.TryResolve(cost.LiquidKind, out LiquidType liquidType, out string displayName))
            {
                Core.Log?.Error($"Skipped custom trade '{tradeDefinition.Id}' because liquid cost #{costIndex} could not resolve '{cost.LiquidKind}'.");
                return false;
            }

            costRuntime.LiquidType = liquidType;
            costRuntime.DisplayName = displayName;
            costRuntime.HasCustomIcon = cost.DisplayIcon != null && cost.DisplayIcon.HasIcon;
            costRuntime.Icon = costRuntime.HasCustomIcon ? CustomTradeIconResolver.Resolve(cost.DisplayIcon, tradeDefinition.Id, $"liquid requirement #{costIndex}") : LiquidResolver.GetIcon(liquidType);
            return true;
        }

        private static string GetFallbackDisplayName(CustomTradeItem cost)
        {
            if (cost == null) return "Unknown";
            if (!string.IsNullOrEmpty(cost.GearName)) return cost.GearName;
            if (!string.IsNullOrEmpty(cost.CategoryName)) return cost.CategoryName;
            if (cost.LiquidKind != CustomLiquidKind.None) return cost.LiquidKind.ToString();
            return cost.ExchangeType.ToString();
        }

        private static bool ShouldStackReward(CustomTradeReward reward, bool prefabIsStackable)
        {
            if (reward == null || reward.Type != CustomTradeRewardType.GearItem) return false;

            return reward.Stacking switch
            {
                CustomRewardStacking.Stackable => prefabIsStackable,
                CustomRewardStacking.NonStackable => false,
                _ => prefabIsStackable,
            };
        }

        internal static ExchangeItem CreateExchangeItem(CustomTradeCostRuntime costRuntime)
        {
            ExchangeItem exchangeItem = new()
            {
                m_ExchangeItemType = costRuntime.Definition.ExchangeType,
                m_Amount = Math.Max(1, costRuntime.Definition.Amount),
                m_Weight = ItemWeight.FromKilograms(Math.Max(0.01f, costRuntime.Definition.WeightKilograms)),
                m_LiquidVolume = ItemLiquidVolume.FromLiters(Math.Max(0.01f, costRuntime.Definition.LiquidLiters)),
                m_ExchangedAmount = 0,
                m_ExchangedWeight = ItemWeight.Zero,
                m_ExchangedLiquidVolume = ItemLiquidVolume.Zero
            };

            switch (costRuntime.Definition.ExchangeType)
            {
                case ExchangeItemType.GearAmount:
                case ExchangeItemType.GearWeight:
                    exchangeItem.m_GearItemData = new AssetReferenceGearItemData(costRuntime.ReferenceKey);
                    break;
                case ExchangeItemType.CategoryAmount:
                case ExchangeItemType.CategoryWeight:
                    exchangeItem.m_ItemCategory = new AssetReferenceExchangeItemCategory(costRuntime.ReferenceKey);
                    break;
                case ExchangeItemType.Liquid:
                    exchangeItem.m_LiquidType = new AssetReferenceLiquidType(costRuntime.ReferenceKey);
                    break;
            }

            return exchangeItem;
        }

        internal static void RewriteReferencesPreservingActiveValues(CustomTradeRuntime runtime, TradeExchangeData tradeData)
        {
            RewriteReferencesPreservingActiveValues(runtime, tradeData, false);
        }

        internal static void RewriteActiveReferencesPreservingActiveValues(CustomTradeRuntime runtime, TradeExchangeData tradeData)
        {
            RewriteReferencesPreservingActiveValues(runtime, tradeData, true, 1);
        }

        internal static void RewriteActiveReferencesPreservingActiveValues(CustomTradeRuntime runtime, TradeExchangeData tradeData, int selectedMultiplier)
        {
            RewriteReferencesPreservingActiveValues(runtime, tradeData, true, selectedMultiplier);
        }

        private static void RewriteReferencesPreservingActiveValues(CustomTradeRuntime runtime, TradeExchangeData tradeData, bool usePhysicalReward)
        {
            RewriteReferencesPreservingActiveValues(runtime, tradeData, usePhysicalReward, 1);
        }

        private static void RewriteReferencesPreservingActiveValues(CustomTradeRuntime runtime, TradeExchangeData tradeData, bool usePhysicalReward, int selectedMultiplier)
        {
            if (runtime == null || runtime.Definition == null || tradeData == null) return;

            int preservedMultiplier = Math.Max(GetPreservedTradeMultiplier(runtime, tradeData, usePhysicalReward), Math.Max(1, selectedMultiplier));

            tradeData.m_TradeExchangeName = runtime.Definition.Id;
            tradeData.m_IsSpecialRequest = runtime.Definition.IsSpecialRequest;
            ApplyRewardReference(runtime, tradeData, usePhysicalReward, true);
            EnsureRewardAmountForMultiplier(runtime, tradeData, usePhysicalReward, preservedMultiplier);
            tradeData.m_Description ??= new LocalizedString();
            tradeData.m_UnlockedExchanges ??= new Il2CppTradeExchangeUnlockList();
            tradeData.m_UnlockedConversations ??= new Il2CppConversationUnlockList();

            tradeData.m_ExchangeItems = RewriteCostReferencesPreservingAmounts(runtime, tradeData.m_ExchangeItems, preservedMultiplier);
            tradeData.m_CheapExchangeItems = RewriteCostReferencesPreservingAmounts(runtime, tradeData.m_CheapExchangeItems, preservedMultiplier);
        }

        private static void ApplyRewardReference(CustomTradeRuntime runtime, TradeExchangeData tradeData, bool usePhysicalReward, bool preservePositiveAmount)
        {
            if (runtime == null || tradeData == null) return;

            bool usePhysical = usePhysicalReward && runtime.HasPhysicalReward && !string.IsNullOrEmpty(runtime.RewardReferenceKey);
            string referenceKey = usePhysical ? runtime.RewardReferenceKey : runtime.DisplayRewardReferenceKey;
            int amount = usePhysical && runtime.Definition?.Reward != null ? Math.Max(1, runtime.Definition.Reward.Amount) : Math.Max(1, runtime.DisplayRewardAmount);

            tradeData.m_GainedItem = new AssetReferenceGearItemData(referenceKey);
            if (usePhysical || !preservePositiveAmount || tradeData.m_GainedItemAmount <= 0) tradeData.m_GainedItemAmount = amount;
        }

        private static Il2CppExchangeItemList RewriteCostReferencesPreservingAmounts(CustomTradeRuntime runtime, Il2CppExchangeItemList exchangeItems, int multiplier)
        {
            Il2CppExchangeItemList result = exchangeItems;
            if (runtime == null || runtime.CostItems == null) return result;

            int safeMultiplier = Math.Max(1, multiplier);

            if (result == null || result.Count != runtime.CostItems.Length)
            {
                result = new Il2CppExchangeItemList();
                for (int i = 0; i < runtime.CostItems.Length; i++)
                {
                    ExchangeItem exchangeItem = CreateExchangeItem(runtime.CostItems[i]);
                    EnsureCostRequirementForMultiplier(exchangeItem, runtime.CostItems[i], safeMultiplier);
                    result.Add(exchangeItem);
                }

                return result;
            }

            for (int i = 0; i < result.Count; i++)
            {
                ExchangeItem exchangeItem = result[i];
                CustomTradeCostRuntime costRuntime = runtime.CostItems[i];
                if (exchangeItem == null)
                {
                    exchangeItem = CreateExchangeItem(costRuntime);
                    EnsureCostRequirementForMultiplier(exchangeItem, costRuntime, safeMultiplier);
                    result[i] = exchangeItem;
                    continue;
                }

                ApplyCostReferencePreservingAmounts(exchangeItem, costRuntime);
                EnsureCostRequirementForMultiplier(exchangeItem, costRuntime, safeMultiplier);
            }

            return result;
        }

        private static void ApplyCostReferencePreservingAmounts(ExchangeItem exchangeItem, CustomTradeCostRuntime costRuntime)
        {
            if (exchangeItem == null || costRuntime == null || costRuntime.Definition == null) return;

            exchangeItem.m_ExchangeItemType = costRuntime.Definition.ExchangeType;
            switch (costRuntime.Definition.ExchangeType)
            {
                case ExchangeItemType.GearAmount:
                case ExchangeItemType.GearWeight:
                    exchangeItem.m_GearItemData = new AssetReferenceGearItemData(costRuntime.ReferenceKey);
                    break;
                case ExchangeItemType.CategoryAmount:
                case ExchangeItemType.CategoryWeight:
                    exchangeItem.m_ItemCategory = new AssetReferenceExchangeItemCategory(costRuntime.ReferenceKey);
                    break;
                case ExchangeItemType.Liquid:
                    exchangeItem.m_LiquidType = new AssetReferenceLiquidType(costRuntime.ReferenceKey);
                    break;
            }
        }

        private static int GetPreservedTradeMultiplier(CustomTradeRuntime runtime, TradeExchangeData tradeData, bool usePhysicalReward)
        {
            if (runtime == null || runtime.Definition == null || tradeData == null) return 1;

            int multiplier = 1;

            if (!usePhysicalReward)
            {
                int baseRewardAmount = GetBaseRewardAmountForReference(runtime, false);
                if (baseRewardAmount > 0 && tradeData.m_GainedItemAmount > baseRewardAmount)
                {
                    multiplier = Math.Max(multiplier, RoundMultiplier(tradeData.m_GainedItemAmount, baseRewardAmount));
                }
            }

            multiplier = Math.Max(multiplier, GetPreservedAmountMultiplier(runtime, tradeData.m_ExchangeItems));
            multiplier = Math.Max(multiplier, GetPreservedAmountMultiplier(runtime, tradeData.m_CheapExchangeItems));

            return Math.Max(1, multiplier);
        }

        private static int GetBaseRewardAmountForReference(CustomTradeRuntime runtime, bool usePhysicalReward)
        {
            if (runtime == null) return 1;
            if (usePhysicalReward && runtime.HasPhysicalReward && runtime.Definition?.Reward != null) return Math.Max(1, runtime.Definition.Reward.Amount);
            return Math.Max(1, runtime.DisplayRewardAmount);
        }

        private static int GetPreservedAmountMultiplier(CustomTradeRuntime runtime, Il2CppExchangeItemList exchangeItems)
        {
            if (runtime == null || runtime.CostItems == null || exchangeItems == null) return 1;

            int count = Math.Min(runtime.CostItems.Length, exchangeItems.Count);
            int multiplier = 1;

            for (int i = 0; i < count; i++)
            {
                ExchangeItem exchangeItem = exchangeItems[i];
                CustomTradeCostRuntime costRuntime = runtime.CostItems[i];
                if (exchangeItem == null || costRuntime == null || costRuntime.Definition == null) continue;

                switch (costRuntime.Definition.ExchangeType)
                {
                    case ExchangeItemType.GearAmount:
                    case ExchangeItemType.CategoryAmount:
                        int baseAmount = Math.Max(1, costRuntime.Definition.Amount);
                        int activeAmount = Math.Max(1, exchangeItem.m_Amount);
                        if (activeAmount > baseAmount) multiplier = Math.Max(multiplier, RoundMultiplier(activeAmount, baseAmount));
                        break;

                    case ExchangeItemType.GearWeight:
                    case ExchangeItemType.CategoryWeight:
                        multiplier = Math.Max(multiplier, RoundMultiplier(ToKilogramsSafe(exchangeItem.m_Weight), Math.Max(0.01f, costRuntime.Definition.WeightKilograms)));
                        break;

                    case ExchangeItemType.Liquid:
                        multiplier = Math.Max(multiplier, RoundMultiplier(ToLitersSafe(exchangeItem.m_LiquidVolume), Math.Max(0.01f, costRuntime.Definition.LiquidLiters)));
                        break;
                }
            }

            return Math.Max(1, multiplier);
        }

        private static int RoundMultiplier(int activeAmount, int baseAmount)
        {
            if (baseAmount <= 0) return 1;
            return Math.Max(1, (int)Math.Round((double)Math.Max(1, activeAmount) / baseAmount));
        }

        private static int RoundMultiplier(float activeAmount, float baseAmount)
        {
            if (baseAmount <= 0f) return 1;
            return Math.Max(1, (int)Math.Round(Math.Max(0.01f, activeAmount) / baseAmount));
        }

        private static float ToKilogramsSafe(ItemWeight weight)
        {
            try
            {
                return ParseMetricValueSafe(weight.ToStringMetric(4u));
            }
            catch { }

            return 0f;
        }

        private static float ToLitersSafe(ItemLiquidVolume volume)
        {
            try
            {
                return ParseMetricValueSafe(volume.ToStringMetric(4u));
            }
            catch { }

            return 0f;
        }

        private static float ParseMetricValueSafe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0f;

            System.Text.StringBuilder builder = new();
            bool foundDigit = false;
            bool foundDecimalSeparator = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsDigit(c))
                {
                    builder.Append(c);
                    foundDigit = true;
                    continue;
                }

                if ((c == '.' || c == ',') && !foundDecimalSeparator)
                {
                    builder.Append('.');
                    foundDecimalSeparator = true;
                    continue;
                }

                if ((c == '-' || c == '+') && builder.Length == 0)
                {
                    builder.Append(c);
                    continue;
                }

                if (foundDigit) break;
            }

            if (builder.Length == 0) return 0f;
            return float.TryParse(builder.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? Math.Max(0f, parsed) : 0f;
        }

        private static void EnsureRewardAmountForMultiplier(CustomTradeRuntime runtime, TradeExchangeData tradeData, bool usePhysicalReward, int multiplier)
        {
            if (runtime == null || tradeData == null) return;

            int safeMultiplier = Math.Max(1, multiplier);
            int baseAmount = GetBaseRewardAmountForReference(runtime, usePhysicalReward);
            int expectedAmount = Math.Max(1, baseAmount * safeMultiplier);

            if (tradeData.m_GainedItemAmount < expectedAmount) tradeData.m_GainedItemAmount = expectedAmount;
        }

        private static void EnsureCostRequirementForMultiplier(ExchangeItem exchangeItem, CustomTradeCostRuntime costRuntime, int multiplier)
        {
            if (exchangeItem == null || costRuntime == null || costRuntime.Definition == null) return;

            int safeMultiplier = Math.Max(1, multiplier);
            if (safeMultiplier <= 1) return;

            switch (costRuntime.Definition.ExchangeType)
            {
                case ExchangeItemType.GearAmount:
                case ExchangeItemType.CategoryAmount:
                    int expectedAmount = Math.Max(1, costRuntime.Definition.Amount) * safeMultiplier;
                    if (exchangeItem.m_Amount < expectedAmount) exchangeItem.m_Amount = expectedAmount;
                    break;

                case ExchangeItemType.GearWeight:
                case ExchangeItemType.CategoryWeight:
                    exchangeItem.m_Weight = ItemWeight.FromKilograms(Math.Max(0.01f, costRuntime.Definition.WeightKilograms * safeMultiplier));
                    break;

                case ExchangeItemType.Liquid:
                    exchangeItem.m_LiquidVolume = ItemLiquidVolume.FromLiters(Math.Max(0.01f, costRuntime.Definition.LiquidLiters * safeMultiplier));
                    break;
            }
        }

    }
}