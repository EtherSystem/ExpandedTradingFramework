using Il2CppTLD.Gear;

namespace ExpandedTradingFramework.Framework
{
    internal static class LiquidResolver
    {
        internal static bool TryResolve(CustomLiquidKind kind, out LiquidType liquidType, out string displayName)
        {
            liquidType = null;
            displayName = GetFallbackName(kind);
            if (kind == CustomLiquidKind.None) return false;

            try
            {
                LiquidType.CacheLiquidTypes();
            }
            catch { }

            try
            {
                switch (kind)
                {
                    case CustomLiquidKind.PotableWater:
                        liquidType = LiquidType.GetPotableWater();
                        displayName = "Water (Potable)";
                        break;
                    case CustomLiquidKind.NonPotableWater:
                        liquidType = LiquidType.GetNonPotableWater();
                        displayName = "Water (Non-Potable)";
                        break;
                    case CustomLiquidKind.Kerosene:
                        liquidType = LiquidType.GetKerosene();
                        displayName = "Kerosene";
                        break;
                    case CustomLiquidKind.Antiseptic:
                        liquidType = LiquidType.GetAntiseptic();
                        displayName = "Antiseptic";
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Exception while resolving liquid '{kind}': {e}");
                return false;
            }

            if (liquidType == null)
            {
                Core.Log?.Error($"Could not resolve liquid '{kind}'.");
                return false;
            }

            return true;
        }

        internal static UnityEngine.Texture2D GetIcon(LiquidType liquidType)
        {
            if (liquidType == null) return null;

            try
            {
                return liquidType.GetInventoryIcon();
            }
            catch { }

            return null;
        }

        private static string GetFallbackName(CustomLiquidKind kind)
        {
            return kind switch
            {
                CustomLiquidKind.PotableWater => "Water (Potable)",
                CustomLiquidKind.NonPotableWater => "Water (Non-Potable)",
                CustomLiquidKind.Kerosene => "Kerosene",
                CustomLiquidKind.Antiseptic => "Antiseptic",
                _ => "Liquid",
            };
        }
    }
}