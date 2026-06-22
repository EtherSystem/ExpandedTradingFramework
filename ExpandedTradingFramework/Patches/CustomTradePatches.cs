using ExpandedTradingFramework.Framework;
using Il2CppTLD.Gear;
using Il2CppTLD.Trader;

using Il2CppTradeExchangeDataList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Trader.TradeExchangeData>;

namespace ExpandedTradingFramework.Patches
{
    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.Deserialize))]
    internal static class TraderManagerDeserializePatch
    {
        private static void Prefix(TraderState state)
        {
            CustomTradeRegistry.PrepareTraderStateBeforeDeserialize(state);
        }
    }


    [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.SaveGlobalData))]
    internal static class SaveGameSystemSaveGlobalDataPatch
    {
        private static void Prefix()
        {
            CustomTradeRegistry.PrepareTraderStateBeforeSave(GameManager.m_TraderManager);
        }

        private static void Postfix()
        {
            CustomTradeRegistry.SaveFrameworkState("SaveGlobalData");
        }
    }


    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.GetAvailableTradeExchanges))]
    internal static class TraderManagerGetAvailableTradeExchangesPatch
    {
        private static void Postfix(TraderManager __instance, ref Il2CppTradeExchangeDataList __result)
        {
            __result = CustomTradeRegistry.BuildNormalTradeResultCopy(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.GetSpecialRequests))]
    internal static class TraderManagerGetSpecialRequestsPatch
    {
        private static void Postfix(TraderManager __instance, ref Il2CppTradeExchangeDataList __result)
        {
            __result = CustomTradeRegistry.BuildSpecialRequestResultCopy(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.SpecialRequestActive), MethodType.Getter)]
    internal static class TraderManagerSpecialRequestActivePatch
    {
        private static void Postfix(TraderManager __instance, ref bool __result)
        {
            bool removedStaleSpecialRequests = CustomTradeRegistry.CleanupStaleSpecialRequests(__instance);
            if (CustomTradeRegistry.HasEligibleSpecialRequests(__instance))
            {
                __result = true;
                return;
            }

            if (removedStaleSpecialRequests && !CustomTradeRegistry.HasAnySpecialRequestInState(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.SpecialRequestHeaderText), MethodType.Getter)]
    internal static class TraderManagerSpecialRequestHeaderTextPatch
    {
        private static void Postfix(TraderManager __instance, ref string __result)
        {
            bool removedStaleSpecialRequests = CustomTradeRegistry.CleanupStaleSpecialRequests(__instance);
            if (CustomTradeRegistry.TryGetSpecialRequestHeaderOverride(__instance, __result, out string text))
            {
                __result = text;
                return;
            }

            if (removedStaleSpecialRequests && !CustomTradeRegistry.HasAnySpecialRequestInState(__instance)) __result = "";
        }
    }

    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.SpecialRequestDescriptionText), MethodType.Getter)]
    internal static class TraderManagerSpecialRequestDescriptionTextPatch
    {
        private static void Postfix(TraderManager __instance, ref string __result)
        {
            bool removedStaleSpecialRequests = CustomTradeRegistry.CleanupStaleSpecialRequests(__instance);
            if (CustomTradeRegistry.TryGetSpecialRequestDescriptionOverride(__instance, __result, out string text))
            {
                __result = text;
                return;
            }

            if (removedStaleSpecialRequests && !CustomTradeRegistry.HasAnySpecialRequestInState(__instance)) __result = "";
        }
    }

    [HarmonyPatch(typeof(TradeExchangeData), nameof(TradeExchangeData.IsValidInCurrentGamemode))]
    internal static class TradeExchangeDataIsValidInCurrentGamemodePatch
    {
        private static bool Prefix(TradeExchangeData __instance, ref bool __result)
        {
            if (!CustomTradeRegistry.TryGetTrade(__instance, out _)) return true;

            CustomTradeRegistry.EnsureTradeDataRuntimeReferences(__instance, "gamemode validation");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TradeExchangeData), nameof(TradeExchangeData.CloneForActiveTrade))]
    internal static class TradeExchangeDataCloneForActiveTradePatch
    {
        private static void Postfix(TradeExchangeData __instance, ref TradeExchangeData __result)
        {
            if (!CustomTradeRegistry.TryGetTrade(__instance, out CustomTradeRuntime runtime)) return;
            if (__result == null) return;

            int selectedMultiplier = CustomTradeRegistry.GetRememberedTradeSelectionMultiplier(__instance, runtime);
            CustomTradeFactory.RewriteActiveReferencesPreservingActiveValues(runtime, __result, selectedMultiplier);
            if (CustomTradeRegistry.VerboseLifecycleLogging) Core.Log?.Msg($"Rewrote custom trade references on active-trade clone while preserving selected repeat count: '{runtime.Definition.Id}' x{selectedMultiplier}.");
        }
    }

    [HarmonyPatch(typeof(TradeExchangeData), nameof(TradeExchangeData.GetGainedItemData))]
    internal static class TradeExchangeDataGetGainedItemDataPatch
    {
        private static bool Prefix(TradeExchangeData __instance, ref GearItemData __result)
        {
            if (!CustomTradeRegistry.TryGetTrade(__instance, out CustomTradeRuntime runtime)) return true;

            __result = CustomTradeRegistry.GetGainedItemDataForTradeExchange(__instance, runtime);
            return false;
        }
    }

    [HarmonyPatch(typeof(TradeExchangeData), nameof(TradeExchangeData.GetTradeExchangeHeader))]
    internal static class TradeExchangeDataGetTradeExchangeHeaderPatch
    {
        private static bool Prefix(TradeExchangeData __instance, int multiplier, ref string __result)
        {
            if (!CustomTradeRegistry.TryGetTrade(__instance, out CustomTradeRuntime runtime)) return true;

            if (runtime.IsSpecialRequest)
            {
                __result = CustomTradeRegistry.GetSpecialRequestHeaderText(runtime);
                CustomTradeRegistry.LogTradeDisplayBehavior(runtime, "special request header", __result);
                return false;
            }

            int effectiveMultiplier = CustomTradeRegistry.RememberTradeSelectionMultiplier(__instance, GetEffectiveDisplayMultiplier(__instance, runtime, multiplier));
            int amount = Math.Max(1, effectiveMultiplier) * Math.Max(1, runtime.VisibleDisplayRewardAmount);
            __result = CustomTradeRegistry.FormatRewardDisplay(runtime.DisplayRewardName, amount);
            CustomTradeRegistry.LogTradeDisplayBehavior(runtime, $"trade header x{effectiveMultiplier}", __result);
            return false;
        }

        private static int GetEffectiveDisplayMultiplier(TradeExchangeData tradeData, CustomTradeRuntime runtime, int uiMultiplier)
        {
            int multiplier = Math.Max(1, uiMultiplier);
            if (tradeData == null || runtime == null) return multiplier;

            try
            {
                int gainedAmount = Math.Max(0, tradeData.m_GainedItemAmount);
                multiplier = Math.Max(multiplier, GetMultiplierFromAmount(gainedAmount, runtime.VisibleDisplayRewardAmount));

                if (runtime.HasPhysicalReward)
                {
                    multiplier = Math.Max(multiplier, GetMultiplierFromAmount(gainedAmount, runtime.VisibleRewardAmount));
                    multiplier = Math.Max(multiplier, GetMultiplierFromAmount(gainedAmount, runtime.Definition?.Reward?.Amount ?? 1));
                }
            }
            catch { }

            return Math.Max(1, multiplier);
        }

        private static int GetMultiplierFromAmount(int activeAmount, int baseAmount)
        {
            if (activeAmount <= 0 || baseAmount <= 0) return 1;
            if (activeAmount <= baseAmount) return 1;
            return Math.Max(1, (int)Math.Round((double)activeAmount / baseAmount));
        }
    }


    [HarmonyPatch(typeof(Panel_TradeSelection), nameof(Panel_TradeSelection.UpdateSelectedExchange))]
    internal static class PanelTradeSelectionUpdateSelectedExchangePatch
    {
        private static void Postfix(Panel_TradeSelection __instance)
        {
            if (__instance == null) return;

            TradeExchangeData selectedTrade;
            try
            {
                selectedTrade = __instance.SelectedTradeExchange;
            }
            catch
            {
                return;
            }

            if (!CustomTradeRegistry.TryGetTrade(selectedTrade, out CustomTradeRuntime runtime)) return;

            CustomTradeRegistry.ApplyTradeSelectionUiOverrides(__instance, runtime);
        }
    }

    [HarmonyPatch(typeof(ActiveTradeState), nameof(ActiveTradeState.HasRewards))]
    internal static class ActiveTradeStateHasRewardsPatch
    {
        private static bool Prefix(ActiveTradeState __instance, ref bool __result)
        {
            if (!CustomTradeRegistry.ContainsCustomExchange(__instance)) return true;

            __result = CustomTradeRegistry.HasAnyPhysicalReward(__instance);
            return false;
        }
    }


    [HarmonyPatch(typeof(ExchangeBox), nameof(ExchangeBox.PrepareAndShowRewards))]
    internal static class ExchangeBoxPrepareAndShowRewardsPatch
    {
        private static void Prefix(ExchangeBox __instance)
        {
            CustomTradeRegistry.BeginRewardPreparation(__instance);
        }

        private static void Postfix(ExchangeBox __instance)
        {
            CustomTradeRegistry.NormalizeStackableRewards(__instance);
        }

        private static void Finalizer(ExchangeBox __instance)
        {
            CustomTradeRegistry.EndRewardPreparation();
        }
    }

    [HarmonyPatch(typeof(ExchangeBox), nameof(ExchangeBox.AddItem))]
    internal static class ExchangeBoxAddItemPatch
    {
        private static bool Prefix(ExchangeBox __instance, int exchange, GearItem gearItem)
        {
            if (CustomTradeRegistry.TrySuppressNoRewardItem(__instance, exchange, gearItem)) return false;
            return !CustomTradeRegistry.TryAbsorbStackableRewardItem(__instance, exchange, gearItem);
        }
    }



    [HarmonyPatch(typeof(Panel_ExchangeBox), nameof(Panel_ExchangeBox.ShowExchangeBox))]
    internal static class PanelExchangeBoxShowExchangeBoxPatch
    {
        private static void Prefix(Panel_ExchangeBox __instance, ExchangeBox exchangeBox)
        {
            CustomTradeRegistry.LogExchangeBoxRequirementProgressSlotCount(__instance);
            CustomTradeRegistry.NormalizeStackableRewardsBeforeExchangeBoxUi(exchangeBox, "panel show");
        }
    }

    [HarmonyPatch(typeof(Panel_ExchangeBox), nameof(Panel_ExchangeBox.RefreshTables))]
    internal static class PanelExchangeBoxRefreshTablesPatch
    {
        private static void Prefix(Panel_ExchangeBox __instance)
        {
            CustomTradeRegistry.LogExchangeBoxRequirementProgressSlotCount(__instance);
            ExchangeBox exchangeBox = __instance?.m_ExchangeBox;
            CustomTradeRegistry.NormalizeStackableRewardsBeforeExchangeBoxUi(exchangeBox, "panel refresh");
        }
    }

    [HarmonyPatch(typeof(Utils), nameof(Utils.GetInventoryIconTexture), [typeof(GearItem)])]
    internal static class UtilsGetInventoryIconTexturePatch
    {
        private static bool Prefix(GearItem gi, ref Texture2D __result)
        {
            if (gi == null)
            {
                __result = null;
                return false;
            }

            try
            {
                string _ = gi.name;
            }
            catch
            {
                __result = null;
                return false;
            }

            return true;
        }
    }


    [HarmonyPatch(typeof(ExchangeBox), nameof(ExchangeBox.SetActiveTrade))]
    internal static class ExchangeBoxSetActiveTradePatch
    {
        private static void Prefix(ActiveTradeState activeTrade)
        {
            CustomTradeRegistry.RehydrateActiveTradeState(activeTrade, "exchange-box set-active-trade");
        }
    }


    [HarmonyPatch(typeof(Panel_TradeSelection), nameof(Panel_TradeSelection.AddSelectedExchange))]
    internal static class PanelTradeSelectionAddSelectedExchangePatch
    {
        private static bool Prefix(Panel_TradeSelection __instance)
        {
            return !CustomTradeRegistry.ShouldBlockAddingSelectedSpecialRequest(__instance);
        }
    }

    [HarmonyPatch(typeof(TraderManager), nameof(TraderManager.CompleteTrade))]
    internal static class TraderManagerCompleteTradePatch
    {
        private static void Prefix(TraderManager __instance, ref ActiveTradeState __state)
        {
            __state = __instance?.GetActiveTrade();
        }

        private static void Postfix(ActiveTradeState __state)
        {
            CustomTradeRegistry.OnTradeCompleted(__state);
        }
    }


    [HarmonyPatch(typeof(Panel_TradeSelection), nameof(Panel_TradeSelection.UpdateSelectedExchangeRequirements))]
    internal static class PanelTradeSelectionUpdateSelectedExchangeRequirementsPatch
    {
        private static bool s_LoggedRequirementSlotCount;

        private static void Postfix(Panel_TradeSelection __instance)
        {
            if (!s_LoggedRequirementSlotCount)
            {
                int slotCount = 0;
                try
                {
                    if (__instance != null && __instance.m_RequirementItems != null) slotCount = __instance.m_RequirementItems.Count;
                }
                catch { }

                s_LoggedRequirementSlotCount = true;
                Core.Log?.Msg($"Trader trade-selection list requirement UI slots detected: {slotCount}. The trade-selection list can show this many requirements, but the Exchange Box may expose fewer visible progress rows.");
            }

            if (__instance == null) return;

            TradeExchangeData selectedTrade;
            try
            {
                selectedTrade = __instance.SelectedTradeExchange;
            }
            catch
            {
                return;
            }

            if (!CustomTradeRegistry.TryGetTrade(selectedTrade, out CustomTradeRuntime runtime)) return;
            CustomTradeRegistry.ApplyTradeSelectionUiOverrides(__instance, runtime);
        }
    }


    [HarmonyPatch(typeof(Panel_Log), nameof(Panel_Log.BuildTrader))]
    internal static class PanelLogBuildTraderPatch
    {
        private static void Postfix(Panel_Log __instance)
        {
            CustomTradeRegistry.ApplyJournalSpecialRequestListEntry(__instance);
        }
    }

    [HarmonyPatch(typeof(Panel_Log), nameof(Panel_Log.Update))]
    internal static class PanelLogUpdatePatch
    {
        private static void Postfix(Panel_Log __instance)
        {
            CustomTradeRegistry.ApplyJournalSpecialRequestListEntry(__instance);
        }
    }

    [HarmonyPatch(typeof(Panel_Log), nameof(Panel_Log.OnTraderLogItemSelected))]
    internal static class PanelLogOnTraderLogItemSelectedPatch
    {
        private static void Postfix(Panel_Log __instance, TraderLogItem traderLogItem)
        {
            CustomTradeRegistry.ApplyJournalSpecialRequestSelection(__instance, traderLogItem);
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.IsValidInCurrentGamemode))]
    internal static class ExchangeItemIsValidInCurrentGamemodePatch
    {
        private static bool Prefix(ExchangeItem __instance, ref bool __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out _)) return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetGearItemDataIfValid))]
    internal static class ExchangeItemGetGearItemDataIfValidPatch
    {
        private static bool Prefix(ExchangeItem __instance, ref GearItemData __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;
            if (costRuntime.Definition.ExchangeType != ExchangeItemType.GearAmount && costRuntime.Definition.ExchangeType != ExchangeItemType.GearWeight) return true;

            __result = costRuntime.GearItemData;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetExchangeItemCategoryIfValid))]
    internal static class ExchangeItemGetExchangeItemCategoryIfValidPatch
    {
        private static bool Prefix(ExchangeItem __instance, ref ExchangeItemCategory __result)
        {
            if (!CustomTradeRegistry.TryGetCategoryForCost(__instance, out ExchangeItemCategory category)) return true;

            __result = category;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetLiquidTypeIfValid))]
    internal static class ExchangeItemGetLiquidTypeIfValidPatch
    {
        private static bool Prefix(ExchangeItem __instance, ref LiquidType __result)
        {
            if (!CustomTradeRegistry.TryGetLiquidForCost(__instance, out LiquidType liquidType)) return true;

            __result = liquidType;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.IsMatchingType))]
    internal static class ExchangeItemIsMatchingTypePatch
    {
        private static bool Prefix(ExchangeItem __instance, GearItem gearItem, ref bool __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;

            if (costRuntime.Definition.ExchangeType == ExchangeItemType.Liquid) return true;

            __result = CustomTradeRegistry.IsMatchingCost(costRuntime, gearItem);
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetExchangeItemIcon))]
    internal static class ExchangeItemGetExchangeItemIconPatch
    {
        private static bool Prefix(ExchangeItem __instance, ref Texture2D __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;

            __result = CustomTradeRegistry.GetCostIcon(costRuntime);
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetExchangeItemName))]
    internal static class ExchangeItemGetExchangeItemNamePatch
    {
        private static bool Prefix(ExchangeItem __instance, ref string __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;

            __result = CustomTradeRegistry.GetCostDisplayName(costRuntime);
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetFormattedString))]
    internal static class ExchangeItemGetFormattedStringPatch
    {
        private static bool Prefix(ExchangeItem __instance, int multiplier, ref string __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;

            __result = CustomTradeRegistry.GetCostFormattedString(__instance, costRuntime, multiplier);
            return false;
        }
    }

    [HarmonyPatch(typeof(ExchangeItem), nameof(ExchangeItem.GetFormattedStringWithExchanged))]
    internal static class ExchangeItemGetFormattedStringWithExchangedPatch
    {
        private static bool Prefix(ExchangeItem __instance, ref string __result)
        {
            if (!CustomTradeRegistry.TryGetCost(__instance, out CustomTradeCostRuntime costRuntime)) return true;

            __result = CustomTradeRegistry.GetCostFormattedStringWithExchanged(__instance, costRuntime);
            return false;
        }
    }
}