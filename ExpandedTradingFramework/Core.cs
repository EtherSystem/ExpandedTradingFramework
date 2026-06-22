using ExpandedTradingFramework.Framework;
using Il2CppTLD.Trader;

[assembly: MelonInfo(typeof(ExpandedTradingFramework.Core), "Expanded Trading Framework", "1.0.0", "EtherSystem", null)]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: VerifyLoaderVersion("0.7.2", true)]

namespace ExpandedTradingFramework
{
    public class Core : MelonMod
    {
        internal static MelonLogger.Instance Log;

        internal static bool DiagnosticLoggingEnabled => ExpandedTradingFrameworkSettings.Instance.EnableDiagnosticLogging;
        internal static bool EmergencyRescueHotkeysEnabled => ExpandedTradingFrameworkSettings.Instance.EnableEmergencyRescueHotkeys;

        internal static void Diagnostic(string message)
        {
            if (DiagnosticLoggingEnabled) Log?.Msg(message);
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            ExpandedTradingFrameworkSettings.Instance.AddToModSettings("Expanded Trading Framework");
            LoggerInstance.Msg($"Initialized.");
            Diagnostic($"Settings -> DiagnosticLogging:{DiagnosticLoggingEnabled}, EmergencyRescueHotkeys:{EmergencyRescueHotkeysEnabled}. Current definition count:{CustomTradeRegistry.DefinitionCount}.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (sceneName.Contains("MainMenu", StringComparison.OrdinalIgnoreCase) || sceneName.Contains("Menu", StringComparison.OrdinalIgnoreCase)) CustomTradeRegistry.ResetSaveDataForMainMenu();
        }

        public override void OnUpdate()
        {
            if (!EmergencyRescueHotkeysEnabled) return;
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;
            if (!Input.GetKeyDown(KeyCode.T)) return;

            ClearActiveTradeForSaveRescue("Ctrl+Alt+T");
        }

        private static bool ClearActiveTradeForSaveRescue(string source)
        {
            try
            {
                TraderManager traderManager = GameManager.m_TraderManager;
                if (traderManager == null)
                {
                    Log?.Warning($"Emergency active Trader trade clear failed from {source}: TraderManager is not available.");
                    return false;
                }

                TraderState state = traderManager.m_CurrentState;
                if (state == null)
                {
                    Log?.Warning($"Emergency active Trader trade clear failed from {source}: TraderState is not available.");
                    return false;
                }

                int previousExchangeCount = 0;
                string previousState = "null";
                ActiveTradeState previousActiveTrade = state.m_ActiveTrade;
                if (previousActiveTrade != null)
                {
                    previousState = previousActiveTrade.m_TradeState.ToString();
                    if (previousActiveTrade.m_ExchangeStates != null) previousExchangeCount = previousActiveTrade.m_ExchangeStates.Count;
                }

                ActiveTradeState emptyActiveTrade = new()
                {
                    m_ExchangeBoxScene = "",
                    m_TradeState = ActiveTradeState.State.Closed,
                    m_ExchangeStates = new Il2CppSystem.Collections.Generic.List<ActiveTradeState.ActiveExchangeState>()
                };
                state.m_ActiveTrade = emptyActiveTrade;

                CustomTradeRegistry.ResetRewardPreparationStateForSaveRescue();
                CustomTradeRegistry.ClearSpecialRequestStateForSaveRescue(state, source);

                Log?.Msg($"Emergency active Trader trade clear executed from {source}. Previous state:{previousState}, previous exchange count:{previousExchangeCount}. Force a normal game save after this, then reload the save if desired.");
                return true;
            }
            catch (Exception e)
            {
                Log?.Error($"Emergency active Trader trade clear failed from {source}: {e}");
                return false;
            }
        }
    }
}