using Il2CppTLD.Gear;

namespace ExpandedTradingFramework.Framework
{
    internal static class GearResolver
    {
        private static readonly Dictionary<string, GearItem> s_PrefabCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GearItemData> s_DataCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_Failed = new(StringComparer.OrdinalIgnoreCase);

        internal static bool TryResolve(string gearName, out GearItemData gearItemData)
        {
            gearItemData = null;
            if (!TryResolvePrefab(gearName, out GearItem prefab)) return false;

            try
            {
                gearItemData = prefab.GearItemData;
                if (gearItemData == null)
                {
                    s_Failed.Add(gearName);
                    Core.Log?.Error($"GearItem prefab '{gearName}' has no GearItemData.");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                s_Failed.Add(gearName);
                Core.Log?.Error($"Exception while reading GearItemData for '{gearName}': {e}");
                return false;
            }
        }

        internal static bool TryResolvePrefab(string gearName, out GearItem prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(gearName)) return false;

            if (s_PrefabCache.TryGetValue(gearName, out prefab)) return prefab != null;

            try
            {
                prefab = GearItem.LoadGearItemPrefab(gearName);
                if (prefab == null)
                {
                    s_Failed.Add(gearName);
                    Core.Log?.Error($"GearItem.LoadGearItemPrefab('{gearName}') returned null.");
                    return false;
                }

                GearItemData gearItemData = prefab.GearItemData;
                if (gearItemData == null)
                {
                    s_Failed.Add(gearName);
                    Core.Log?.Error($"GearItem prefab '{gearName}' has no GearItemData.");
                    return false;
                }

                s_PrefabCache[gearName] = prefab;
                s_DataCache[gearName] = gearItemData;
                return true;
            }
            catch (Exception e)
            {
                s_Failed.Add(gearName);
                Core.Log?.Error($"Exception while resolving '{gearName}': {e}");
                return false;
            }
        }

        internal static bool IsStackablePrefab(GearItem prefab)
        {
            try
            {
                return prefab != null && prefab.GetStackableItem() != null;
            }
            catch { }

            return false;
        }

        internal static string GetReferenceKey(GearItemData gearItemData, string fallback)
        {
            if (gearItemData != null && !string.IsNullOrEmpty(gearItemData.name)) return gearItemData.name;
            return fallback;
        }

        internal static string GetDisplayName(GearItemData gearItemData, string fallback)
        {
            try
            {
                if (gearItemData != null) return gearItemData.DisplayName;
            }
            catch { }

            return fallback;
        }

        internal static UnityEngine.Texture2D GetInventoryIcon(GearItem prefab)
        {
            if (prefab == null) return null;

            try
            {
                return prefab.GetInventoryIconTexture();
            }
            catch { }

            return null;
        }

        internal static bool IsSameGear(GearItem gearItem, GearItemData expectedData, string expectedGearName)
        {
            if (gearItem == null) return false;

            try
            {
                GearItemData actualData = gearItem.GearItemData;
                if (actualData != null && expectedData != null && !string.IsNullOrEmpty(actualData.name) && string.Equals(actualData.name, expectedData.name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }

            try
            {
                string objectName = NormalizeGearObjectName(gearItem.name);
                string expectedName = NormalizeGearObjectName(expectedGearName);
                if (!string.IsNullOrEmpty(objectName) && !string.IsNullOrEmpty(expectedName) && string.Equals(objectName, expectedName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }

            return false;
        }

        private static string NormalizeGearObjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string normalized = name.Trim();
            if (normalized.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^7].Trim();
            return normalized;
        }
    }
}