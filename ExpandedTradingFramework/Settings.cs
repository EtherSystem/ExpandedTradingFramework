namespace ExpandedTradingFramework
{
    internal sealed class ExpandedTradingFrameworkSettings : JsonModSettings
    {
        internal static readonly ExpandedTradingFrameworkSettings Instance = new();

        [Section("Diagnostics")]
        [Name("Diagnostic logging")]
        [Description("Enable detailed Expanded Trading Framework state logging. Keep this disabled during normal play unless a modder asked you to reproduce a Trader issue and need a useful log.")]
        public bool EnableDiagnosticLogging = false;

        [Section("Emergency Recovery")]
        [Name("Emergency rescue hotkeys")]
        [Description("Enable Ctrl+Alt+T emergency Trader active-trade cleanup. This is a recovery tool only, leave it disabled unless a modder instructed you do to do it after diagnosting a Trader issue.")]
        public bool EnableEmergencyRescueHotkeys = false;
    }
}