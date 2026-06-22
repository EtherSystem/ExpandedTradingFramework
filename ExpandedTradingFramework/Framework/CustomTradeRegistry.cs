using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;
using Il2CppTLD.SaveState;
using Il2CppTLD.Trader;

using Il2CppTradeExchangeDataList = Il2CppSystem.Collections.Generic.List<Il2CppTLD.Trader.TradeExchangeData>;

namespace ExpandedTradingFramework.Framework
{
    public static class CustomTradeRegistry
    {
        internal static bool VerboseLifecycleLogging => Core.DiagnosticLoggingEnabled;

        private static readonly List<CustomTradeDefinition> s_Definitions = [];
        private static readonly List<CustomTradeDefinition> s_ExternalDefinitions = [];
        private static readonly List<CustomTradeRuntime> s_RuntimeTrades = [];
        private static readonly Dictionary<string, CustomTradeRuntime> s_RuntimeTradesById = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ExpandedTradingSaveDataManager s_SaveDataManager = new();
        private static ExpandedTradingSaveDataProxy s_SaveData = new();
        private static bool s_SaveDataLoaded;
        private static readonly HashSet<string> s_SyncedSpecialRequests = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedStackableRewardPreparation = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedNoRewardSuppression = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> s_LastObservedTradeMultipliers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<StackableRewardAmountOverride> s_StackableRewardAmountOverrides = [];

        private static int s_RewardPreparationDepth;
        private static bool s_Built;
        private static readonly Dictionary<CategoryIcon, Texture2D> s_VanillaCategoryIcons = [];
        private static readonly HashSet<string> s_LoggedVanillaCategoryIconWarnings = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedMissingCostIconWarnings = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedCostIconResolutionDiagnostics = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedCostMatchDiagnostics = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedRuntimeBuildDiagnostics = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedUiDisplayDiagnostics = new(StringComparer.OrdinalIgnoreCase);
        private static string s_LastLoggedActiveSpecialRequestId = "";
        private const int MaxActiveSpecialRequests = 2;
        private const string SecondarySpecialRequestLogItemName = "ExpandedTradingFramework_SecondarySpecialRequestLogItem";
        private static bool s_LoggedExchangeBoxProgressSlotCount;
        private static float s_LastSpecialRequestLimitFeedbackTime = -999f;

        internal static int DefinitionCount => s_Definitions.Count;

        private sealed class StackableRewardAmountOverride
        {
            public TradeExchangeData ExchangeData;
            public CustomTradeRuntime Runtime;
            public int OriginalAmount;
            public int ExpectedUnits;
        }

        private static void DiagnosticLog(string message)
        {
            Core.Diagnostic(message);
        }

        private static string JoinLimited(List<string> values, int limit = 24)
        {
            if (values == null || values.Count <= 0) return "none";

            int safeLimit = Math.Max(1, limit);
            List<string> visible = [];
            int count = Math.Min(values.Count, safeLimit);
            for (int i = 0; i < count; i++) visible.Add(values[i]);

            string suffix = values.Count > safeLimit ? $", ... +{values.Count - safeLimit}" : "";
            return string.Join(", ", visible) + suffix;
        }

        private static void IncrementReason(Dictionary<string, int> reasons, string reason)
        {
            if (reasons == null) return;
            string key = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
            reasons.TryGetValue(key, out int count);
            reasons[key] = count + 1;
        }

        private static string FormatReasonCounts(Dictionary<string, int> reasons)
        {
            if (reasons == null || reasons.Count <= 0) return "none";

            List<string> parts = [];
            foreach (KeyValuePair<string, int> pair in reasons) parts.Add($"{pair.Key}:{pair.Value}");
            return string.Join(", ", parts);
        }

        private static string SafeCostKey(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null) return "unknown-cost";
            return $"{costRuntime.TradeDefinition?.Id ?? "unknown"}:{costRuntime.ReferenceKey ?? "no-ref"}:{costRuntime.Definition?.ExchangeType.ToString() ?? "unknown"}";
        }

        private static string DescribeTextureState(Texture2D icon)
        {
            if (icon == null) return "null";

            try
            {
                return $"{icon.name} ({icon.width}x{icon.height})";
            }
            catch { }

            return "resolved";
        }

        private static string DescribeCostIconPolicy(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return "unknown";
            if (costRuntime.HasCustomIcon) return "custom icon";

            return costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.GearWeight => "gear prefab inventory icon",
                ExchangeItemType.CategoryAmount or ExchangeItemType.CategoryWeight => costRuntime.CategoryIcon switch
                {
                    CategoryIcon.Auto => "first allowed item icon",
                    CategoryIcon.GearItem => $"configured gear icon '{costRuntime.CategoryIconGearName}'",
                    CategoryIcon.AnyHide or CategoryIcon.AnyHarvestPlant or CategoryIcon.AnyFishAnimalMeat or CategoryIcon.AnyFishMeat => $"vanilla category icon '{costRuntime.CategoryIcon}' with fallback",
                    _ => costRuntime.CategoryIcon.ToString(),
                },
                ExchangeItemType.Liquid => "liquid type icon",
                _ => "unknown",
            };
        }

        private static string DescribeCostRequirement(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return "unknown requirement";

            CustomTradeItem cost = costRuntime.Definition;
            string value = cost.ExchangeType switch
            {
                ExchangeItemType.GearAmount => $"amount:{Math.Max(1, cost.Amount)} gear:'{cost.GearName}'",
                ExchangeItemType.GearWeight => $"weight:{Math.Max(0.01f, cost.WeightKilograms):0.###}kg gear:'{cost.GearName}'",
                ExchangeItemType.CategoryAmount => $"amount:{Math.Max(1, cost.Amount)} category:'{costRuntime.DisplayName}' allowed:[{JoinArray(costRuntime.CategoryAllowedGearNames)}]",
                ExchangeItemType.CategoryWeight => $"weight:{Math.Max(0.01f, cost.WeightKilograms):0.###}kg category:'{costRuntime.DisplayName}' allowed:[{JoinArray(costRuntime.CategoryAllowedGearNames)}]",
                ExchangeItemType.Liquid => $"volume:{Math.Max(0.01f, cost.LiquidLiters):0.###}L liquid:{cost.LiquidKind}",
                _ => cost.ExchangeType.ToString(),
            };

            return $"type:{cost.ExchangeType}, {value}, display:'{costRuntime.DisplayName}', ref:'{costRuntime.ReferenceKey}', iconPolicy:{DescribeCostIconPolicy(costRuntime)}, iconAtBuild:{DescribeTextureState(costRuntime.Icon)}";
        }

        private static string JoinArray(string[] values)
        {
            if (values == null || values.Length <= 0) return "none";

            List<string> parts = [];
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) continue;
                parts.Add(values[i]);
            }

            return parts.Count <= 0 ? "none" : string.Join(", ", parts);
        }

        private static void LogRuntimeBuildDiagnostics(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return;
            if (!s_LoggedRuntimeBuildDiagnostics.Add(runtime.Definition.Id)) return;

            CustomTradeDefinition definition = runtime.Definition;
            string kind = definition.IsSpecialRequest ? "special request" : "normal trade";
            string selection = definition.SelectionMode == CustomTradeSelectionMode.AppearanceChance
                ? $"AppearanceChance:{Clamp01(definition.AppearanceChance):0.###}"
                : $"DrawPool:{definition.DrawPool}";
            string callback = definition.OnCompleted != null ? "yes" : "no";
            string rewardMode = runtime.HasPhysicalReward ? $"physical:{DescribeActualReward(runtime)}" : "completion-only:none";

            DiagnosticLog($"Built custom trade '{definition.Id}' -> kind:{kind}, enabled:{definition.Enabled}, selection:{selection}, minTrust:{definition.MinTrust}, maxPerTrade:{Math.Max(1, definition.MaxPerTrade)}, repeatable:{definition.Repeatable}, costs:{runtime.CostItems?.Length ?? 0}, display:{FormatRewardDisplay(runtime.DisplayRewardName, runtime.VisibleDisplayRewardAmount)}, reward:{rewardMode}, stackableReward:{runtime.RewardIsStackable}, onCompleted:{callback}.");

            if (runtime.CostItems == null) return;
            for (int i = 0; i < runtime.CostItems.Length; i++) DiagnosticLog($"Built custom trade '{definition.Id}' requirement #{i} -> {DescribeCostRequirement(runtime.CostItems[i])}.");
        }

        private static void LogCostIconResolutionOnce(CustomTradeCostRuntime costRuntime, string source, Texture2D icon)
        {
            if (costRuntime == null || costRuntime.TradeDefinition == null || costRuntime.Definition == null) return;

            string key = SafeCostKey(costRuntime) + ":" + source + ":" + (icon != null ? "ok" : "null");
            if (!s_LoggedCostIconResolutionDiagnostics.Add(key)) return;

            DiagnosticLog($"Custom trade '{costRuntime.TradeDefinition.Id}' requirement icon -> requirement:'{costRuntime.DisplayName}', type:{costRuntime.Definition.ExchangeType}, source:{source}, icon:{DescribeTextureState(icon)}.");
        }

        private static void LogCostMatchDiagnosticsOnce(CustomTradeCostRuntime costRuntime, GearItem gearItem, bool result)
        {
            if (costRuntime == null || costRuntime.TradeDefinition == null || costRuntime.Definition == null || gearItem == null) return;

            string gearName = GetGearNameForLog(gearItem);
            string key = SafeCostKey(costRuntime) + ":" + gearName + ":" + result;
            if (!s_LoggedCostMatchDiagnostics.Add(key)) return;

            DiagnosticLog($"Custom trade '{costRuntime.TradeDefinition.Id}' requirement match -> requirement:'{costRuntime.DisplayName}', type:{costRuntime.Definition.ExchangeType}, incoming:'{gearName}', result:{result}.");
        }

        internal static void LogTradeDisplayBehavior(CustomTradeRuntime runtime, string surface, string text)
        {
            if (runtime == null || runtime.Definition == null) return;

            string safeSurface = string.IsNullOrWhiteSpace(surface) ? "unknown surface" : surface.Trim();
            string safeText = TrimForLog(text ?? "");
            string key = runtime.Definition.Id + ":" + safeSurface + ":" + safeText;
            if (!s_LoggedUiDisplayDiagnostics.Add(key)) return;

            DiagnosticLog($"Custom trade '{runtime.Definition.Id}' UI display -> surface:{safeSurface}, text:'{safeText}'.");
        }

        private static void LogCostDisplayBehavior(CustomTradeCostRuntime costRuntime, string surface, string text)
        {
            if (costRuntime == null || costRuntime.TradeDefinition == null || costRuntime.Definition == null) return;

            string safeSurface = string.IsNullOrWhiteSpace(surface) ? "unknown surface" : surface.Trim();
            string safeText = TrimForLog(text ?? "");
            string key = SafeCostKey(costRuntime) + ":" + safeSurface + ":" + safeText;
            if (!s_LoggedUiDisplayDiagnostics.Add(key)) return;

            DiagnosticLog($"Custom trade '{costRuntime.TradeDefinition.Id}' requirement display -> requirement:'{costRuntime.DisplayName}', type:{costRuntime.Definition.ExchangeType}, surface:{safeSurface}, text:'{safeText}'.");
        }

        private static string GetGearNameForLog(GearItem gearItem)
        {
            if (gearItem == null) return "null";

            try
            {
                if (!string.IsNullOrEmpty(gearItem.name)) return gearItem.name;
            }
            catch { }

            try
            {
                GearItemData data = gearItem.GearItemData;
                if (data != null && !string.IsNullOrEmpty(data.name)) return data.name;
            }
            catch { }

            return "unknown gear";
        }

        private static string DescribeSaveDataState()
        {
            if (s_SaveData == null) return "save data:null";
            int completedNonRepeatable = s_SaveData.CompletedNonRepeatableTradeIds?.Count ?? 0;
            int selectedNormal = s_SaveData.SelectedNormalTradeIds?.Count ?? 0;
            int completionCounts = s_SaveData.CompletionCounts?.Count ?? 0;
            int appearanceRolls = s_SaveData.AppearanceRolls?.Count ?? 0;
            string normalInventorySignature = string.IsNullOrEmpty(s_SaveData.LastNormalVanillaInventorySignature) ? "none" : "set";
            return $"completedNonRepeatable:{completedNonRepeatable}, selectedNormal:{selectedNormal}, completionCounts:{completionCounts}, appearanceRolls:{appearanceRolls}, normalInventorySignature:{normalInventorySignature}, active:'{s_SaveData.ActiveCustomTradeId}', multiplier:{s_SaveData.ActiveCustomTradeMultiplier}";
        }

        internal static void ResetSaveDataForMainMenu()
        {
            s_SaveData = new ExpandedTradingSaveDataProxy();
            s_SaveDataLoaded = false;
            s_LastLoggedActiveSpecialRequestId = "";
            DiagnosticLog("Reset framework save-data cache after returning to main menu.");
        }

        internal static void SaveFrameworkState(string source)
        {
            if (!s_SaveDataLoaded) return;
            NormalizeSaveData();
            s_SaveDataManager.Save(s_SaveData);
            DiagnosticLog($"Saved framework state from {source}. {DescribeSaveDataState()}.");
        }

        private static void EnsureSaveDataLoaded(string source)
        {
            if (s_SaveDataLoaded) return;

            s_SaveData = s_SaveDataManager.Load();
            NormalizeSaveData();
            s_SaveDataLoaded = true;
            DiagnosticLog($"Loaded framework state from {source}. {DescribeSaveDataState()}.");
        }

        private static void NormalizeSaveData()
        {
            s_SaveData ??= new ExpandedTradingSaveDataProxy();
            s_SaveData.CompletedNonRepeatableTradeIds ??= [];
            s_SaveData.SelectedNormalTradeIds ??= [];
            s_SaveData.CompletionCounts = CopyDictionary(s_SaveData.CompletionCounts);
            s_SaveData.AppearanceRolls = CopyDictionary(s_SaveData.AppearanceRolls);
            s_SaveData.ActiveCustomTradeMultiplier = Math.Max(1, s_SaveData.ActiveCustomTradeMultiplier);
            s_SaveData.ActiveCustomTradeId ??= "";
            s_SaveData.LastNormalVanillaInventorySignature ??= "";
        }

        private static Dictionary<string, T> CopyDictionary<T>(Dictionary<string, T> source)
        {
            Dictionary<string, T> result = new(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;

            foreach (KeyValuePair<string, T> pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                result[pair.Key.Trim()] = pair.Value;
            }

            return result;
        }

        private static bool ContainsString(List<string> values, string value)
        {
            if (values == null || string.IsNullOrEmpty(value)) return false;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        internal static int GetCompletionCount(string tradeId)
        {
            if (string.IsNullOrEmpty(tradeId)) return 0;
            EnsureSaveDataLoaded("completion query");
            return s_SaveData.CompletionCounts.TryGetValue(tradeId, out int count) ? Math.Max(0, count) : 0;
        }

        internal static int RememberTradeSelectionMultiplier(TradeExchangeData tradeData, int multiplier)
        {
            if (tradeData == null) return Math.Max(1, multiplier);
            if (!TryGetTrade(tradeData, out CustomTradeRuntime runtime)) return Math.Max(1, multiplier);

            int safeMultiplier = ClampTradeMultiplier(runtime, multiplier);
            string id = runtime.Definition?.Id ?? "";
            if (!string.IsNullOrEmpty(id)) s_LastObservedTradeMultipliers[id] = safeMultiplier;

            return safeMultiplier;
        }

        internal static int GetRememberedTradeSelectionMultiplier(TradeExchangeData tradeData, CustomTradeRuntime runtime)
        {
            if (runtime == null && tradeData != null) TryGetTrade(tradeData, out runtime);
            if (runtime?.Definition == null) return 1;

            int multiplier = 1;
            string id = runtime.Definition.Id ?? "";

            if (!string.IsNullOrEmpty(id) && s_LastObservedTradeMultipliers.TryGetValue(id, out int observedMultiplier)) multiplier = Math.Max(multiplier, observedMultiplier);

            try
            {
                if (!string.IsNullOrEmpty(id) && s_SaveDataLoaded && string.Equals(s_SaveData.ActiveCustomTradeId, id, StringComparison.OrdinalIgnoreCase)) multiplier = Math.Max(multiplier, s_SaveData.ActiveCustomTradeMultiplier);
            }
            catch { }

            multiplier = Math.Max(multiplier, GetActiveTradeMultiplierFromData(runtime, tradeData));
            return ClampTradeMultiplier(runtime, multiplier);
        }

        private static int ClampTradeMultiplier(CustomTradeRuntime runtime, int multiplier)
        {
            int safeMultiplier = Math.Max(1, multiplier);
            int maxPerTrade = runtime?.Definition != null ? Math.Max(1, runtime.Definition.MaxPerTrade) : safeMultiplier;
            return Math.Min(safeMultiplier, maxPerTrade);
        }

        public static bool RegisterExternalTrade(CustomTradeDefinition definition)
        {
            if (!TryValidateDefinitionForRegistration(definition, out string reason))
            {
                Core.Log?.Warning($"Rejected external custom trade registration: {reason}");
                return false;
            }

            string id = definition.Id.Trim();
            definition.Id = id;

            if (ContainsDefinitionId(s_ExternalDefinitions, id) || s_RuntimeTradesById.ContainsKey(id))
            {
                Core.Log?.Warning($"Rejected external custom trade registration for duplicate id '{id}'. Trade ids must be unique.");
                return false;
            }

            s_ExternalDefinitions.Add(definition);
            s_Definitions.Add(definition);

            if (s_Built && !TryBuildAndAddRuntime(definition, true))
            {
                s_ExternalDefinitions.Remove(definition);
                s_Definitions.Remove(definition);
                return false;
            }

            if (s_Built && s_RuntimeTradesById.TryGetValue(id, out CustomTradeRuntime registeredRuntime) && registeredRuntime.IsNormal) InvalidateNormalTradeSelectionCache($"external custom trade registration '{id}'", true, false);

            DiagnosticLog($"Registered external custom trade '{id}'.");
            return true;
        }

        public static int RegisterExternalTrades(IEnumerable<CustomTradeDefinition> definitions)
        {
            if (definitions == null) return 0;

            int total = 0;
            int registered = 0;
            foreach (CustomTradeDefinition definition in definitions)
            {
                total++;
                if (RegisterExternalTrade(definition)) registered++;
            }

            Core.Log?.Msg($"Registered {registered}/{total} external custom trade definition(s).");
            return registered;
        }

        private static bool TryValidateDefinitionForRegistration(CustomTradeDefinition definition, out string reason)
        {
            if (definition == null)
            {
                reason = "definition is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                reason = "definition id is empty.";
                return false;
            }

            reason = "";
            return true;
        }

        private static bool ContainsDefinitionId(List<CustomTradeDefinition> definitions, string id)
        {
            if (definitions == null || string.IsNullOrEmpty(id)) return false;

            for (int i = 0; i < definitions.Count; i++)
            {
                CustomTradeDefinition definition = definitions[i];
                if (definition == null) continue;
                if (string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        internal static Il2CppTradeExchangeDataList BuildNormalTradeResultCopy(Il2CppTradeExchangeDataList source, TraderManager traderManager)
        {
            EnsureBuilt();
            CleanupStaleSpecialRequests(traderManager);

            Il2CppTradeExchangeDataList result = BuildCleanTradeResultCopy(source, false);
            SynchronizeNormalSelectionWithVanillaInventory(result, "TraderManager.GetAvailableTradeExchanges");
            AppendTrades(result, traderManager, false);
            SyncEligibleSpecialRequestsIntoState(traderManager);
            return result;
        }

        internal static Il2CppTradeExchangeDataList BuildSpecialRequestResultCopy(Il2CppTradeExchangeDataList source, TraderManager traderManager)
        {
            EnsureBuilt();

            Il2CppTradeExchangeDataList result = BuildCleanTradeResultCopy(source, true);
            CleanupStaleSpecialRequests(traderManager, result);
            AppendTrades(result, traderManager, true);
            return result;
        }

        private static Il2CppTradeExchangeDataList BuildCleanTradeResultCopy(Il2CppTradeExchangeDataList source, bool specialRequests)
        {
            Il2CppTradeExchangeDataList result = new();
            if (source == null) return result;

            int removedCustomEntries = 0;
            for (int i = 0; i < source.Count; i++)
            {
                TradeExchangeData trade = source[i];
                if (trade == null) continue;

                if (IsKnownOrLikelyCustomTrade(trade))
                {
                    removedCustomEntries++;
                    continue;
                }

                result.Add(trade);
            }

            if (removedCustomEntries > 0) DiagnosticLog($"Dropped {removedCustomEntries} previously injected ETF {(specialRequests ? "special request" : "normal trade")} entr{(removedCustomEntries == 1 ? "y" : "ies")} from the returned Trader list before appending a fresh selection.");
            return result;
        }

        private static void SynchronizeNormalSelectionWithVanillaInventory(Il2CppTradeExchangeDataList cleanVanillaTrades, string source)
        {
            EnsureSaveDataLoaded("normal vanilla inventory sync");

            string currentSignature = BuildNormalVanillaInventorySignature(cleanVanillaTrades);
            if (string.IsNullOrEmpty(currentSignature))
            {
                DiagnosticLog($"Normal trade selection cache signature sync skipped from {source}: vanilla normal trade signature is empty.");
                return;
            }

            string previousSignature = s_SaveData.LastNormalVanillaInventorySignature ?? "";
            if (string.IsNullOrEmpty(previousSignature))
            {
                s_SaveData.LastNormalVanillaInventorySignature = currentSignature;
                DiagnosticLog($"Normal trade selection cache signature initialized from {source}: {DescribeNormalInventorySignature(currentSignature)}.");
                return;
            }

            if (string.Equals(previousSignature, currentSignature, StringComparison.Ordinal))
            {
                DiagnosticLog($"Normal trade selection cache signature unchanged from {source}: {DescribeNormalInventorySignature(currentSignature)}.");
                return;
            }

            int cleared = s_SaveData.SelectedNormalTradeIds?.Count ?? 0;
            s_SaveData.SelectedNormalTradeIds?.Clear();
            s_SaveData.LastNormalVanillaInventorySignature = currentSignature;
            DiagnosticLog($"Normal trade selection cache invalidated from {source}: vanilla normal trade signature changed. Cleared cached custom draw-pool selection:{cleared}. Previous:{DescribeNormalInventorySignature(previousSignature)}. Current:{DescribeNormalInventorySignature(currentSignature)}.");
        }

        private static string BuildNormalVanillaInventorySignature(Il2CppTradeExchangeDataList cleanVanillaTrades)
        {
            if (cleanVanillaTrades == null || cleanVanillaTrades.Count <= 0) return "";

            List<string> ids = [];
            for (int i = 0; i < cleanVanillaTrades.Count; i++)
            {
                string id = GetTradeId(cleanVanillaTrades[i]);
                if (string.IsNullOrWhiteSpace(id)) id = $"<unnamed:{i}>";
                ids.Add(id.Trim());
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", ids);
        }

        private static string DescribeNormalInventorySignature(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return "none";

            string[] ids = signature.Split('|', StringSplitOptions.RemoveEmptyEntries);
            return $"count:{ids.Length}, ids:[{JoinLimited(ids.ToList(), 18)}]";
        }

        private static void InvalidateNormalTradeSelectionCache(string source, bool clearVanillaSignature, bool save)
        {
            EnsureSaveDataLoaded(source);

            int selectedCount = s_SaveData.SelectedNormalTradeIds?.Count ?? 0;
            bool hadSignature = !string.IsNullOrEmpty(s_SaveData.LastNormalVanillaInventorySignature);
            if (selectedCount <= 0 && (!clearVanillaSignature || !hadSignature))
            {
                DiagnosticLog($"Normal trade selection cache invalidation from {source} had nothing to clear. clearVanillaSignature:{clearVanillaSignature}.");
                return;
            }

            s_SaveData.SelectedNormalTradeIds?.Clear();
            if (clearVanillaSignature) s_SaveData.LastNormalVanillaInventorySignature = "";

            DiagnosticLog($"Normal trade selection cache invalidated from {source}. Cleared cached custom draw-pool selection:{selectedCount}. Cleared vanilla signature:{clearVanillaSignature && hadSignature}.");
            if (save) SaveFrameworkState(source);
        }

        private static bool ContainsNormalTradeExchange(ActiveTradeState activeTrade)
        {
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;

            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                TradeExchangeData exchange = exchangeState?.m_Exchange;
                if (exchange == null) continue;
                if (!IsSpecialRequestTradeData(exchange)) return true;
            }

            return false;
        }

        internal static bool TryGetTrade(TradeExchangeData tradeData, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (tradeData == null) return false;

            string id = tradeData.m_TradeExchangeName;
            if (string.IsNullOrEmpty(id)) return false;
            return s_RuntimeTradesById.TryGetValue(id, out runtime) && runtime != null;
        }

        internal static GearItemData GetGainedItemDataForTradeExchange(TradeExchangeData tradeData, CustomTradeRuntime runtime)
        {
            if (runtime == null) return null;
            if (runtime.HasPhysicalReward && runtime.RewardGearItemData != null && IsReferenceKey(tradeData?.m_GainedItem, runtime.RewardReferenceKey)) return runtime.RewardGearItemData;
            return runtime.DisplayRewardGearItemData;
        }

        internal static bool TryGetCost(ExchangeItem exchangeItem, out CustomTradeCostRuntime costRuntime)
        {
            costRuntime = null;
            if (exchangeItem == null) return false;

            for (int i = 0; i < s_RuntimeTrades.Count; i++)
            {
                CustomTradeRuntime runtime = s_RuntimeTrades[i];
                if (runtime == null || runtime.CostItems == null) continue;

                for (int j = 0; j < runtime.CostItems.Length; j++)
                {
                    CustomTradeCostRuntime candidate = runtime.CostItems[j];
                    if (candidate == null || candidate.Definition == null) continue;
                    if (!IsKnownCostReference(exchangeItem, candidate)) continue;

                    costRuntime = candidate;
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsCustomExchange(ActiveTradeState activeTrade)
        {
            return TryGetActiveTrade(activeTrade, out _);
        }

        internal static bool HasAnyPhysicalReward(ActiveTradeState activeTrade)
        {
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;

            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                if (exchangeState == null) continue;
                if (!TryGetTrade(exchangeState.m_Exchange, out CustomTradeRuntime runtime)) continue;
                if (runtime.HasPhysicalReward) return true;
            }

            return false;
        }

        internal static bool TryGetActiveTrade(ActiveTradeState activeTrade, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;

            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                if (exchangeState == null) continue;
                if (!TryGetTrade(exchangeState.m_Exchange, out runtime)) continue;
                return true;
            }

            return false;
        }

        internal static void ResetRewardPreparationStateForSaveRescue()
        {
            s_RewardPreparationDepth = 0;
        }

        internal static bool CleanupStaleSpecialRequests(TraderManager traderManager, Il2CppTradeExchangeDataList visibleResult = null)
        {
            bool removed = false;

            try
            {
                EnsureBuilt();
                bool vanillaTradeIdsAvailable = TryBuildVanillaTradeIdSet(traderManager, out HashSet<string> vanillaTradeIds);

                TraderState state = traderManager?.m_CurrentState;
                if (state?.m_SpecialRequests != null) removed |= RemoveStaleSpecialRequests(state.m_SpecialRequests, vanillaTradeIds, vanillaTradeIdsAvailable, "TraderState.m_SpecialRequests");
                if (visibleResult != null) removed |= RemoveStaleSpecialRequests(visibleResult, vanillaTradeIds, vanillaTradeIdsAvailable, "TraderManager.GetSpecialRequests result");

                if (!HasCurrentCustomSpecialRequestDefinition()) removed |= ClearStaleCustomSpecialRequestTextState(state);

                if (removed) s_LastLoggedActiveSpecialRequestId = "";
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not clean stale custom special requests: {e.Message}");
            }

            return removed;
        }

        internal static void ClearSpecialRequestStateForSaveRescue(TraderState state, string source)
        {
            if (state == null) return;

            try
            {
                int previousSpecialRequestCount = state.m_SpecialRequests != null ? state.m_SpecialRequests.Count : 0;
                string previousHeader = state.m_SpecialRequestHeaderLocID ?? "";
                string previousDescription = state.m_SpecialRequestDescriptionLocID ?? "";

                int removed = state.m_SpecialRequests != null ? RemoveCurrentAndLegacyCustomSpecialRequests(state.m_SpecialRequests, $"save rescue / {source}") : 0;
                if (LooksLikeCustomSpecialRequestText(state.m_SpecialRequestHeaderLocID)) state.m_SpecialRequestHeaderLocID = "";
                if (LooksLikeCustomSpecialRequestText(state.m_SpecialRequestDescriptionLocID)) state.m_SpecialRequestDescriptionLocID = "";
                s_LastLoggedActiveSpecialRequestId = "";

                Core.Log?.Msg($"Emergency Trader special request cleanup executed from {source}. Removed ETF entries:{removed}. Previous special request count:{previousSpecialRequestCount}, previous header:'{TrimForLog(previousHeader)}', previous description:'{TrimForLog(previousDescription)}'. Force a normal game save after this, then reload the save.");
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Emergency Trader special request state clear failed from {source}: {e.Message}");
            }
        }

        internal static bool HasAnySpecialRequestInState(TraderManager traderManager)
        {
            try
            {
                TraderState state = traderManager?.m_CurrentState;
                return state?.m_SpecialRequests != null && state.m_SpecialRequests.Count > 0;
            }
            catch { }

            return false;
        }

        private static bool RemoveStaleSpecialRequests(Il2CppTradeExchangeDataList trades, HashSet<string> vanillaTradeIds, bool vanillaTradeIdsAvailable, string context)
        {
            if (trades == null) return false;

            bool removed = false;
            for (int i = trades.Count - 1; i >= 0; i--)
            {
                TradeExchangeData trade = trades[i];
                string id = GetTradeId(trade);
                if (string.IsNullOrEmpty(id)) continue;

                if (IsCurrentCustomSpecialRequestId(id))
                {
                    EnsureTradeDataRuntimeReferences(trade, $"current special-request cleanup keep / {context}");
                    continue;
                }

                if (!LooksLikeCustomTradeId(id)) continue;

                trades.RemoveAt(i);
                removed = true;
                Core.Log?.Msg($"Removed stale ETF special request from {context}: '{id}' is no longer present in current Expanded Trading Framework definitions.");
            }

            return removed;
        }

        private static int RemoveCurrentAndLegacyCustomSpecialRequests(Il2CppTradeExchangeDataList trades, string context)
        {
            if (trades == null) return 0;

            int removed = 0;
            for (int i = trades.Count - 1; i >= 0; i--)
            {
                string id = GetTradeId(trades[i]);
                if (string.IsNullOrEmpty(id)) continue;
                if (!IsCurrentCustomSpecialRequestId(id) && !LooksLikeCustomTradeId(id)) continue;

                trades.RemoveAt(i);
                removed++;
                Core.Log?.Msg($"Removed ETF special request from {context}: '{id}'.");
            }

            return removed;
        }

        private static bool IsCurrentCustomSpecialRequestId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!s_RuntimeTradesById.TryGetValue(id, out CustomTradeRuntime runtime)) return false;
            return runtime != null && runtime.IsSpecialRequest;
        }

        private static bool HasCurrentCustomSpecialRequestDefinition()
        {
            EnsureBuilt();

            for (int i = 0; i < s_RuntimeTrades.Count; i++)
            {
                CustomTradeRuntime runtime = s_RuntimeTrades[i];
                if (runtime?.Definition?.IsSpecialRequest == true) return true;
            }

            return false;
        }

        private static bool ClearStaleCustomSpecialRequestTextState(TraderState state)
        {
            if (state == null) return false;

            string header = state.m_SpecialRequestHeaderLocID ?? "";
            string description = state.m_SpecialRequestDescriptionLocID ?? "";
            if (string.IsNullOrWhiteSpace(header) && string.IsNullOrWhiteSpace(description)) return false;
            if (!LooksLikeCustomSpecialRequestText(header) && !LooksLikeCustomSpecialRequestText(description)) return false;

            state.m_SpecialRequestHeaderLocID = "";
            state.m_SpecialRequestDescriptionLocID = "";
            Core.Log?.Msg($"Cleared stale custom special request text from TraderState: header:'{TrimForLog(header)}', description:'{TrimForLog(description)}'.");
            return true;
        }

        private static bool ClearCurrentCustomSpecialRequestTextState(TraderState state)
        {
            if (state == null) return false;

            bool cleared = false;
            if (LooksLikeCurrentCustomSpecialRequestText(state.m_SpecialRequestHeaderLocID))
            {
                state.m_SpecialRequestHeaderLocID = "";
                cleared = true;
            }

            if (LooksLikeCurrentCustomSpecialRequestText(state.m_SpecialRequestDescriptionLocID))
            {
                state.m_SpecialRequestDescriptionLocID = "";
                cleared = true;
            }

            if (cleared) Core.Log?.Msg("Cleared ETF special request text from TraderState before save.");
            return cleared;
        }

        private static bool LooksLikeCurrentCustomSpecialRequestText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (LooksLikeCustomSpecialRequestText(text)) return true;

            EnsureBuilt();
            for (int i = 0; i < s_RuntimeTrades.Count; i++)
            {
                CustomTradeRuntime runtime = s_RuntimeTrades[i];
                if (runtime == null || !runtime.IsSpecialRequest) continue;
                if (string.Equals(text, GetSpecialRequestHeaderText(runtime), StringComparison.Ordinal)) return true;
                if (string.Equals(text, GetSpecialRequestDescriptionText(runtime), StringComparison.Ordinal)) return true;
                if (string.Equals(text, GetSpecialRequestRequirementText(runtime), StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static bool LooksLikeCustomSpecialRequestText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (LooksLikeLocalizationKey(trimmed)) return false;
            if (trimmed.StartsWith("ExpandedTradingFramework", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("ExpandedTraderFramework", StringComparison.OrdinalIgnoreCase)) return true; // Legacy intermediate rename cleanup
            if (trimmed.StartsWith("CustomTrades", StringComparison.OrdinalIgnoreCase)) return true; // Legacy pre-rename state cleanup
            return false;
        }

        private static bool LooksLikeLocalizationKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains(" ", StringComparison.Ordinal)) return false;

            bool hasUnderscore = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '_')
                {
                    hasUnderscore = true;
                    continue;
                }

                if (char.IsLetterOrDigit(c)) continue;
                return false;
            }

            return hasUnderscore;
        }

        private static string TrimForLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string trimmed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return trimmed.Length <= 120 ? trimmed : trimmed.Substring(0, 117) + "...";
        }

        private static bool TryBuildVanillaTradeIdSet(TraderManager traderManager, out HashSet<string> ids)
        {
            ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Il2CppSystem.Collections.Generic.List<TradeExchange> exchanges = traderManager?.GetPossibleExchanges();
                if (exchanges == null || exchanges.Count <= 0) return false;

                for (int i = 0; i < exchanges.Count; i++)
                {
                    TradeExchange exchange = exchanges[i];
                    if (exchange == null) continue;

                    AddTradeIds(ids, exchange.m_PossibleExchanges);
                    AddTradeIds(ids, exchange.m_FilteredExchanges);
                }

                return ids.Count > 0;
            }
            catch { }

            return false;
        }

        private static bool LooksLikeCustomTradeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.StartsWith("ExpandedTradingFramework", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("ExpandedTraderFramework", StringComparison.OrdinalIgnoreCase) // Legacy intermediate rename cleanup
                || id.StartsWith("CustomTrades", StringComparison.OrdinalIgnoreCase); // Legacy pre-rename state cleanup
        }

        private static void AddTradeIds(HashSet<string> ids, Il2CppTradeExchangeDataList trades)
        {
            if (ids == null || trades == null) return;

            for (int i = 0; i < trades.Count; i++)
            {
                string id = GetTradeId(trades[i]);
                if (string.IsNullOrEmpty(id)) continue;
                ids.Add(id);
            }
        }

        internal static void PrepareTraderStateBeforeDeserialize(TraderState state)
        {
            EnsureSaveDataLoaded("trader deserialize");
            SanitizeTraderState(state, "load");
        }

        internal static void PrepareTraderStateBeforeSave(TraderManager traderManager)
        {
            EnsureSaveDataLoaded("save");
            TraderState state = null;
            bool? tradeIsActive = null;
            try
            {
                state = traderManager?.m_CurrentState;
                if (traderManager != null) tradeIsActive = traderManager.TradeIsActive;
            }
            catch { }

            SanitizeTraderState(state, "save", tradeIsActive);
        }

        private static void SanitizeTraderState(TraderState state, string context, bool? traderManagerTradeIsActive = null)
        {
            if (state == null) return;

            try
            {
                EnsureBuilt();
                SanitizeActiveTradeState(state, context, traderManagerTradeIsActive);
                if (state.m_SpecialRequests != null && string.Equals(context, "save", StringComparison.OrdinalIgnoreCase))
                {
                    int removedForSave = RemoveCurrentAndLegacyCustomSpecialRequests(state.m_SpecialRequests, $"TraderState.m_SpecialRequests / {context}");
                    if (removedForSave > 0) ClearCurrentCustomSpecialRequestTextState(state);
                }
                else if (state.m_SpecialRequests != null)
                {
                    RemoveStaleSpecialRequests(state.m_SpecialRequests, null, false, $"TraderState.m_SpecialRequests / {context}");
                    ClearStaleCustomSpecialRequestTextState(state);
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not sanitize TraderState during {context}: {e.Message}");
            }
        }

        private static void SanitizeActiveTradeState(TraderState state, string context, bool? traderManagerTradeIsActive = null)
        {
            ActiveTradeState activeTrade = state?.m_ActiveTrade;
            if (activeTrade == null || activeTrade.m_ExchangeStates == null || activeTrade.m_ExchangeStates.Count <= 0)
            {
                ClearSavedActiveCustomTradeState($"no vanilla active trade data during {context}", false);
                return;
            }

            bool cleared = false;
            string activeCustomId = "";
            int activeMultiplier = 1;
            bool hasRuntimeCustomTrade = false;

            for (int i = activeTrade.m_ExchangeStates.Count - 1; i >= 0; i--)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                string id = GetTradeId(exchangeState?.m_Exchange);
                if (string.IsNullOrEmpty(id)) continue;

                if (TryGetRuntimeById(id, out CustomTradeRuntime runtime))
                {
                    hasRuntimeCustomTrade = true;
                    exchangeState.m_Exchange ??= runtime.TradeData;
                    int selectedMultiplier = GetRememberedTradeSelectionMultiplier(exchangeState.m_Exchange, runtime);
                    CustomTradeFactory.RewriteActiveReferencesPreservingActiveValues(runtime, exchangeState.m_Exchange, selectedMultiplier);

                    if (string.IsNullOrEmpty(activeCustomId))
                    {
                        activeCustomId = runtime.Definition.Id;
                        activeMultiplier = GetActiveTradeMultiplier(runtime, exchangeState.m_Exchange);
                    }
                    continue;
                }

                bool wasSavedAsCustomActiveTrade = !string.IsNullOrEmpty(s_SaveData.ActiveCustomTradeId) && string.Equals(s_SaveData.ActiveCustomTradeId, id, StringComparison.OrdinalIgnoreCase);
                if (!LooksLikeCustomTradeId(id) && !wasSavedAsCustomActiveTrade) continue;

                ClearActiveTradeState(state);
                cleared = true;
                Core.Log?.Warning($"Closed stale custom Trader active trade '{id}' during {context}. The current ETF runtime no longer owns this trade id, so keeping it in the save would be unsafe.");
                break;
            }

            if (cleared)
            {
                ClearSavedActiveCustomTradeState($"stale runtime id during {context}", false);
                return;
            }

            if (!hasRuntimeCustomTrade)
            {
                ClearSavedActiveCustomTradeState($"active vanilla trade has no custom exchanges during {context}", false);
                return;
            }

            bool vanillaSaysInactive = traderManagerTradeIsActive == false;
            bool vanillaStateIsCompleted = activeTrade.m_TradeState == ActiveTradeState.State.Completed;

            if (vanillaSaysInactive || vanillaStateIsCompleted)
            {
                ClearSavedActiveCustomTradeState($"vanilla trade is not active during {context}", false);
                return;
            }

            if (!string.IsNullOrEmpty(activeCustomId))
            {
                s_SaveData.ActiveCustomTradeId = activeCustomId;
                s_SaveData.ActiveCustomTradeMultiplier = Math.Max(1, activeMultiplier);
            }
        }

        private static void ClearSavedActiveCustomTradeState(string reason, bool logWhenEmpty)
        {
            string previousId = s_SaveData?.ActiveCustomTradeId ?? "";
            int previousMultiplier = s_SaveData?.ActiveCustomTradeMultiplier ?? 1;
            if (string.IsNullOrEmpty(previousId) && previousMultiplier <= 1)
            {
                if (logWhenEmpty) DiagnosticLog($"No saved active custom trade state to clear: {reason}.");
                return;
            }

            s_SaveData.ActiveCustomTradeId = "";
            s_SaveData.ActiveCustomTradeMultiplier = 1;
            Core.Log?.Msg($"Cleared saved active custom trade state '{previousId}' x{Math.Max(1, previousMultiplier)}: {reason}.");
        }

        private static void ClearActiveTradeState(TraderState state)
        {
            if (state == null) return;

            state.m_ActiveTrade = new ActiveTradeState
            {
                m_ExchangeBoxScene = "",
                m_TradeState = ActiveTradeState.State.Closed,
                m_ExchangeStates = new Il2CppSystem.Collections.Generic.List<ActiveTradeState.ActiveExchangeState>()
            };
        }

        private static int GetActiveTradeMultiplier(CustomTradeRuntime runtime, TradeExchangeData tradeData)
        {
            return GetRememberedTradeSelectionMultiplier(tradeData, runtime);
        }

        private static int GetActiveTradeMultiplierFromData(CustomTradeRuntime runtime, TradeExchangeData tradeData)
        {
            if (runtime == null || tradeData == null) return 1;

            int multiplier = 1;
            multiplier = Math.Max(multiplier, GetRewardMultiplier(runtime, tradeData));
            multiplier = Math.Max(multiplier, GetAmountMultiplier(runtime, tradeData.m_ExchangeItems));
            multiplier = Math.Max(multiplier, GetAmountMultiplier(runtime, tradeData.m_CheapExchangeItems));
            return Math.Max(1, multiplier);
        }

        private static int GetRewardMultiplier(CustomTradeRuntime runtime, TradeExchangeData tradeData)
        {
            if (runtime == null || tradeData == null) return 1;
            int gainedAmount = Math.Max(0, tradeData.m_GainedItemAmount);
            if (gainedAmount <= 0) return 1;

            int multiplier = 1;
            if (runtime.HasPhysicalReward && runtime.Definition?.Reward != null) multiplier = Math.Max(multiplier, RoundMultiplier(gainedAmount, Math.Max(1, runtime.Definition.Reward.Amount)));
            multiplier = Math.Max(multiplier, RoundMultiplier(gainedAmount, Math.Max(1, runtime.DisplayRewardAmount)));
            multiplier = Math.Max(multiplier, RoundMultiplier(gainedAmount, Math.Max(1, runtime.VisibleRewardAmount)));
            multiplier = Math.Max(multiplier, RoundMultiplier(gainedAmount, Math.Max(1, runtime.VisibleDisplayRewardAmount)));
            return Math.Max(1, multiplier);
        }

        private static int GetAmountMultiplier(CustomTradeRuntime runtime, Il2CppSystem.Collections.Generic.List<ExchangeItem> exchangeItems)
        {
            if (runtime == null || runtime.CostItems == null || exchangeItems == null) return 1;

            int count = Math.Min(runtime.CostItems.Length, exchangeItems.Count);
            int multiplier = 1;
            for (int i = 0; i < count; i++)
            {
                CustomTradeCostRuntime costRuntime = runtime.CostItems[i];
                ExchangeItem exchangeItem = exchangeItems[i];
                if (costRuntime?.Definition == null || exchangeItem == null) continue;

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

        internal static void RehydrateActiveTradeState(ActiveTradeState activeTrade, string context)
        {
            if (activeTrade == null) return;

            try
            {
                EnsureBuilt();
                int repaired = RepairActiveTradeReferences(activeTrade);
                if (repaired > 0) DiagnosticLog($"Rehydrated {repaired} active custom trade exchange(s) during {context}.");
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not rehydrate active custom trade during {context}: {e.Message}");
            }
        }

        internal static bool EnsureTradeDataRuntimeReferences(TradeExchangeData tradeData, string context)
        {
            if (tradeData == null) return false;

            string id = GetTradeId(tradeData);
            if (string.IsNullOrEmpty(id)) return false;
            if (!TryGetRuntimeById(id, out CustomTradeRuntime runtime)) return false;

            try
            {
                CustomTradeFactory.RewriteReferencesPreservingActiveValues(runtime, tradeData);
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not rehydrate custom trade '{id}' during {context}: {e.Message}");
                return false;
            }
        }

        private static int RepairActiveTradeReferences(ActiveTradeState activeTrade)
        {
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return 0;

            int repaired = 0;
            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                if (exchangeState == null) continue;

                string id = GetTradeId(exchangeState.m_Exchange);
                if (string.IsNullOrEmpty(id)) continue;
                if (!TryGetRuntimeById(id, out CustomTradeRuntime runtime)) continue;

                exchangeState.m_Exchange ??= runtime.TradeData;
                int selectedMultiplier = GetRememberedTradeSelectionMultiplier(exchangeState.m_Exchange, runtime);
                CustomTradeFactory.RewriteActiveReferencesPreservingActiveValues(runtime, exchangeState.m_Exchange, selectedMultiplier);
                repaired++;
            }

            return repaired;
        }

        private static bool TryGetRuntimeById(string id, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (string.IsNullOrEmpty(id)) return false;

            EnsureBuilt();
            return s_RuntimeTradesById.TryGetValue(id, out runtime) && runtime != null;
        }

        private static bool IsKnownOrLikelyCustomTrade(TradeExchangeData tradeData)
        {
            string id = GetTradeId(tradeData);
            if (string.IsNullOrEmpty(id)) return false;

            if (s_RuntimeTradesById.ContainsKey(id)) return true;

            for (int i = 0; i < s_Definitions.Count; i++)
            {
                CustomTradeDefinition definition = s_Definitions[i];
                if (definition == null) continue;
                if (string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return id.StartsWith("ExpandedTradingFramework_", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("ExpandedTraderFramework_", StringComparison.OrdinalIgnoreCase) // Legacy intermediate rename cleanup
                || id.StartsWith("CustomTrades_", StringComparison.OrdinalIgnoreCase); // Legacy pre-rename state cleanup
        }

        private static string GetTradeId(TradeExchangeData tradeData)
        {
            if (tradeData == null) return "";

            try
            {
                return tradeData.m_TradeExchangeName ?? "";
            }
            catch
            {
                return "";
            }
        }

        internal static bool HasEligibleSpecialRequests(TraderManager traderManager)
        {
            return SyncEligibleSpecialRequestsIntoState(traderManager) > 0;
        }

        internal static bool TryGetSpecialRequestHeaderOverride(TraderManager traderManager, string currentHeaderText, out string text)
        {
            text = "";

            if (!HasActiveVanillaSpecialRequest(traderManager) && TryGetActiveSpecialRequestRuntime(out CustomTradeRuntime activeRuntime))
            {
                text = GetSpecialRequestHeaderText(activeRuntime);
                return true;
            }

            List<CustomTradeRuntime> eligible = GetEligibleSpecialRequestRuntimes(traderManager);
            SyncSpecialRequestsIntoState(traderManager, eligible);
            if (eligible.Count <= 0) return false;

            if (!string.IsNullOrEmpty(currentHeaderText) && !LooksLikeCurrentCustomSpecialRequestText(currentHeaderText)) return false;
            if (HasActiveVanillaSpecialRequest(traderManager)) return false;

            text = eligible.Count == 1 ? GetSpecialRequestHeaderText(eligible[0]) : "Special Requests";
            return true;
        }

        internal static bool TryGetSpecialRequestDescriptionOverride(TraderManager traderManager, string currentDescriptionText, out string text)
        {
            text = "";

            if (!HasActiveVanillaSpecialRequest(traderManager) && TryGetActiveSpecialRequestRuntime(out CustomTradeRuntime activeRuntime))
            {
                text = GetSpecialRequestDescriptionText(activeRuntime);
                return true;
            }

            List<CustomTradeRuntime> eligible = GetEligibleSpecialRequestRuntimes(traderManager);
            SyncSpecialRequestsIntoState(traderManager, eligible);
            if (eligible.Count <= 0) return false;

            if (!string.IsNullOrEmpty(currentDescriptionText) && !LooksLikeCurrentCustomSpecialRequestText(currentDescriptionText)) return false;
            if (HasActiveVanillaSpecialRequest(traderManager)) return false;

            text = eligible.Count == 1 ? GetSpecialRequestDescriptionText(eligible[0]) : $"{eligible.Count} custom special requests are available.";
            return true;
        }

        private static int SyncEligibleSpecialRequestsIntoState(TraderManager traderManager)
        {
            List<CustomTradeRuntime> eligible = GetEligibleSpecialRequestRuntimes(traderManager);
            SyncSpecialRequestsIntoState(traderManager, eligible);
            return eligible.Count;
        }

        private static List<CustomTradeRuntime> GetEligibleSpecialRequestRuntimes(TraderManager traderManager)
        {
            List<CustomTradeRuntime> eligible = [];
            EnsureBuilt();

            int trust = GetCurrentTrust(traderManager);

            for (int i = 0; i < s_RuntimeTrades.Count; i++)
            {
                CustomTradeRuntime candidate = s_RuntimeTrades[i];
                if (candidate == null || candidate.Definition == null) continue;
                if (!candidate.Definition.IsSpecialRequest) continue;
                if (!IsEligible(candidate, trust)) continue;

                eligible.Add(candidate);
            }

            return eligible;
        }

        private static void SyncSpecialRequestsIntoState(TraderManager traderManager, List<CustomTradeRuntime> runtimes)
        {
            if (traderManager == null || runtimes == null || runtimes.Count <= 0) return;

            for (int i = 0; i < runtimes.Count; i++) EnsureSpecialRequestInState(traderManager, runtimes[i]);

            string ids = JoinRuntimeIds(runtimes);
            if (!string.Equals(s_LastLoggedActiveSpecialRequestId, ids, StringComparison.Ordinal))
            {
                s_LastLoggedActiveSpecialRequestId = ids;
                Core.Log?.Msg($"Custom special requests available ({runtimes.Count}): {ids}.");
            }
        }

        private static string JoinRuntimeIds(List<CustomTradeRuntime> runtimes)
        {
            if (runtimes == null || runtimes.Count <= 0) return "none";

            string result = "";
            for (int i = 0; i < runtimes.Count; i++)
            {
                CustomTradeRuntime runtime = runtimes[i];
                string id = runtime?.Definition?.Id ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                if (result.Length > 0) result += ", ";
                result += "'" + id + "'";
            }

            return result.Length > 0 ? result : "none";
        }

        internal static string GetSpecialRequestHeaderText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "Special Request";
            if (!string.IsNullOrEmpty(runtime.Definition.SpecialRequest?.Title)) return runtime.Definition.SpecialRequest.Title;
            return runtime.Definition.Id;
        }

        internal static string GetSpecialRequestDescriptionText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "";
            if (!string.IsNullOrEmpty(runtime.Definition.SpecialRequest?.Description)) return runtime.Definition.SpecialRequest.Description;
            return GetTradeDescriptionText(runtime);
        }

        internal static string GetSpecialRequestRequirementText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "";
            return runtime.Definition.SpecialRequest?.RequirementText ?? "";
        }

        internal static bool ShouldHideSpecialRequestRequirementIcon(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return false;
            return runtime.Definition.SpecialRequest?.HideRequirementIcon == true;
        }

        internal static void ApplyJournalSpecialRequestListEntry(Panel_Log panel)
        {
            if (panel == null) return;

            try
            {
                List<SpecialRequestJournalEntry> activeEntries = GetActiveSpecialRequestJournalEntries();
                List<SpecialRequestJournalEntry> customEntries = GetCustomJournalEntries(activeEntries);
                bool hasVanilla = HasVanillaJournalEntry(activeEntries);

                if (customEntries.Count <= 0)
                {
                    CleanupJournalSpecialRequestDisplay(panel, hasVanilla);
                    return;
                }

                TraderLogItem baseLogItem = panel.m_SpecialRequestLogItem;
                if (baseLogItem == null || baseLogItem.gameObject == null) return;

                if (hasVanilla)
                {
                    HideExpandedTradingFrameworkDescription(baseLogItem.gameObject);
                    baseLogItem.gameObject.SetActive(true);
                    ApplySecondaryJournalSpecialRequestListEntry(panel, customEntries[0]);
                    return;
                }

                baseLogItem.gameObject.SetActive(true);
                ApplyJournalSpecialRequestListItemDisplay(baseLogItem.gameObject, customEntries[0].Runtime);

                if (customEntries.Count > 1) ApplySecondaryJournalSpecialRequestListEntry(panel, customEntries[1]);
                else HideSecondaryJournalSpecialRequestListEntry(panel);
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not apply custom special-request journal list entries: {e.Message}");
            }
        }

        internal static void ApplyJournalSpecialRequestSelection(Panel_Log panel, TraderLogItem selectedLogItem)
        {
            if (panel == null) return;

            try
            {
                ApplyJournalSpecialRequestListEntry(panel);

                if (!TryGetSelectedJournalSpecialRequestRuntime(panel, selectedLogItem, out CustomTradeRuntime runtime)) return;

                panel.m_SpecialRequestDetailsRoot?.SetActive(true);
                if (panel.m_SpecialRequestDetailsHeader != null) panel.m_SpecialRequestDetailsHeader.text = GetSpecialRequestJournalHeaderText(runtime);
                if (panel.m_SpecialRequestDetailsDescription != null) panel.m_SpecialRequestDetailsDescription.text = GetSpecialRequestJournalDescriptionText(runtime);
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not apply custom special-request journal selection display: {e.Message}");
            }
        }

        internal static bool ShouldBlockAddingSelectedSpecialRequest(Panel_TradeSelection panel)
        {
            if (panel == null) return false;

            try
            {
                TradeExchangeData selectedTrade = panel.SelectedTradeExchange;
                if (!IsSpecialRequestTradeData(selectedTrade)) return false;

                string selectedId = GetTradeId(selectedTrade);
                if (string.IsNullOrEmpty(selectedId)) return false;

                TraderManager traderManager = null;
                try { traderManager = GameManager.m_TraderManager; } catch { }

                HashSet<string> activeSpecialRequestIds = CollectActiveSpecialRequestIds(traderManager?.GetActiveTrade());
                HashSet<string> pendingSpecialRequestIds = CollectPanelSelectedSpecialRequestIds(panel, activeSpecialRequestIds);

                if (activeSpecialRequestIds.Contains(selectedId) || pendingSpecialRequestIds.Contains(selectedId))
                {
                    DiagnosticLog($"Blocked duplicate special-request selection in Trader UI: '{selectedId}'.");
                    PlaySpecialRequestSelectionLimitFeedback($"Special request already selected.");
                    return true;
                }

                int totalSelected = activeSpecialRequestIds.Count + pendingSpecialRequestIds.Count;
                if (totalSelected < MaxActiveSpecialRequests) return false;

                Core.Log?.Warning($"Blocked special-request selection in Trader UI: selected '{selectedId}', active:{activeSpecialRequestIds.Count}, pending:{pendingSpecialRequestIds.Count}, max:{MaxActiveSpecialRequests}.");
                PlaySpecialRequestSelectionLimitFeedback($"The Trader can only track {MaxActiveSpecialRequests} special requests at once.");
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not validate special-request selection limit: {e.Message}");
                return false;
            }
        }

        private sealed class SpecialRequestJournalEntry
        {
            internal string Id = "";
            internal bool IsCustom;
            internal CustomTradeRuntime Runtime;
            internal TradeExchangeData TradeData;
        }

        private static List<SpecialRequestJournalEntry> GetActiveSpecialRequestJournalEntries()
        {
            List<SpecialRequestJournalEntry> entries = [];

            try
            {
                TraderManager traderManager = GameManager.m_TraderManager;
                ActiveTradeState activeTrade = traderManager?.GetActiveTrade();
                if (activeTrade == null || activeTrade.m_ExchangeStates == null) return entries;

                EnsureBuilt();
                for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
                {
                    ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                    TradeExchangeData exchange = exchangeState?.m_Exchange;
                    if (exchange == null) continue;
                    if (!IsSpecialRequestTradeData(exchange)) continue;

                    string id = GetTradeId(exchange);
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ContainsJournalEntry(entries, id)) continue;

                    bool isCustom = TryGetTrade(exchange, out CustomTradeRuntime runtime) && runtime != null && runtime.IsSpecialRequest;
                    entries.Add(new SpecialRequestJournalEntry
                    {
                        Id = id,
                        IsCustom = isCustom,
                        Runtime = isCustom ? runtime : null,
                        TradeData = exchange
                    });

                    if (entries.Count >= MaxActiveSpecialRequests) break;
                }
            }
            catch { }

            return entries;
        }

        private static bool ContainsJournalEntry(List<SpecialRequestJournalEntry> entries, string id)
        {
            if (entries == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i]?.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static List<SpecialRequestJournalEntry> GetCustomJournalEntries(List<SpecialRequestJournalEntry> entries)
        {
            List<SpecialRequestJournalEntry> custom = [];
            if (entries == null) return custom;

            for (int i = 0; i < entries.Count; i++)
            {
                SpecialRequestJournalEntry entry = entries[i];
                if (entry == null || !entry.IsCustom || entry.Runtime == null) continue;
                custom.Add(entry);
                if (custom.Count >= MaxActiveSpecialRequests) break;
            }

            return custom;
        }

        private static bool HasVanillaJournalEntry(List<SpecialRequestJournalEntry> entries)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                SpecialRequestJournalEntry entry = entries[i];
                if (entry != null && !entry.IsCustom) return true;
            }

            return false;
        }

        private static void ApplySecondaryJournalSpecialRequestListEntry(Panel_Log panel, SpecialRequestJournalEntry entry)
        {
            if (panel == null || entry == null || entry.Runtime == null) return;

            TraderLogItem secondary = GetOrCreateSecondaryJournalSpecialRequestLogItem(panel);
            if (secondary == null || secondary.gameObject == null) return;

            secondary.gameObject.SetActive(true);
            ApplyJournalSpecialRequestListItemDisplay(secondary.gameObject, entry.Runtime);
        }

        private static TraderLogItem GetOrCreateSecondaryJournalSpecialRequestLogItem(Panel_Log panel)
        {
            TraderLogItem template = panel?.m_SpecialRequestLogItem;
            if (template == null || template.gameObject == null || template.transform == null) return null;

            Transform parent = template.transform.parent;
            if (parent == null) return null;

            Transform existing = parent.Find(SecondarySpecialRequestLogItemName);
            if (existing != null) return existing.GetComponent<TraderLogItem>();

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject);
            if (clone == null) return null;

            clone.name = SecondarySpecialRequestLogItemName;
            clone.transform.SetParent(parent, false);
            clone.transform.localScale = template.transform.localScale;
            Vector3 position = template.transform.localPosition;
            clone.transform.localPosition = new Vector3(position.x, position.y - 163f, position.z);
            DiagnosticLog("Created secondary special-request journal widget clone with vertical offset 163.");

            TraderLogItem traderLogItem = clone.GetComponent<TraderLogItem>();
            if (traderLogItem == null) traderLogItem = clone.AddComponent<TraderLogItem>();
            return traderLogItem;
        }

        private static void HideSecondaryJournalSpecialRequestListEntry(Panel_Log panel)
        {
            try
            {
                TraderLogItem template = panel?.m_SpecialRequestLogItem;
                Transform parent = template?.transform?.parent;
                Transform existing = parent?.Find(SecondarySpecialRequestLogItemName);
                existing?.gameObject.SetActive(false);
            }
            catch { }
        }

        private static bool TryGetSelectedJournalSpecialRequestRuntime(Panel_Log panel, TraderLogItem selectedLogItem, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (panel == null || selectedLogItem == null || selectedLogItem.gameObject == null) return false;

            List<SpecialRequestJournalEntry> activeEntries = GetActiveSpecialRequestJournalEntries();
            List<SpecialRequestJournalEntry> customEntries = GetCustomJournalEntries(activeEntries);
            if (customEntries.Count <= 0) return false;

            bool hasVanilla = HasVanillaJournalEntry(activeEntries);
            TraderLogItem baseLogItem = panel.m_SpecialRequestLogItem;
            if (!hasVanilla && selectedLogItem == baseLogItem)
            {
                runtime = customEntries[0].Runtime;
                return runtime != null;
            }

            if (selectedLogItem.gameObject.name.Contains(SecondarySpecialRequestLogItemName, StringComparison.OrdinalIgnoreCase))
            {
                int index = hasVanilla ? 0 : 1;
                if (customEntries.Count <= index) return false;
                runtime = customEntries[index].Runtime;
                return runtime != null;
            }

            return false;
        }

        private static void CleanupJournalSpecialRequestDisplay(Panel_Log panel, bool preserveVanillaSpecialRequest)
        {
            if (panel == null) return;

            try
            {
                HideSecondaryJournalSpecialRequestListEntry(panel);

                TraderLogItem specialRequestLogItem = panel.m_SpecialRequestLogItem;
                if (specialRequestLogItem != null && specialRequestLogItem.gameObject != null)
                {
                    HideExpandedTradingFrameworkDescription(specialRequestLogItem.gameObject);
                    if (!preserveVanillaSpecialRequest) specialRequestLogItem.gameObject.SetActive(false);
                }

                if (!preserveVanillaSpecialRequest)
                {
                    panel.m_SpecialRequestDetailsRoot?.SetActive(false);
                    if (panel.m_SpecialRequestDetailsHeader != null) panel.m_SpecialRequestDetailsHeader.text = "";
                    if (panel.m_SpecialRequestDetailsDescription != null) panel.m_SpecialRequestDetailsDescription.text = "";
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not clean up custom special-request journal display: {e.Message}");
            }
        }

        private static void HideExpandedTradingFrameworkDescription(GameObject root)
        {
            if (root == null) return;

            Transform description = root.transform?.Find("ExpandedTradingFramework_SpecialRequestListDescription");
            description?.gameObject.SetActive(false);
        }

        private static bool HasActiveVanillaSpecialRequest(TraderManager traderManager)
        {
            try
            {
                ActiveTradeState activeTrade = traderManager?.GetActiveTrade();
                if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;

                for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
                {
                    TradeExchangeData exchange = activeTrade.m_ExchangeStates[i]?.m_Exchange;
                    if (exchange == null) continue;
                    if (!IsSpecialRequestTradeData(exchange)) continue;
                    if (TryGetTrade(exchange, out CustomTradeRuntime runtime) && runtime != null && runtime.IsSpecialRequest) continue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static HashSet<string> CollectActiveSpecialRequestIds(ActiveTradeState activeTrade)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return ids;

            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                TradeExchangeData exchange = activeTrade.m_ExchangeStates[i]?.m_Exchange;
                if (exchange == null) continue;
                if (!IsSpecialRequestTradeData(exchange)) continue;

                string id = GetTradeId(exchange);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }

            return ids;
        }

        private static HashSet<string> CollectPanelSelectedSpecialRequestIds(Panel_TradeSelection panel, HashSet<string> activeSpecialRequestIds)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (panel == null) return ids;

            try
            {
                Il2CppTradeExchangeDataList selectedTrades = panel.m_SelectedTradeExchanges;
                if (selectedTrades == null) return ids;

                for (int i = 0; i < selectedTrades.Count; i++)
                {
                    TradeExchangeData exchange = selectedTrades[i];
                    if (exchange == null) continue;
                    if (!IsSpecialRequestTradeData(exchange)) continue;

                    string id = GetTradeId(exchange);
                    if (string.IsNullOrEmpty(id)) continue;
                    if (activeSpecialRequestIds != null && activeSpecialRequestIds.Contains(id)) continue;

                    ids.Add(id);
                }
            }
            catch { }

            return ids;
        }

        private static bool IsSpecialRequestTradeData(TradeExchangeData exchange)
        {
            if (exchange == null) return false;
            if (TryGetTrade(exchange, out CustomTradeRuntime runtime)) return runtime != null && runtime.IsSpecialRequest;

            try
            {
                return exchange.m_IsSpecialRequest;
            }
            catch
            {
                return false;
            }
        }

        private static void PlaySpecialRequestSelectionLimitFeedback(string message)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - s_LastSpecialRequestLimitFeedbackTime < 0.35f) return;
                s_LastSpecialRequestLimitFeedbackTime = now;
            }
            catch { }

            TryShowHudMessage(message);
        }

        private static void TryShowHudMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                HUDMessage.AddMessage(message, 5f, true, true);
            }
            catch { }
        }

        internal static bool TryGetActiveSpecialRequestRuntime(out CustomTradeRuntime runtime)
        {
            runtime = null;

            List<SpecialRequestJournalEntry> activeEntries = GetActiveSpecialRequestJournalEntries();
            for (int i = 0; i < activeEntries.Count; i++)
            {
                SpecialRequestJournalEntry entry = activeEntries[i];
                if (entry == null || !entry.IsCustom || entry.Runtime == null) continue;
                runtime = entry.Runtime;
                return true;
            }

            return false;
        }

        private static string GetSpecialRequestJournalHeaderText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "Special Request";
            if (!string.IsNullOrEmpty(runtime.Definition.SpecialRequest?.JournalTitle)) return runtime.Definition.SpecialRequest.JournalTitle;
            return GetSpecialRequestHeaderText(runtime);
        }

        private static string GetSpecialRequestJournalListTitleText()
        {
            return "Special Request";
        }

        private static string GetSpecialRequestJournalListDescriptionText(CustomTradeRuntime runtime)
        {
            return GetSpecialRequestDescriptionText(runtime);
        }

        private static string GetSpecialRequestJournalDescriptionText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "";

            CustomSpecialRequestDisplay display = runtime.Definition.SpecialRequest;
            string text = !string.IsNullOrEmpty(display?.JournalDescription) ? display.JournalDescription : GetSpecialRequestDescriptionText(runtime);

            if (display?.JournalTips == null || display.JournalTips.Length <= 0) return text;

            List<string> tips = [];
            for (int i = 0; i < display.JournalTips.Length; i++)
            {
                string tip = display.JournalTips[i];
                if (string.IsNullOrWhiteSpace(tip)) continue;
                tips.Add(tip);
            }

            if (tips.Count <= 0) return text;

            if (!string.IsNullOrEmpty(text)) text += "\n\n";
            text += "Tips:";
            for (int i = 0; i < tips.Count; i++) text += "\n- " + tips[i];
            return text;
        }

        private static void ApplyJournalSpecialRequestListItemDisplay(GameObject root, CustomTradeRuntime runtime)
        {
            if (root == null || runtime == null) return;

            string title = GetSpecialRequestJournalListTitleText();
            string description = GetSpecialRequestJournalListDescriptionText(runtime);

            UILabel titleLabel = null;
            UILabel progressLabel = null;
            var labels = root.GetComponentsInChildren<UILabel>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null) continue;

                string text = label.text ?? "";
                if (titleLabel == null && IsSpecialRequestListTitleCandidate(text))
                {
                    titleLabel = label;
                    continue;
                }

                if (progressLabel == null && IsSpecialRequestListProgressCandidate(text))
                {
                    progressLabel = label;
                    continue;
                }
            }

            titleLabel ??= FirstUsableLabel(labels);
            if (titleLabel != null) titleLabel.text = title;

            progressLabel?.gameObject.SetActive(false);

            UILabel descriptionLabel = GetOrCreateJournalSpecialRequestDescriptionLabel(root, titleLabel, progressLabel);
            if (descriptionLabel != null && !string.IsNullOrEmpty(description))
            {
                ConfigureJournalSpecialRequestDescriptionLabel(descriptionLabel, titleLabel, description);
            }

            HideJournalSpecialRequestProgressWidgets(root);
        }

        private static bool IsSpecialRequestListTitleCandidate(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("active trade", StringComparison.OrdinalIgnoreCase)
                || text.Contains("special request", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpecialRequestListProgressCandidate(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string trimmed = text.Trim();
            return trimmed.EndsWith("%", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("complete", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("progress", StringComparison.OrdinalIgnoreCase);
        }

        private static UILabel GetOrCreateJournalSpecialRequestDescriptionLabel(GameObject root, UILabel titleLabel, UILabel progressLabel)
        {
            if (root == null) return null;

            Transform parent = titleLabel != null && titleLabel.transform != null && titleLabel.transform.parent != null ? titleLabel.transform.parent : root.transform;
            Transform existing = parent?.Find("ExpandedTradingFramework_SpecialRequestListDescription");
            if (existing != null) return existing.GetComponent<UILabel>();

            UILabel label = progressLabel;
            if (label != null)
            {
                label.gameObject.name = "ExpandedTradingFramework_SpecialRequestListDescription";
                if (parent != null && label.transform.parent != parent) label.transform.SetParent(parent, false);
                label.gameObject.SetActive(true);
                return label;
            }

            if (titleLabel == null || parent == null) return null;

            GameObject descriptionObject = new("ExpandedTradingFramework_SpecialRequestListDescription");
            descriptionObject.transform.SetParent(parent, false);
            UILabel descriptionLabel = descriptionObject.AddComponent<UILabel>();
            return descriptionLabel;
        }

        private static void ConfigureJournalSpecialRequestDescriptionLabel(UILabel descriptionLabel, UILabel titleLabel, string description)
        {
            if (descriptionLabel == null) return;

            descriptionLabel.gameObject.SetActive(true);
            descriptionLabel.text = description;

            try
            {
                if (titleLabel != null)
                {
                    descriptionLabel.color = titleLabel.color;
                    descriptionLabel.fontSize = Math.Max(14, titleLabel.fontSize - 2);
                    descriptionLabel.width = Math.Max(Math.Max(titleLabel.width, descriptionLabel.width), 360);
                    descriptionLabel.height = Math.Max(42, descriptionLabel.height);
                    descriptionLabel.depth = titleLabel.depth + 1;
                    descriptionLabel.alignment = titleLabel.alignment;
                    descriptionLabel.pivot = titleLabel.pivot;

                    Transform titleTransform = titleLabel.transform;
                    Transform descriptionTransform = descriptionLabel.transform;
                    if (titleTransform != null && descriptionTransform != null)
                    {
                        if (descriptionTransform.parent != titleTransform.parent && titleTransform.parent != null) descriptionTransform.SetParent(titleTransform.parent, false);
                        Vector3 position = titleTransform.localPosition;
                        descriptionTransform.localPosition = new Vector3(position.x, position.y - 44f, position.z);
                        descriptionTransform.localScale = titleTransform.localScale;
                    }
                }
            }
            catch { }
        }

        private static UILabel FirstUsableLabel(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<UILabel> labels, UILabel excluded = null)
        {
            if (labels == null) return null;

            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null || label == excluded) continue;
                if (!string.IsNullOrEmpty(label.text)) return label;
            }

            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null || label == excluded) continue;
                return label;
            }

            return null;
        }

        private static void HideJournalSpecialRequestProgressWidgets(GameObject root)
        {
            if (root == null) return;

            var progressBars = root.GetComponentsInChildren<UIProgressBar>(true);
            for (int i = 0; i < progressBars.Length; i++)
            {
                UIProgressBar progressBar = progressBars[i];
                if (progressBar == null || progressBar.gameObject == null) continue;
                if (IsExpandedTradingFrameworkSpecialRequestDescription(progressBar.gameObject)) continue;
                progressBar.gameObject.SetActive(false);
            }

            var sliders = root.GetComponentsInChildren<UISlider>(true);
            for (int i = 0; i < sliders.Length; i++)
            {
                UISlider slider = sliders[i];
                if (slider == null || slider.gameObject == null) continue;
                if (IsExpandedTradingFrameworkSpecialRequestDescription(slider.gameObject)) continue;
                slider.gameObject.SetActive(false);
            }

            var sprites = root.GetComponentsInChildren<UISprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                UISprite sprite = sprites[i];
                if (sprite == null || sprite.gameObject == null) continue;
                if (IsExpandedTradingFrameworkSpecialRequestDescription(sprite.gameObject)) continue;
                if (!IsJournalSpecialRequestProgressVisual(sprite.gameObject)) continue;

                sprite.gameObject.SetActive(false);
            }
        }

        private static bool IsExpandedTradingFrameworkSpecialRequestDescription(GameObject gameObject)
        {
            if (gameObject == null) return false;
            Transform current = gameObject.transform;
            int depth = 0;
            while (current != null && depth < 4)
            {
                string name = current.gameObject != null ? current.gameObject.name ?? "" : "";
                if (name.Contains("ExpandedTradingFramework_SpecialRequestListDescription", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("ExpandedTraderFramework_SpecialRequestListDescription", StringComparison.OrdinalIgnoreCase)) return true; // Legacy intermediate rename cleanup
                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool IsJournalSpecialRequestProgressVisual(GameObject gameObject)
        {
            Transform current = gameObject?.transform;
            int depth = 0;
            while (current != null && depth < 4)
            {
                string name = current.gameObject != null ? current.gameObject.name ?? "" : "";
                if (name.Contains("progress", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("completion", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("fill", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("meter", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("slider", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("track", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Contains("bar", StringComparison.OrdinalIgnoreCase)) return true;

                current = current.parent;
                depth++;
            }

            return false;
        }

        internal static void ApplyTradeSelectionUiOverrides(Panel_TradeSelection panel, CustomTradeRuntime runtime)
        {
            if (panel == null || runtime == null || runtime.Definition == null) return;

            try
            {
                if (panel.m_TradeExchangeDescriptionLabel != null)
                {
                    string description = runtime.IsSpecialRequest ? GetSpecialRequestDescriptionText(runtime) : GetTradeDescriptionText(runtime);
                    panel.m_TradeExchangeDescriptionLabel.text = description;
                    LogTradeDisplayBehavior(runtime, "trade selection description", description);
                }
            }
            catch { }

            if (!runtime.IsSpecialRequest) return;

            try
            {
                if (panel.m_GainedItemNameLabel != null)
                {
                    string header = GetSpecialRequestHeaderText(runtime);
                    panel.m_GainedItemNameLabel.text = header;
                    LogTradeDisplayBehavior(runtime, "special request gained item header override", header);
                }
            }
            catch { }

            ApplySpecialRequestRequirementOverride(panel, runtime);
        }

        private static void ApplySpecialRequestRequirementOverride(Panel_TradeSelection panel, CustomTradeRuntime runtime)
        {
            string requirementText = GetSpecialRequestRequirementText(runtime);
            if (string.IsNullOrEmpty(requirementText)) return;

            try
            {
                Il2CppSystem.Collections.Generic.List<TradeExchangeRequirementItem> slots = panel.m_RequirementItems;
                if (slots == null || slots.Count <= 0) return;

                TradeExchangeRequirementItem firstSlot = slots[0];
                if (firstSlot == null) return;

                firstSlot.gameObject.SetActive(true);
                SetFirstRequirementSlotText(firstSlot.gameObject, requirementText);

                if (ShouldHideSpecialRequestRequirementIcon(runtime)) HideRequirementSlotIcons(firstSlot.gameObject);

                for (int i = 1; i < slots.Count; i++)
                {
                    TradeExchangeRequirementItem slot = slots[i];
                    slot?.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not apply custom special-request requirement display for '{runtime.Definition.Id}': {e.Message}");
            }
        }

        private static void SetFirstRequirementSlotText(GameObject root, string text)
        {
            if (root == null) return;

            UILabel selectedLabel = null;
            UILabel firstLabel = null;
            var labels = root.GetComponentsInChildren<UILabel>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null) continue;
                firstLabel ??= label;
                if (!string.IsNullOrEmpty(label.text))
                {
                    selectedLabel = label;
                    break;
                }
            }

            selectedLabel ??= firstLabel;
            if (selectedLabel == null) return;

            selectedLabel.text = text;
            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null || label == selectedLabel) continue;
                label.text = "";
            }
        }

        private static void HideRequirementSlotIcons(GameObject root)
        {
            if (root == null) return;

            var textures = root.GetComponentsInChildren<UITexture>(true);
            for (int i = 0; i < textures.Length; i++)
            {
                UITexture texture = textures[i];
                texture?.gameObject.SetActive(false);
            }

            var sprites = root.GetComponentsInChildren<UISprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                UISprite sprite = sprites[i];
                sprite?.gameObject.SetActive(false);
            }
        }

        internal static string GetTradeDescriptionText(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "";
            if (!string.IsNullOrEmpty(runtime.Definition.Description)) return runtime.Definition.Description;
            return BuildTradeSummary(runtime);
        }

        internal static void BeginRewardPreparation(ExchangeBox exchangeBox)
        {
            if (s_RewardPreparationDepth == 0) RestoreStackableRewardAmountOverrides("stale reward preparation cleanup");

            s_RewardPreparationDepth++;
            try
            {
                if (exchangeBox != null) RehydrateActiveTradeState(exchangeBox.m_ActiveTrade, "exchange-box reward preparation");
            }
            catch { }

            ApplyStackableRewardAmountOverrides(exchangeBox);
        }

        internal static void EndRewardPreparation()
        {
            if (s_RewardPreparationDepth > 0) s_RewardPreparationDepth--;
            if (s_RewardPreparationDepth <= 0) RestoreStackableRewardAmountOverrides("reward preparation finished");
        }

        private static void ApplyStackableRewardAmountOverrides(ExchangeBox exchangeBox)
        {
            if (exchangeBox == null) return;

            ActiveTradeState activeTrade;
            try
            {
                activeTrade = exchangeBox.m_ActiveTrade;
            }
            catch { return; }

            if (activeTrade?.m_ExchangeStates == null) return;

            for (int exchangeIndex = 0; exchangeIndex < activeTrade.m_ExchangeStates.Count; exchangeIndex++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[exchangeIndex];
                TradeExchangeData exchangeData = exchangeState?.m_Exchange;
                if (exchangeData == null) continue;
                if (!TryGetTrade(exchangeData, out CustomTradeRuntime runtime)) continue;
                if (!ShouldNormalizeStackableReward(runtime)) continue;
                if (HasStackableRewardAmountOverride(exchangeData)) continue;

                int originalAmount = Math.Max(1, exchangeData.m_GainedItemAmount);
                int expectedUnits = GetExpectedRewardAmount(runtime, exchangeData);
                if (expectedUnits <= 1) continue;

                s_StackableRewardAmountOverrides.Add(new StackableRewardAmountOverride
                {
                    ExchangeData = exchangeData,
                    Runtime = runtime,
                    OriginalAmount = originalAmount,
                    ExpectedUnits = expectedUnits
                });

                exchangeData.m_GainedItemAmount = 1;
                DiagnosticLog($"Custom trade '{runtime.Definition.Id}' stackable reward spawn override -> exchangeSlot:{exchangeIndex}, originalSpawnCount:{originalAmount}, expectedUnits:{expectedUnits}, temporarySpawnCount:1.");
            }
        }

        private static bool HasStackableRewardAmountOverride(TradeExchangeData exchangeData)
        {
            if (exchangeData == null) return false;
            for (int i = 0; i < s_StackableRewardAmountOverrides.Count; i++)
            {
                if (ReferenceEquals(s_StackableRewardAmountOverrides[i]?.ExchangeData, exchangeData)) return true;
            }

            return false;
        }

        private static bool TryGetStackableRewardAmountOverride(TradeExchangeData exchangeData, out StackableRewardAmountOverride amountOverride)
        {
            amountOverride = null;
            if (exchangeData == null) return false;

            for (int i = s_StackableRewardAmountOverrides.Count - 1; i >= 0; i--)
            {
                StackableRewardAmountOverride candidate = s_StackableRewardAmountOverrides[i];
                if (candidate == null || candidate.ExchangeData == null) continue;
                if (!ReferenceEquals(candidate.ExchangeData, exchangeData)) continue;

                amountOverride = candidate;
                return true;
            }

            return false;
        }

        private static void RestoreStackableRewardAmountOverrides(string source)
        {
            if (s_StackableRewardAmountOverrides.Count <= 0) return;
            ArgumentNullException.ThrowIfNull(source);

            for (int i = s_StackableRewardAmountOverrides.Count - 1; i >= 0; i--)
            {
                StackableRewardAmountOverride amountOverride = s_StackableRewardAmountOverrides[i];
                if (amountOverride?.ExchangeData == null) continue;

                try
                {
                    amountOverride.ExchangeData.m_GainedItemAmount = Math.Max(1, amountOverride.OriginalAmount);
                    if (amountOverride.Runtime?.Definition != null) DiagnosticLog($"Custom trade '{amountOverride.Runtime.Definition.Id}' stackable reward spawn override restored from {source} -> restoredSpawnCount:{Math.Max(1, amountOverride.OriginalAmount)}, expectedUnits:{Math.Max(1, amountOverride.ExpectedUnits)}.");
                }
                catch (Exception e)
                {
                    Core.Log?.Warning($"Could not restore stackable reward amount override after {source}: {e.Message}");
                }
            }

            s_StackableRewardAmountOverrides.Clear();
        }

        internal static void NormalizeStackableRewards(ExchangeBox exchangeBox)
        {
            NormalizeStackableRewards(exchangeBox, "post-prepare");
        }

        private static void NormalizeStackableRewards(ExchangeBox exchangeBox, string source)
        {
            if (exchangeBox == null) return;
            ArgumentNullException.ThrowIfNull(source);
            ActiveTradeState activeTrade;
            try
            {
                activeTrade = exchangeBox.m_ActiveTrade;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not read active trade from exchange box while normalizing stackable rewards: {e.Message}");
                return;
            }

            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return;

            RemoveCompletionOnlyDisplayRewardItems(exchangeBox, source);

            for (int exchangeIndex = 0; exchangeIndex < activeTrade.m_ExchangeStates.Count; exchangeIndex++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[exchangeIndex];
                if (exchangeState == null) continue;
                if (!TryGetTrade(exchangeState.m_Exchange, out CustomTradeRuntime runtime)) continue;
                if (!ShouldNormalizeStackableReward(runtime)) continue;

                NormalizeStackableReward(exchangeBox, runtime, GetExpectedRewardAmount(runtime, exchangeState.m_Exchange), source);
            }
        }


        internal static void LogExchangeBoxRequirementProgressSlotCount(Panel_ExchangeBox panel)
        {
            if (s_LoggedExchangeBoxProgressSlotCount) return;

            int slotCount = 0;
            try
            {
                if (panel != null && panel.m_ExchangeProgressItems != null) slotCount = panel.m_ExchangeProgressItems.Count;
            }
            catch { }

            s_LoggedExchangeBoxProgressSlotCount = true;
            Core.Log?.Msg($"Trader Exchange Box requirement progress rows detected: {slotCount}. Trades may accept more requirements than this, but only this many are visible in the Exchange Box progress display.");
        }

        internal static void NormalizeStackableRewardsBeforeExchangeBoxUi(ExchangeBox exchangeBox, string source)
        {
            if (exchangeBox == null) return;

            try
            {
                RehydrateActiveTradeState(exchangeBox.m_ActiveTrade, "exchange-box UI " + source);
            }
            catch { }

            NormalizeStackableRewards(exchangeBox, source);
        }

        internal static bool TrySuppressNoRewardItem(ExchangeBox exchangeBox, int exchangeIndex, GearItem gearItem)
        {
            if (exchangeBox == null || gearItem == null) return false;
            if (!IsPreparingRewards(exchangeBox)) return false;

            if (TryGetRuntimeForExchangeBoxIndex(exchangeBox, exchangeIndex, out CustomTradeRuntime runtime))
            {
                if (!runtime.HasPhysicalReward && IsDisplayRewardGear(runtime, gearItem)) return SuppressNoRewardItem(runtime, exchangeIndex, gearItem);
                if (runtime.HasPhysicalReward && IsPhysicalRewardGear(runtime, gearItem)) return false;
            }

            if (!TryGetCompletionOnlyRuntimeForDisplayGear(exchangeBox, gearItem, out CustomTradeRuntime completionOnlyRuntime, out int completionOnlyIndex)) return false;
            return SuppressNoRewardItem(completionOnlyRuntime, completionOnlyIndex >= 0 ? completionOnlyIndex : exchangeIndex, gearItem);
        }

        private static bool SuppressNoRewardItem(CustomTradeRuntime runtime, int exchangeIndex, GearItem gearItem)
        {
            try
            {
                UnityEngine.Object.Destroy(gearItem.gameObject);
            }
            catch { }

            LogNoRewardSuppressionOnce(runtime, exchangeIndex);
            return true;
        }

        private static bool TryGetCompletionOnlyRuntimeForDisplayGear(ExchangeBox exchangeBox, GearItem gearItem, out CustomTradeRuntime runtime, out int exchangeIndex)
        {
            runtime = null;
            exchangeIndex = -1;
            if (exchangeBox == null || gearItem == null) return false;

            try
            {
                ActiveTradeState activeTrade = exchangeBox.m_ActiveTrade;
                if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;

                for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
                {
                    ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                    if (exchangeState == null) continue;
                    if (!TryGetTrade(exchangeState.m_Exchange, out CustomTradeRuntime candidate)) continue;
                    if (candidate.HasPhysicalReward) continue;
                    if (!IsDisplayRewardGear(candidate, gearItem)) continue;

                    runtime = candidate;
                    exchangeIndex = i;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsDisplayRewardGear(CustomTradeRuntime runtime, GearItem gearItem)
        {
            if (runtime == null || gearItem == null) return false;
            return GearResolver.IsSameGear(gearItem, runtime.DisplayRewardGearItemData, runtime.Definition?.DisplayReward?.GearName ?? "");
        }

        private static bool IsPhysicalRewardGear(CustomTradeRuntime runtime, GearItem gearItem)
        {
            if (runtime == null || gearItem == null || !runtime.HasPhysicalReward || runtime.Definition?.Reward == null) return false;
            return GearResolver.IsSameGear(gearItem, runtime.RewardGearItemData, runtime.Definition.Reward.GearName);
        }

        private static bool TryGetStackableRewardRuntimeForGear(ExchangeBox exchangeBox, GearItem gearItem, out CustomTradeRuntime runtime, out TradeExchangeData exchangeData)
        {
            runtime = null;
            exchangeData = null;
            if (exchangeBox == null || gearItem == null) return false;

            ActiveTradeState activeTrade;
            try
            {
                activeTrade = exchangeBox.m_ActiveTrade;
            }
            catch { return false; }

            if (activeTrade?.m_ExchangeStates == null) return false;

            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                if (exchangeState == null) continue;
                if (!TryGetTrade(exchangeState.m_Exchange, out CustomTradeRuntime candidate)) continue;
                if (!ShouldNormalizeStackableReward(candidate)) continue;
                if (!IsPhysicalRewardGear(candidate, gearItem)) continue;

                runtime = candidate;
                exchangeData = exchangeState.m_Exchange;
                return true;
            }

            return false;
        }

        private static TradeExchangeData TryGetActiveTradeExchangeForRuntime(ExchangeBox exchangeBox, CustomTradeRuntime runtime)
        {
            if (exchangeBox == null || runtime?.Definition == null) return null;

            try
            {
                ActiveTradeState activeTrade = exchangeBox.m_ActiveTrade;
                if (activeTrade?.m_ExchangeStates == null) return null;

                for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
                {
                    ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                    TradeExchangeData exchangeData = exchangeState?.m_Exchange;
                    if (exchangeData == null) continue;
                    if (string.Equals(exchangeData.m_TradeExchangeName, runtime.Definition.Id, StringComparison.OrdinalIgnoreCase)) return exchangeData;
                }
            }
            catch { }

            return null;
        }

        internal static bool TryAbsorbStackableRewardItem(ExchangeBox exchangeBox, int exchangeIndex, GearItem gearItem)
        {
            if (exchangeBox == null || gearItem == null) return false;
            if (!IsPreparingRewards(exchangeBox)) return false;

            TradeExchangeData exchangeData = null;
            if (!TryGetRuntimeForExchangeBoxIndex(exchangeBox, exchangeIndex, out CustomTradeRuntime runtime) || !IsPhysicalRewardGear(runtime, gearItem))
            {
                if (!TryGetStackableRewardRuntimeForGear(exchangeBox, gearItem, out runtime, out exchangeData)) return false;
            }
            else
            {
                exchangeData = TryGetActiveTradeExchangeForRuntime(exchangeBox, runtime);
            }

            if (!ShouldNormalizeStackableReward(runtime)) return false;

            StackableItem incomingStack = GetStackableItemSafe(gearItem);
            if (incomingStack == null) return false;

            int expectedUnits = GetExpectedRewardAmount(runtime, exchangeData);

            if (TryFindExistingRewardStack(exchangeBox, runtime, gearItem, out StackableItem existingStack))
            {
                existingStack.m_Units = expectedUnits;
                LogStackableRewardUnitsAppliedOnce(runtime, exchangeIndex, expectedUnits, "existing reward stack");

                try
                {
                    UnityEngine.Object.Destroy(gearItem.gameObject);
                }
                catch { }

                LogStackableRewardPreparationOnce(runtime, exchangeIndex, $"Stacked reward for '{runtime.Definition.Id}' while preparing exchange slot {exchangeIndex}: kept one stack at {expectedUnits} unit(s) and skipped duplicate reward instances before they reached the exchange box.");
                return true;
            }

            incomingStack.m_Units = expectedUnits;
            LogStackableRewardUnitsAppliedOnce(runtime, exchangeIndex, expectedUnits, "first reward stack");
            LogStackableRewardPreparationOnce(runtime, exchangeIndex, $"Prepared stackable reward for '{runtime.Definition.Id}' in exchange slot {exchangeIndex}: first reward instance set to {expectedUnits} unit(s) before it reached the exchange box.");
            return false;
        }

        internal static void OnTradeCompleted(ActiveTradeState activeTrade)
        {
            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return;

            bool completedNormalTrade = ContainsNormalTradeExchange(activeTrade);
            if (completedNormalTrade) InvalidateNormalTradeSelectionCache("TraderManager.CompleteTrade", true, false);

            int completedCustomTrades = 0;
            for (int i = 0; i < activeTrade.m_ExchangeStates.Count; i++)
            {
                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[i];
                if (exchangeState == null) continue;
                if (!TryGetTrade(exchangeState.m_Exchange, out CustomTradeRuntime runtime)) continue;

                completedCustomTrades++;
                MarkCompleted(runtime, GetActiveTradeMultiplier(runtime, exchangeState.m_Exchange));
            }

            if (completedNormalTrade && completedCustomTrades <= 0) SaveFrameworkState("vanilla trade completed");
        }

        private static void MarkCompleted(CustomTradeRuntime runtime, int multiplier)
        {
            if (runtime == null || runtime.Definition == null || string.IsNullOrEmpty(runtime.Definition.Id)) return;
            EnsureSaveDataLoaded("trade completed");

            string id = runtime.Definition.Id;
            int completedAmount = Math.Max(1, multiplier);
            s_SaveData.CompletionCounts[id] = GetCompletionCount(id) + completedAmount;

            Core.Log?.Msg($"Completed custom trade '{id}'. Reward: {DescribeActualReward(runtime)}. Added completions:{completedAmount}. Completion count:{s_SaveData.CompletionCounts[id]}.");
            DiagnosticLog($"Completion behavior for custom trade '{id}' -> multiplier:{multiplier}, addedCompletions:{completedAmount}, repeatable:{runtime.Definition.Repeatable}, hasPhysicalReward:{runtime.HasPhysicalReward}, onCompleted:{(runtime.Definition.OnCompleted != null)}.");
            InvokeCompletionCallback(runtime, s_SaveData.CompletionCounts[id]);
            if (!runtime.Definition.Repeatable && !ContainsString(s_SaveData.CompletedNonRepeatableTradeIds, id))
            {
                s_SaveData.CompletedNonRepeatableTradeIds.Add(id);
                DiagnosticLog($"Marked non-repeatable custom trade '{id}' as completed for this save.");
            }
            s_LastObservedTradeMultipliers.Remove(id);
            s_SaveData.ActiveCustomTradeId = "";
            s_SaveData.ActiveCustomTradeMultiplier = 1;
            SaveFrameworkState("trade completed");
        }

        private static void InvokeCompletionCallback(CustomTradeRuntime runtime, int completionCount)
        {
            if (runtime == null || runtime.Definition == null || runtime.Definition.OnCompleted == null) return;

            bool completionOnly = runtime.Definition.Reward == null || runtime.Definition.Reward.Type == CustomTradeRewardType.None || !runtime.HasPhysicalReward;
            DiagnosticLog($"Invoking completion callback for custom trade '{runtime.Definition.Id}' -> completionCount:{completionCount}, completionOnly:{completionOnly}.");

            try
            {
                runtime.Definition.OnCompleted(new CustomTradeCompletionContext
                {
                    TradeId = runtime.Definition.Id,
                    CompletionCount = completionCount,
                    IsCompletionOnly = completionOnly,
                    Definition = runtime.Definition
                });

                DiagnosticLog($"Completion callback finished for custom trade '{runtime.Definition.Id}'.");
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Custom trade '{runtime.Definition.Id}' completion callback failed: {e}");
            }
        }

        private static string DescribeActualReward(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null || runtime.Definition.Reward == null) return "unknown";
            if (!runtime.HasPhysicalReward || runtime.Definition.Reward.Type == CustomTradeRewardType.None) return "none";
            return FormatRewardDisplay(runtime.RewardDisplayName ?? runtime.Definition.Reward.GearName, runtime.VisibleRewardAmount);
        }

        internal static bool IsMatchingCost(CustomTradeCostRuntime costRuntime, GearItem gearItem)
        {
            if (costRuntime == null || costRuntime.Definition == null || gearItem == null) return false;

            bool result = costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.GearWeight => GearResolver.IsSameGear(gearItem, costRuntime.GearItemData, costRuntime.Definition.GearName),
                ExchangeItemType.CategoryAmount or ExchangeItemType.CategoryWeight => IsMatchingCategoryCost(costRuntime, gearItem),
                _ => false,
            };

            LogCostMatchDiagnosticsOnce(costRuntime, gearItem, result);
            return result;
        }

        internal static Texture2D GetCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null) return null;

            if (costRuntime.HasCustomIcon)
            {
                LogCostIconResolutionOnce(costRuntime, "custom-display-icon", costRuntime.Icon);
                return costRuntime.Icon;
            }

            if (IsCategoryCost(costRuntime)
                && IsVanillaCategoryIcon(costRuntime.CategoryIcon)
                && TryGetRequestedVanillaCategoryIcon(costRuntime, out Texture2D vanillaCategoryIcon))
            {
                Texture2D assigned = AssignCostIcon(costRuntime, vanillaCategoryIcon);
                LogCostIconResolutionOnce(costRuntime, "vanilla-category-icon", assigned);
                return assigned;
            }

            if (costRuntime.Icon != null)
            {
                LogCostIconResolutionOnce(costRuntime, "cached-runtime-icon", costRuntime.Icon);
                return costRuntime.Icon;
            }

            Texture2D icon = TryResolveCostIcon(costRuntime);
            if (icon != null)
            {
                LogCostIconResolutionOnce(costRuntime, "lazy-runtime-retry", icon);
                return icon;
            }

            LogCostIconResolutionOnce(costRuntime, "unresolved", null);
            LogMissingCostIcon(costRuntime);
            return null;
        }

        private static Texture2D TryResolveCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return null;

            return costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.GearWeight => TryResolveGearCostIcon(costRuntime),
                ExchangeItemType.CategoryAmount or ExchangeItemType.CategoryWeight => TryResolveCategoryCostIcon(costRuntime),
                ExchangeItemType.Liquid => TryResolveLiquidCostIcon(costRuntime),
                _ => null,
            };
        }

        private static Texture2D TryResolveGearCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return null;

            Texture2D icon = GearResolver.GetInventoryIcon(costRuntime.CostPrefab);
            if (icon != null) return AssignCostIcon(costRuntime, icon);

            if (!string.IsNullOrEmpty(costRuntime.Definition.GearName) && GearResolver.TryResolvePrefab(costRuntime.Definition.GearName, out GearItem prefab))
            {
                costRuntime.CostPrefab = prefab;
                try
                {
                    costRuntime.GearItemData ??= prefab?.GearItemData;
                }
                catch { }

                icon = GearResolver.GetInventoryIcon(prefab);
                if (icon != null) return AssignCostIcon(costRuntime, icon);
            }

            return null;
        }

        private static Texture2D TryResolveCategoryCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return null;

            if (IsVanillaCategoryIcon(costRuntime.CategoryIcon) && TryGetRequestedVanillaCategoryIcon(costRuntime, out Texture2D vanillaCategoryIcon))
            {
                return AssignCostIcon(costRuntime, vanillaCategoryIcon);
            }

            if (costRuntime.CategoryIcon == CategoryIcon.GearItem && !string.IsNullOrEmpty(costRuntime.CategoryIconGearName))
            {
                Texture2D requestedIcon = TryGetGearIcon(costRuntime.CategoryIconGearName);
                if (requestedIcon != null) return AssignCostIcon(costRuntime, requestedIcon);
            }

            if (costRuntime.CategoryAllowedGearNames != null)
            {
                for (int i = 0; i < costRuntime.CategoryAllowedGearNames.Length; i++)
                {
                    Texture2D allowedIcon = TryGetGearIcon(costRuntime.CategoryAllowedGearNames[i]);
                    if (allowedIcon != null) return AssignCostIcon(costRuntime, allowedIcon);
                }
            }

            try
            {
                if (costRuntime.Category?.m_CategoryIcon != null) return AssignCostIcon(costRuntime, costRuntime.Category.m_CategoryIcon);
            }
            catch { }

            return null;
        }

        private static Texture2D TryResolveLiquidCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null) return null;

            Texture2D icon = LiquidResolver.GetIcon(costRuntime.LiquidType);
            if (icon != null) return AssignCostIcon(costRuntime, icon);

            return null;
        }

        private static Texture2D TryGetGearIcon(string gearName)
        {
            if (string.IsNullOrEmpty(gearName)) return null;
            if (!GearResolver.TryResolvePrefab(gearName, out GearItem prefab)) return null;
            return GearResolver.GetInventoryIcon(prefab);
        }

        private static Texture2D AssignCostIcon(CustomTradeCostRuntime costRuntime, Texture2D icon)
        {
            if (costRuntime == null || icon == null) return null;

            costRuntime.Icon = icon;
            if (IsCategoryCost(costRuntime))
            {
                try
                {
                    if (costRuntime.Category != null) costRuntime.Category.m_CategoryIcon = icon;
                }
                catch { }
            }

            return icon;
        }

        private static void LogMissingCostIcon(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.TradeDefinition == null || costRuntime.Definition == null) return;

            string key = costRuntime.TradeDefinition.Id + ":" + costRuntime.ReferenceKey + ":" + costRuntime.Definition.ExchangeType;
            if (!s_LoggedMissingCostIconWarnings.Add(key)) return;

            Core.Log?.Warning($"Custom trade '{costRuntime.TradeDefinition.Id}' requirement '{costRuntime.DisplayName}' could not resolve an icon at UI time. The requirement will be displayed without a vanilla fallback icon.");
        }

        internal static string FormatRewardDisplay(string displayName, int amount)
        {
            displayName = string.IsNullOrEmpty(displayName) ? "Unknown" : displayName;
            amount = Math.Max(1, amount);
            return amount == 1 ? displayName : $"{displayName} ({amount})";
        }

        internal static string GetCostDisplayName(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null) return "";
            string result = costRuntime.DisplayName ?? "";
            LogCostDisplayBehavior(costRuntime, "requirement name", result);
            return result;
        }

        internal static string GetCostFormattedString(ExchangeItem exchangeItem, CustomTradeCostRuntime costRuntime, int multiplier)
        {
            if (exchangeItem == null || costRuntime == null) return "";

            int safeMultiplier = Math.Max(1, multiplier);
            string result = costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.CategoryAmount => $"{Math.Max(1, exchangeItem.m_Amount) * safeMultiplier} x {GetCostDisplayName(costRuntime)}",
                ExchangeItemType.GearWeight or ExchangeItemType.CategoryWeight => $"{FormatWeight(exchangeItem.m_Weight * safeMultiplier)} {GetCostDisplayName(costRuntime)}",
                ExchangeItemType.Liquid => $"{FormatLiquid(exchangeItem.m_LiquidVolume * safeMultiplier)} {GetCostDisplayName(costRuntime)}",
                _ => GetCostDisplayName(costRuntime),
            };

            LogCostDisplayBehavior(costRuntime, $"formatted requirement x{safeMultiplier}", result);
            return result;
        }

        internal static string GetCostFormattedStringWithExchanged(ExchangeItem exchangeItem, CustomTradeCostRuntime costRuntime)
        {
            if (exchangeItem == null || costRuntime == null) return "";

            string result = costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.CategoryAmount => $"{Math.Max(0, exchangeItem.m_ExchangedAmount)}/{Math.Max(1, exchangeItem.m_Amount)} x {GetCostDisplayName(costRuntime)}",
                ExchangeItemType.GearWeight or ExchangeItemType.CategoryWeight => $"{FormatWeight(exchangeItem.m_ExchangedWeight)} / {FormatWeight(exchangeItem.m_Weight)} {GetCostDisplayName(costRuntime)}",
                ExchangeItemType.Liquid => $"{FormatLiquid(exchangeItem.m_ExchangedLiquidVolume)} / {FormatLiquid(exchangeItem.m_LiquidVolume)} {GetCostDisplayName(costRuntime)}",
                _ => GetCostDisplayName(costRuntime),
            };

            LogCostDisplayBehavior(costRuntime, "formatted progress requirement", result);
            return result;
        }

        internal static bool TryGetCategoryForCost(ExchangeItem exchangeItem, out ExchangeItemCategory category)
        {
            category = null;
            if (!TryGetCost(exchangeItem, out CustomTradeCostRuntime costRuntime)) return false;
            if (costRuntime.Definition.ExchangeType != ExchangeItemType.CategoryAmount && costRuntime.Definition.ExchangeType != ExchangeItemType.CategoryWeight) return false;

            category = costRuntime.Category;
            return category != null;
        }

        internal static bool TryGetLiquidForCost(ExchangeItem exchangeItem, out LiquidType liquidType)
        {
            liquidType = null;
            if (!TryGetCost(exchangeItem, out CustomTradeCostRuntime costRuntime)) return false;
            if (costRuntime.Definition.ExchangeType != ExchangeItemType.Liquid) return false;

            liquidType = costRuntime.LiquidType;
            return liquidType != null;
        }

        private static bool IsKnownCostReference(ExchangeItem exchangeItem, CustomTradeCostRuntime costRuntime)
        {
            if (exchangeItem == null || costRuntime == null || costRuntime.Definition == null) return false;
            return costRuntime.Definition.ExchangeType switch
            {
                ExchangeItemType.GearAmount or ExchangeItemType.GearWeight => IsReferenceKey(exchangeItem.m_GearItemData, costRuntime.ReferenceKey),
                ExchangeItemType.CategoryAmount or ExchangeItemType.CategoryWeight => IsReferenceKey(exchangeItem.m_ItemCategory, costRuntime.ReferenceKey),
                ExchangeItemType.Liquid => IsReferenceKey(exchangeItem.m_LiquidType, costRuntime.ReferenceKey),
                _ => false,
            };
        }

        private static bool IsReferenceKey(UnityEngine.AddressableAssets.AssetReference reference, string referenceKey)
        {
            if (reference == null || string.IsNullOrEmpty(referenceKey)) return false;

            try
            {
                string assetGuid = reference.AssetGUID;
                if (assetGuid == referenceKey) return true;
            }
            catch { }

            try
            {
                object runtimeKey = reference.RuntimeKey;
                string runtimeKeyString = runtimeKey?.ToString();
                return runtimeKeyString == referenceKey;
            }
            catch { }

            return false;
        }

        private static bool IsMatchingCategoryCost(CustomTradeCostRuntime costRuntime, GearItem gearItem)
        {
            if (costRuntime == null || gearItem == null) return false;

            for (int i = 0; i < costRuntime.CategoryAllowedData.Count; i++)
            {
                GearItemData expectedData = costRuntime.CategoryAllowedData[i];
                string expectedGearName = costRuntime.CategoryAllowedGearNames != null && i < costRuntime.CategoryAllowedGearNames.Length ? costRuntime.CategoryAllowedGearNames[i] : "";
                if (GearResolver.IsSameGear(gearItem, expectedData, expectedGearName)) return true;
            }

            return false;
        }

        private static string FormatLiquid(ItemLiquidVolume volume)
        {
            try
            {
                return AppendUnitIfMissing(volume.ToStringMetric(2u), "L");
            }
            catch { }

            return "0.00 L";
        }

        private static string FormatWeight(ItemWeight weight)
        {
            try
            {
                bool imperial = UsesImperialUnits();
                string unit = imperial ? "lb" : "kg";
                string value = imperial ? weight.ToStringImperial(2u) : weight.ToStringMetric(2u);
                return AppendUnitIfMissing(value, unit);
            }
            catch { }

            return "0.00 kg";
        }

        private static bool UsesImperialUnits()
        {
            try
            {
                SettingsState settings = SettingsState.Instance;
                return settings != null && settings.m_Units == MeasurementUnits.Imperial;
            }
            catch { }

            return false;
        }

        private static string AppendUnitIfMissing(string value, string unit)
        {
            string text = (value ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return unit == "lb" ? "0.00 lb" : $"0.00 {unit}";
            if (text.EndsWith("kg", StringComparison.OrdinalIgnoreCase)) return text;
            if (text.EndsWith("lb", StringComparison.OrdinalIgnoreCase)) return text;
            if (text.EndsWith("lbs", StringComparison.OrdinalIgnoreCase)) return text;
            if (text.EndsWith("L", StringComparison.OrdinalIgnoreCase)) return text;
            return $"{text} {unit}";
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

        private static void AppendTrades(Il2CppTradeExchangeDataList result, TraderManager traderManager, bool specialRequests)
        {
            if (result == null) return;

            EnsureBuilt();

            int trust = GetCurrentTrust(traderManager);
            List<CustomTradeRuntime> eligible = [];
            List<string> eligibleIds = [];
            List<string> skippedDetails = [];
            Dictionary<string, int> skippedReasons = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < s_RuntimeTrades.Count; i++)
            {
                CustomTradeRuntime runtime = s_RuntimeTrades[i];
                if (runtime == null || runtime.Definition == null) continue;
                if (runtime.Definition.IsSpecialRequest != specialRequests) continue;

                if (!IsEligible(runtime, trust, out string reason))
                {
                    IncrementReason(skippedReasons, reason);
                    skippedDetails.Add($"{runtime.Definition.Id}:{reason}");
                    continue;
                }

                if (ContainsTrade(result, runtime.Definition.Id))
                {
                    IncrementReason(skippedReasons, "alreadyInReturnedList");
                    skippedDetails.Add($"{runtime.Definition.Id}:alreadyInReturnedList");
                    continue;
                }

                eligible.Add(runtime);
                eligibleIds.Add(runtime.Definition.Id);
            }

            List<CustomTradeRuntime> selected = SelectTradesForAppend(eligible, traderManager, specialRequests);
            DiagnosticLog($"Append {(specialRequests ? "special request" : "normal trade")} pass -> trust:{trust}, eligible:{eligible.Count}, selected:{selected.Count}, existing vanilla/result count before append:{result.Count}, skipped:{FormatReasonCounts(skippedReasons)}.");
            DiagnosticLog($"Eligible {(specialRequests ? "special request" : "normal trade")} custom trades -> {JoinLimited(eligibleIds)}.");
            if (skippedDetails.Count > 0) DiagnosticLog($"Skipped {(specialRequests ? "special request" : "normal trade")} custom trades -> {JoinLimited(skippedDetails)}.");

            for (int i = 0; i < selected.Count; i++)
            {
                result.Add(selected[i].TradeData);
                if (specialRequests) EnsureSpecialRequestInState(traderManager, selected[i]);
                DiagnosticLog($"Injected custom {(specialRequests ? "special request" : "trade")} '{selected[i].Definition.Id}' -> selection:{DescribeSelectionBehavior(selected[i])}, display:{FormatRewardDisplay(selected[i].DisplayRewardName, selected[i].VisibleDisplayRewardAmount)}, reward:{DescribeActualReward(selected[i])}, maxPerTrade:{Math.Max(1, selected[i].Definition.MaxPerTrade)}, costs:{DescribeRuntimeCosts(selected[i])}.");
            }

            if (specialRequests && selected.Count > 0)
            {
                string ids = JoinRuntimeIds(selected);
                if (!string.Equals(s_LastLoggedActiveSpecialRequestId, ids, StringComparison.Ordinal))
                {
                    s_LastLoggedActiveSpecialRequestId = ids;
                    Core.Log?.Msg($"Injected custom special requests ({selected.Count}): {ids}.");
                }
            }
        }

        private static string DescribeSelectionBehavior(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null) return "unknown";
            CustomTradeDefinition definition = runtime.Definition;
            if (definition.SelectionMode == CustomTradeSelectionMode.AppearanceChance) return $"AppearanceChance:{Clamp01(definition.AppearanceChance):0.###}";
            return $"DrawPool:{definition.DrawPool}";
        }

        private static string DescribeRuntimeCosts(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.CostItems == null || runtime.CostItems.Length <= 0) return "none";

            List<string> parts = [];
            for (int i = 0; i < runtime.CostItems.Length; i++) parts.Add($"#{i} {DescribeCostRequirement(runtime.CostItems[i])}");
            return string.Join(" | ", parts);
        }

        private static List<CustomTradeRuntime> SelectTradesForAppend(List<CustomTradeRuntime> eligible, TraderManager traderManager, bool specialRequests)
        {
            List<CustomTradeRuntime> selected = [];
            if (eligible == null || eligible.Count <= 0) return selected;

            if (specialRequests)
            {
                for (int i = 0; i < eligible.Count; i++) AddIfMissing(selected, eligible[i]);
                return selected;
            }

            EnsureSaveDataLoaded("trade selection");

            for (int i = 0; i < eligible.Count; i++)
            {
                CustomTradeRuntime runtime = eligible[i];
                if (runtime == null || runtime.Definition == null) continue;
                if (runtime.Definition.SelectionMode == CustomTradeSelectionMode.AppearanceChance) AddIfMissing(selected, runtime);
                else if (runtime.Definition.DrawPool == CustomTradeDrawPool.AlwaysShown) AddIfMissing(selected, runtime);
            }

            bool usedCachedSelection = AddCachedDrawPoolTrades(eligible, selected);
            DiagnosticLog($"Normal trade selection cache {(usedCachedSelection ? "used" : "not used")}.");
            if (!usedCachedSelection)
            {
                List<string> drawSelectionIds = [];
                AddDrawPoolTrades(eligible, selected, drawSelectionIds, CustomTradeDrawPool.Common, GetDrawCount(traderManager, CustomTradeDrawPool.Common));
                AddDrawPoolTrades(eligible, selected, drawSelectionIds, CustomTradeDrawPool.Rare, GetDrawCount(traderManager, CustomTradeDrawPool.Rare));
                AddDrawPoolTrades(eligible, selected, drawSelectionIds, CustomTradeDrawPool.ExtraRare, GetDrawCount(traderManager, CustomTradeDrawPool.ExtraRare));
                s_SaveData.SelectedNormalTradeIds = drawSelectionIds;
                DiagnosticLog($"Rolled normal draw-pool custom trades: {string.Join(", ", drawSelectionIds)}.");
            }

            return selected;
        }

        private static bool AddCachedDrawPoolTrades(List<CustomTradeRuntime> eligible, List<CustomTradeRuntime> selected)
        {
            if (eligible == null || selected == null) return false;
            if (s_SaveData.SelectedNormalTradeIds == null || s_SaveData.SelectedNormalTradeIds.Count <= 0) return false;

            List<CustomTradeRuntime> restoredRuntimes = [];
            List<string> restored = [];
            List<string> missing = [];
            for (int i = 0; i < s_SaveData.SelectedNormalTradeIds.Count; i++)
            {
                string id = s_SaveData.SelectedNormalTradeIds[i];
                if (string.IsNullOrEmpty(id)) continue;

                CustomTradeRuntime runtime = FindEligibleRuntime(eligible, id);
                if (runtime == null || runtime.Definition.SelectionMode != CustomTradeSelectionMode.DrawPool || runtime.Definition.DrawPool == CustomTradeDrawPool.AlwaysShown)
                {
                    missing.Add(id);
                    continue;
                }

                restoredRuntimes.Add(runtime);
                restored.Add(id);
            }

            DiagnosticLog($"Normal draw-pool cache restore -> restored:{JoinLimited(restored)}, missingOrIneligible:{JoinLimited(missing)}.");
            if (missing.Count > 0)
            {
                DiagnosticLog("Normal draw-pool cache was incomplete, so it will be rerolled instead of silently reducing the visible custom stock.");
                return false;
            }

            for (int i = 0; i < restoredRuntimes.Count; i++) AddIfMissing(selected, restoredRuntimes[i]);
            return restoredRuntimes.Count > 0;
        }

        private static CustomTradeRuntime FindEligibleRuntime(List<CustomTradeRuntime> eligible, string id)
        {
            if (eligible == null || string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < eligible.Count; i++)
            {
                CustomTradeRuntime runtime = eligible[i];
                if (runtime?.Definition == null) continue;
                if (string.Equals(runtime.Definition.Id, id, StringComparison.OrdinalIgnoreCase)) return runtime;
            }

            return null;
        }

        private static void AddDrawPoolTrades(List<CustomTradeRuntime> eligible, List<CustomTradeRuntime> selected, List<string> drawSelectionIds, CustomTradeDrawPool pool, int count)
        {
            if (eligible == null || selected == null || count <= 0) return;

            List<CustomTradeRuntime> candidates = [];
            for (int i = 0; i < eligible.Count; i++)
            {
                CustomTradeRuntime runtime = eligible[i];
                if (runtime == null || runtime.Definition == null) continue;
                if (runtime.Definition.SelectionMode != CustomTradeSelectionMode.DrawPool) continue;
                if (runtime.Definition.DrawPool != pool) continue;
                if (runtime.Definition.DrawPool == CustomTradeDrawPool.AlwaysShown) continue;
                if (selected.Contains(runtime)) continue;
                candidates.Add(runtime);
            }

            List<string> candidateIds = [];
            for (int i = 0; i < candidates.Count; i++) if (candidates[i]?.Definition != null) candidateIds.Add(candidates[i].Definition.Id);

            int requestedCount = count;
            List<string> pickedIds = [];
            while (count > 0 && candidates.Count > 0)
            {
                int index = candidates.Count == 1 ? 0 : UnityEngine.Random.Range(0, candidates.Count);
                CustomTradeRuntime runtime = candidates[index];
                AddIfMissing(selected, runtime);
                if (runtime?.Definition != null)
                {
                    pickedIds.Add(runtime.Definition.Id);
                    if (drawSelectionIds != null && !ContainsString(drawSelectionIds, runtime.Definition.Id)) drawSelectionIds.Add(runtime.Definition.Id);
                }
                candidates.RemoveAt(index);
                count--;
            }

            DiagnosticLog($"Draw-pool selection '{pool}' -> requested:{requestedCount}, candidates:{JoinLimited(candidateIds)}, picked:{JoinLimited(pickedIds)}.");
        }

        private static void AddIfMissing(List<CustomTradeRuntime> list, CustomTradeRuntime runtime)
        {
            if (list == null || runtime == null || list.Contains(runtime)) return;
            list.Add(runtime);
        }

        private static int GetDrawCount(TraderManager traderManager, CustomTradeDrawPool pool)
        {
            try
            {
                if (traderManager == null) return 0;
                TraderTunables tunables = traderManager.Tunables;
                return pool switch
                {
                    CustomTradeDrawPool.Common => Math.Max(0, tunables.m_CommonExchangesDrawn),
                    CustomTradeDrawPool.Rare => Math.Max(0, tunables.m_RareExchangesDrawn),
                    CustomTradeDrawPool.ExtraRare => Math.Max(0, tunables.m_ExtraRareExchangesDrawn),
                    _ => 0,
                };
            }
            catch { }

            return 0;
        }


        private static bool IsCategoryCost(CustomTradeCostRuntime costRuntime)
        {
            if (costRuntime == null || costRuntime.Definition == null) return false;
            return costRuntime.Definition.ExchangeType == ExchangeItemType.CategoryAmount || costRuntime.Definition.ExchangeType == ExchangeItemType.CategoryWeight;
        }

        private static bool IsVanillaCategoryIcon(CategoryIcon categoryIcon)
        {
            return categoryIcon switch
            {
                CategoryIcon.AnyHide or CategoryIcon.AnyHarvestPlant or CategoryIcon.AnyFishAnimalMeat or CategoryIcon.AnyFishMeat => true,
                _ => false,
            };
        }

        private static bool TryGetRequestedVanillaCategoryIcon(CustomTradeCostRuntime costRuntime, out Texture2D icon)
        {
            icon = null;
            if (costRuntime == null || costRuntime.Definition == null) return false;

            CategoryIcon categoryIcon = costRuntime.CategoryIcon;
            if (!IsVanillaCategoryIcon(categoryIcon)) return false;

            if (s_VanillaCategoryIcons.TryGetValue(categoryIcon, out icon)) return icon != null;

            try
            {
                TraderManager traderManager = GameManager.m_TraderManager;
                if (traderManager != null)
                {
                    if (TryFindVanillaCategoryIcon(traderManager.GetAvailableTradeExchanges(), categoryIcon, out icon)
                        || TryFindVanillaCategoryIcon(traderManager.GetSpecialRequests(), categoryIcon, out icon))
                    {
                        s_VanillaCategoryIcons[categoryIcon] = icon;
                        return icon != null;
                    }
                }

                if (TryFindVanillaCategoryIconFromLoadedCategoryAssets(categoryIcon, out icon))
                {
                    s_VanillaCategoryIcons[categoryIcon] = icon;
                    return icon != null;
                }
            }
            catch { }

            LogMissingVanillaCategoryIcon(costRuntime, categoryIcon);
            return false;
        }

        private static bool TryFindVanillaCategoryIconFromLoadedCategoryAssets(CategoryIcon categoryIcon, out Texture2D icon)
        {
            icon = null;

            try
            {
                var categories = Resources.FindObjectsOfTypeAll<ExchangeItemCategory>();
                if (categories == null) return false;

                for (int i = 0; i < categories.Length; i++)
                {
                    ExchangeItemCategory category = categories[i];
                    if (category == null || category.m_CategoryIcon == null) continue;
                    if (!DoesCategoryMatchRequestedIcon(category, categoryIcon)) continue;

                    icon = category.m_CategoryIcon;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryFindVanillaCategoryIcon(Il2CppTradeExchangeDataList trades, CategoryIcon categoryIcon, out Texture2D icon)
        {
            icon = null;
            if (trades == null) return false;

            for (int i = 0; i < trades.Count; i++)
            {
                TradeExchangeData trade = trades[i];
                if (trade == null || TryGetTrade(trade, out _)) continue;

                if (TryFindVanillaCategoryIcon(trade.m_ExchangeItems, categoryIcon, out icon)) return true;
                if (TryFindVanillaCategoryIcon(trade.m_CheapExchangeItems, categoryIcon, out icon)) return true;
            }

            return false;
        }

        private static bool TryFindVanillaCategoryIcon(Il2CppSystem.Collections.Generic.List<ExchangeItem> exchangeItems, CategoryIcon categoryIcon, out Texture2D icon)
        {
            icon = null;
            if (exchangeItems == null) return false;

            for (int i = 0; i < exchangeItems.Count; i++)
            {
                ExchangeItem item = exchangeItems[i];
                if (item == null) continue;
                if (item.m_ExchangeItemType != ExchangeItemType.CategoryAmount && item.m_ExchangeItemType != ExchangeItemType.CategoryWeight) continue;

                try
                {
                    ExchangeItemCategory category = item.GetExchangeItemCategoryIfValid();
                    if (category == null || category.m_CategoryIcon == null) continue;
                    if (!DoesCategoryMatchRequestedIcon(category, categoryIcon)) continue;

                    icon = category.m_CategoryIcon;
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static bool DoesCategoryMatchRequestedIcon(ExchangeItemCategory category, CategoryIcon categoryIcon)
        {
            if (category == null) return false;

            string iconName = GetUnityObjectName(category.m_CategoryIcon);
            if (StringEquals(iconName, GetVanillaCategoryIconTextureName(categoryIcon))) return true;

            string categoryAssetName = GetUnityObjectName(category);
            if (StringEquals(categoryAssetName, GetVanillaCategoryAssetName(categoryIcon))) return true;

            return false;
        }

        private static string GetVanillaCategoryIconTextureName(CategoryIcon categoryIcon)
        {
            return categoryIcon switch
            {
                CategoryIcon.AnyHide => "ico_AnyHide",
                CategoryIcon.AnyHarvestPlant => "ico_AnyHarvestPlant",
                CategoryIcon.AnyFishAnimalMeat => "ico_AnyFishAnimalMeat",
                CategoryIcon.AnyFishMeat => "ico_AnyFishMeat",
                _ => "",
            };
        }

        private static string GetVanillaCategoryAssetName(CategoryIcon categoryIcon)
        {
            return categoryIcon switch
            {
                CategoryIcon.AnyHide => "DriedHideCategory",
                CategoryIcon.AnyHarvestPlant => "HarvestableCategory",
                CategoryIcon.AnyFishAnimalMeat => "RawAnimalCategory",
                CategoryIcon.AnyFishMeat => "RawFishCategory",
                _ => "",
            };
        }

        private static string GetUnityObjectName(UnityEngine.Object unityObject)
        {
            if (unityObject == null) return "";

            try
            {
                string name = unityObject.name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }

            return "";
        }

        private static bool StringEquals(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(expected) && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void LogMissingVanillaCategoryIcon(CustomTradeCostRuntime costRuntime, CategoryIcon categoryIcon)
        {
            if (costRuntime == null || costRuntime.TradeDefinition == null || costRuntime.Definition == null) return;

            string key = costRuntime.TradeDefinition.Id + ":" + categoryIcon;
            if (s_LoggedVanillaCategoryIconWarnings.Contains(key)) return;

            s_LoggedVanillaCategoryIconWarnings.Add(key);
            Core.Log?.Warning($"Custom trade '{costRuntime.TradeDefinition.Id}' category cost '{costRuntime.DisplayName}' requested category icon '{categoryIcon}', but no loaded vanilla Trader category icon was found for it. Using first-item fallback icon instead.");
        }

        private static void RemoveCompletionOnlyDisplayRewardItems(ExchangeBox exchangeBox, string source)
        {
            if (exchangeBox == null) return;
            ArgumentNullException.ThrowIfNull(source);

            ActiveTradeState activeTrade;
            try
            {
                activeTrade = exchangeBox.m_ActiveTrade;
            }
            catch { return; }

            if (activeTrade == null || activeTrade.m_ExchangeStates == null) return;

            for (int exchangeIndex = 0; exchangeIndex < activeTrade.m_ExchangeStates.Count; exchangeIndex++)
            {
                if (!TryGetRuntimeForExchangeBoxIndex(exchangeBox, exchangeIndex, out CustomTradeRuntime slotRuntime)) continue;

                Il2CppSystem.Collections.Generic.List<GearItemObject> exchangedItems;
                try
                {
                    exchangedItems = exchangeBox.GetExchangedItems(exchangeIndex);
                }
                catch { continue; }

                if (exchangedItems == null || exchangedItems.Count <= 0) continue;

                for (int i = exchangedItems.Count - 1; i >= 0; i--)
                {
                    GearItemObject itemObject = exchangedItems[i];
                    GearItem gearItem = itemObject?.m_GearItem;
                    if (gearItem == null) continue;
                    if (slotRuntime.HasPhysicalReward && IsPhysicalRewardGear(slotRuntime, gearItem)) continue;
                    if (!TryGetCompletionOnlyRuntimeForDisplayGear(exchangeBox, gearItem, out CustomTradeRuntime completionOnlyRuntime, out int completionOnlyIndex)) continue;

                    RemoveDuplicateRewardReferences(exchangeBox, exchangeIndex, exchangedItems, gearItem);
                    DeactivateDuplicateRewardObject(gearItem);
                    LogNoRewardCleanupOnce(completionOnlyRuntime, completionOnlyIndex >= 0 ? completionOnlyIndex : exchangeIndex, source);
                }
            }
        }

        private static void LogNoRewardCleanupOnce(CustomTradeRuntime runtime, int exchangeIndex, string source)
        {
            if (runtime == null || runtime.Definition == null) return;

            string key = runtime.Definition.Id + "|cleanup|" + exchangeIndex + "|" + source;
            if (s_LoggedNoRewardSuppression.Contains(key)) return;

            s_LoggedNoRewardSuppression.Add(key);
            Core.Log?.Msg($"Removed stray display reward item for completion-only custom trade '{runtime.Definition.Id}' from exchange slot {exchangeIndex} during {source}.");
        }

        private static bool ShouldNormalizeStackableReward(CustomTradeRuntime runtime)
        {
            if (runtime == null || runtime.Definition == null || runtime.Definition.Reward == null) return false;
            if (!runtime.HasPhysicalReward) return false;
            if (!runtime.RewardIsStackable) return false;
            return runtime.Definition.Reward.Amount > 1;
        }

        private static void NormalizeStackableReward(ExchangeBox exchangeBox, CustomTradeRuntime runtime, int expectedUnits, string source)
        {
            if (exchangeBox == null || runtime == null) return;
            ArgumentNullException.ThrowIfNull(source);

            int maxSlots = GetExchangeBoxScanSlotCount(exchangeBox);
            for (int exchangeIndex = 0; exchangeIndex < maxSlots; exchangeIndex++)
            {
                if (!TryGetExchangedItems(exchangeBox, exchangeIndex, out Il2CppSystem.Collections.Generic.List<GearItemObject> exchangedItems)) continue;
                if (exchangedItems == null || exchangedItems.Count <= 0) continue;

                GearItem keptItem = null;
                StackableItem keptStack = null;
                List<GearItem> duplicates = [];

                for (int i = 0; i < exchangedItems.Count; i++)
                {
                    GearItemObject itemObject = exchangedItems[i];
                    if (itemObject == null) continue;

                    GearItem gearItem = itemObject.m_GearItem;
                    if (gearItem == null) continue;
                    if (!IsPhysicalRewardGear(runtime, gearItem)) continue;

                    StackableItem stackableItem = GetStackableItemSafe(gearItem);
                    if (stackableItem == null) continue;

                    if (keptItem == null)
                    {
                        keptItem = gearItem;
                        keptStack = stackableItem;
                        continue;
                    }

                    duplicates.Add(gearItem);
                }

                if (keptItem == null || keptStack == null) continue;

                keptStack.m_Units = expectedUnits;

                int removed = 0;
                for (int i = duplicates.Count - 1; i >= 0; i--)
                {
                    GearItem duplicate = duplicates[i];
                    if (duplicate == null || duplicate == keptItem) continue;

                    RemoveDuplicateRewardReferences(exchangeBox, exchangeIndex, exchangedItems, duplicate);
                    DeactivateDuplicateRewardObject(duplicate);
                    removed++;
                }

                if (removed <= 0) continue;
                LogStackableRewardPreparationOnce(runtime, exchangeIndex, $"Normalized stackable reward for '{runtime.Definition.Id}' in exchange slot {exchangeIndex} during {source}: kept one stack at {expectedUnits} unit(s), removed {removed} duplicate reference(s) from the exchange box.");
            }
        }

        private static void RemoveDuplicateRewardReferences(ExchangeBox exchangeBox, int exchangeIndex, Il2CppSystem.Collections.Generic.List<GearItemObject> exchangedItems, GearItem duplicate)
        {
            if (duplicate == null) return;

            try
            {
                exchangeBox.RemoveItem(exchangeIndex, duplicate);
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not remove duplicate stackable reward item from exchange box: {e.Message}");
            }

            try
            {
                RemoveGearItemFromGearItemObjectList(exchangedItems, duplicate);
            }
            catch { }

            try
            {
                RemoveGearItemFromLockedList(exchangeBox, duplicate);
            }
            catch { }
        }

        private static void RemoveGearItemFromGearItemObjectList(Il2CppSystem.Collections.Generic.List<GearItemObject> list, GearItem duplicate)
        {
            if (list == null || duplicate == null) return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                GearItemObject itemObject = list[i];
                if (itemObject == null) continue;
                if (itemObject.m_GearItem != duplicate) continue;

                list.RemoveAt(i);
            }
        }

        private static void RemoveGearItemFromLockedList(ExchangeBox exchangeBox, GearItem duplicate)
        {
            if (exchangeBox == null || duplicate == null) return;

            Il2CppSystem.Collections.Generic.List<GearItem> lockedItems = exchangeBox.m_ItemsLockedInExchangeBox;
            if (lockedItems == null) return;

            for (int i = lockedItems.Count - 1; i >= 0; i--)
            {
                GearItem item = lockedItems[i];
                if (item != duplicate) continue;

                lockedItems.RemoveAt(i);
            }
        }

        private static void DeactivateDuplicateRewardObject(GearItem duplicate)
        {
            if (duplicate == null) return;

            try
            {
                duplicate.gameObject?.SetActive(false);
            }
            catch { }
        }

        private static bool IsPreparingRewards(ExchangeBox exchangeBox)
        {
            return exchangeBox != null && s_RewardPreparationDepth > 0;
        }

        private static bool TryGetRuntimeForExchangeBoxIndex(ExchangeBox exchangeBox, int exchangeIndex, out CustomTradeRuntime runtime)
        {
            runtime = null;
            if (exchangeBox == null || exchangeIndex < 0) return false;

            try
            {
                TradeExchangeData exchangeData = exchangeBox.GetExchangeData(exchangeIndex);
                if (TryGetTrade(exchangeData, out runtime)) return true;
            }
            catch { }

            try
            {
                ActiveTradeState activeTrade = exchangeBox.m_ActiveTrade;
                if (activeTrade == null || activeTrade.m_ExchangeStates == null) return false;
                if (exchangeIndex >= activeTrade.m_ExchangeStates.Count) return false;

                ActiveTradeState.ActiveExchangeState exchangeState = activeTrade.m_ExchangeStates[exchangeIndex];
                return exchangeState != null && TryGetTrade(exchangeState.m_Exchange, out runtime);
            }
            catch { }

            return false;
        }

        private static int GetExchangeBoxScanSlotCount(ExchangeBox exchangeBox)
        {
            int count = 1;

            try
            {
                ActiveTradeState activeTrade = exchangeBox?.m_ActiveTrade;
                if (activeTrade?.m_ExchangeStates != null) count = Math.Max(count, activeTrade.m_ExchangeStates.Count);
            }
            catch { }

            return Math.Min(Math.Max(1, count), 16);
        }

        private static bool TryGetExchangedItems(ExchangeBox exchangeBox, int exchangeIndex, out Il2CppSystem.Collections.Generic.List<GearItemObject> exchangedItems)
        {
            exchangedItems = null;
            if (exchangeBox == null || exchangeIndex < 0) return false;

            try
            {
                exchangedItems = exchangeBox.GetExchangedItems(exchangeIndex);
                return exchangedItems != null;
            }
            catch { }

            return false;
        }

        private static bool TryFindExistingRewardStack(ExchangeBox exchangeBox, CustomTradeRuntime runtime, GearItem incomingItem, out StackableItem existingStack)
        {
            existingStack = null;
            if (exchangeBox == null || runtime == null) return false;

            int maxSlots = GetExchangeBoxScanSlotCount(exchangeBox);
            for (int exchangeIndex = 0; exchangeIndex < maxSlots; exchangeIndex++)
            {
                if (!TryGetExchangedItems(exchangeBox, exchangeIndex, out Il2CppSystem.Collections.Generic.List<GearItemObject> exchangedItems)) continue;
                if (exchangedItems == null || exchangedItems.Count <= 0) continue;

                for (int i = 0; i < exchangedItems.Count; i++)
                {
                    GearItemObject itemObject = exchangedItems[i];
                    if (itemObject == null) continue;

                    GearItem gearItem = itemObject.m_GearItem;
                    if (gearItem == null || gearItem == incomingItem) continue;
                    if (!IsPhysicalRewardGear(runtime, gearItem)) continue;

                    StackableItem stackableItem = GetStackableItemSafe(gearItem);
                    if (stackableItem == null) continue;

                    existingStack = stackableItem;
                    return true;
                }
            }

            return false;
        }

        private static StackableItem GetStackableItemSafe(GearItem gearItem)
        {
            if (gearItem == null) return null;

            try
            {
                return gearItem.GetStackableItem();
            }
            catch { }

            return null;
        }

        private static int GetExpectedRewardAmount(CustomTradeRuntime runtime, TradeExchangeData exchangeData)
        {
            if (runtime?.Definition?.Reward == null) return 1;

            if (TryGetStackableRewardAmountOverride(exchangeData, out StackableRewardAmountOverride amountOverride)) return Math.Max(1, amountOverride.ExpectedUnits);

            int baseAmount = Math.Max(1, runtime.Definition.Reward.Amount);
            int multiplier = GetRememberedTradeSelectionMultiplier(exchangeData, runtime);
            int expected = Math.Max(1, baseAmount * Math.Max(1, multiplier));
            if (exchangeData != null) expected = Math.Max(expected, exchangeData.m_GainedItemAmount);
            return expected;
        }

        private static void LogStackableRewardPreparationOnce(CustomTradeRuntime runtime, int exchangeIndex, string message)
        {
            if (runtime == null || runtime.Definition == null) return;

            string key = runtime.Definition.Id + "|" + exchangeIndex + "|" + message;
            if (s_LoggedStackableRewardPreparation.Contains(key)) return;

            s_LoggedStackableRewardPreparation.Add(key);
            Core.Log?.Msg(message);
        }

        private static void LogStackableRewardUnitsAppliedOnce(CustomTradeRuntime runtime, int exchangeIndex, int units, string target)
        {
            if (runtime == null || runtime.Definition == null) return;

            int appliedUnits = Math.Max(1, units);
            string key = runtime.Definition.Id + "|" + exchangeIndex + "|units-applied|" + appliedUnits + "|" + target;
            if (s_LoggedStackableRewardPreparation.Contains(key)) return;

            s_LoggedStackableRewardPreparation.Add(key);
            DiagnosticLog($"Custom trade '{runtime.Definition.Id}' stackable reward units applied -> exchangeSlot:{exchangeIndex}, target:{target}, units:{appliedUnits}.");
        }

        private static void LogNoRewardSuppressionOnce(CustomTradeRuntime runtime, int exchangeIndex)
        {
            if (runtime == null || runtime.Definition == null) return;

            string key = runtime.Definition.Id + "|" + exchangeIndex;
            if (s_LoggedNoRewardSuppression.Contains(key)) return;

            s_LoggedNoRewardSuppression.Add(key);
            Core.Log?.Msg($"Suppressed physical reward spawn for completion-only custom trade '{runtime.Definition.Id}' in exchange slot {exchangeIndex}.");
        }

        private static void EnsureBuilt()
        {
            if (s_Built) return;
            s_Built = true;

            for (int i = 0; i < s_Definitions.Count; i++)
            {
                TryBuildAndAddRuntime(s_Definitions[i], false);
            }

            Core.Log?.Msg($"Built {s_RuntimeTrades.Count}/{s_Definitions.Count} runtime custom trade(s).");
            DiagnosticLog($"Runtime custom trade ids: {JoinRuntimeIds(s_RuntimeTrades)}.");
        }

        private static bool TryBuildAndAddRuntime(CustomTradeDefinition definition, bool logFailure)
        {
            if (definition == null) return false;
            if (!CustomTradeFactory.TryCreateRuntime(definition, out CustomTradeRuntime runtime))
            {
                if (logFailure) Core.Log?.Warning($"Could not build external custom trade '{definition.Id}'. Check gear names, category entries, liquid kind, display reward and reward definition.");
                return false;
            }

            if (runtime == null || runtime.Definition == null || string.IsNullOrEmpty(runtime.Definition.Id)) return false;
            if (s_RuntimeTradesById.ContainsKey(runtime.Definition.Id))
            {
                Core.Log?.Warning($"Skipped duplicate runtime custom trade id '{runtime.Definition.Id}'. Trade ids must be unique.");
                return false;
            }

            s_RuntimeTrades.Add(runtime);
            s_RuntimeTradesById[runtime.Definition.Id] = runtime;

            if (runtime.HasPhysicalReward && runtime.RewardIsStackable)
            {
                DiagnosticLog($"Custom trade '{runtime.Definition.Id}' reward '{runtime.Definition.Reward.GearName}' will be normalized as a StackableItem reward when prepared in the exchange box.");
            }

            if (!runtime.HasPhysicalReward)
            {
                DiagnosticLog($"Custom trade '{runtime.Definition.Id}' is completion-only. Trader display reward: {FormatRewardDisplay(runtime.DisplayRewardName, runtime.VisibleDisplayRewardAmount)}.");
            }

            LogRuntimeBuildDiagnostics(runtime);
            return true;
        }

        private static void EnsureSpecialRequestInState(TraderManager traderManager, CustomTradeRuntime runtime)
        {
            if (traderManager == null || runtime == null || runtime.TradeData == null) return;

            try
            {
                TraderState state = traderManager.m_CurrentState;
                if (state == null) return;

                state.m_SpecialRequests ??= new Il2CppTradeExchangeDataList();
                if (!ContainsTrade(state.m_SpecialRequests, runtime.Definition.Id))
                {
                    state.m_SpecialRequests.Add(runtime.TradeData);
                    if (!s_SyncedSpecialRequests.Contains(runtime.Definition.Id))
                    {
                        s_SyncedSpecialRequests.Add(runtime.Definition.Id);
                        Core.Log?.Msg($"Synced custom special request into TraderState: '{runtime.Definition.Id}'.");
                    }
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not sync custom special request '{runtime.Definition.Id}' into TraderState: {e.Message}");
            }
        }

        private static bool IsEligible(CustomTradeRuntime runtime, int currentTrust)
        {
            return IsEligible(runtime, currentTrust, out _);
        }

        private static bool IsEligible(CustomTradeRuntime runtime, int currentTrust, out string reason)
        {
            reason = "eligible";
            if (runtime == null || runtime.Definition == null)
            {
                reason = "invalidRuntime";
                return false;
            }

            CustomTradeDefinition definition = runtime.Definition;
            EnsureSaveDataLoaded("eligibility");
            if (!definition.Enabled)
            {
                reason = "disabled";
                return false;
            }

            if (currentTrust < definition.MinTrust)
            {
                reason = $"trustTooLow({currentTrust}<{definition.MinTrust})";
                return false;
            }

            if (!definition.Repeatable && ContainsString(s_SaveData.CompletedNonRepeatableTradeIds, definition.Id))
            {
                reason = "nonRepeatableCompleted";
                return false;
            }

            if (definition.SelectionMode == CustomTradeSelectionMode.AppearanceChance)
            {
                bool rolled = RollAppearance(definition);
                if (!rolled)
                {
                    reason = $"appearanceRollFailed({Clamp01(definition.AppearanceChance):0.###})";
                    return false;
                }
            }

            return true;
        }

        private static bool RollAppearance(CustomTradeDefinition definition)
        {
            if (definition == null) return false;
            EnsureSaveDataLoaded("appearance roll");
            if (s_SaveData.AppearanceRolls.TryGetValue(definition.Id, out bool alreadyRolled)) return alreadyRolled;

            float chance = Clamp01(definition.AppearanceChance);
            bool result = chance >= 1f || UnityEngine.Random.value <= chance;
            s_SaveData.AppearanceRolls[definition.Id] = result;
            DiagnosticLog($"Appearance roll for '{definition.Id}' -> chance:{chance:0.###}, result:{result}.");
            return result;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static int GetCurrentTrust(TraderManager traderManager)
        {
            try
            {
                TraderState state = traderManager?.m_CurrentState;
                if (state != null) return state.m_CurrentTrust;
            }
            catch { }

            return 0;
        }

        private static bool ContainsTrade(Il2CppTradeExchangeDataList trades, string id)
        {
            if (trades == null || string.IsNullOrEmpty(id)) return false;

            for (int i = 0; i < trades.Count; i++)
            {
                TradeExchangeData trade = trades[i];
                if (trade == null || !string.Equals(trade.m_TradeExchangeName, id, StringComparison.OrdinalIgnoreCase)) continue;

                EnsureTradeDataRuntimeReferences(trade, "available-trade list reuse");
                return true;
            }

            return false;
        }

        private static string BuildTradeSummary(CustomTradeRuntime runtime)
        {
            try
            {
                CustomTradeDefinition definition = runtime.Definition;
                string costs = "";
                if (definition.CostItems != null)
                {
                    for (int i = 0; i < definition.CostItems.Length; i++)
                    {
                        CustomTradeItem cost = definition.CostItems[i];
                        if (cost == null) continue;
                        if (costs.Length > 0) costs += ", ";
                        costs += BuildCostSummary(cost);
                    }
                }

                string display = runtime.DisplayRewardName ?? "unknown display";
                string reward = DescribeActualReward(runtime);
                return $"Requested: {costs}. Display: {display}. Reward: {reward}.";
            }
            catch
            {
                return "";
            }
        }

        private static string BuildCostSummary(CustomTradeItem cost)
        {
            if (cost == null) return "unknown cost";

            return cost.ExchangeType switch
            {
                ExchangeItemType.GearAmount => $"{Math.Max(1, cost.Amount)}x {cost.GearName}",
                ExchangeItemType.GearWeight => $"{Math.Max(0.01f, cost.WeightKilograms):0.##} kg {cost.GearName}",
                ExchangeItemType.CategoryAmount => $"{Math.Max(1, cost.Amount)}x {cost.CategoryName}",
                ExchangeItemType.CategoryWeight => $"{Math.Max(0.01f, cost.WeightKilograms):0.##} kg {cost.CategoryName}",
                ExchangeItemType.Liquid => $"{Math.Max(0.01f, cost.LiquidLiters):0.00} L {cost.LiquidKind}",
                _ => cost.ExchangeType.ToString(),
            };
        }
    }
}