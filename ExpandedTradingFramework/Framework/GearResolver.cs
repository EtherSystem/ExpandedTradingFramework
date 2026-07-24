using Il2CppTLD.Gear;

namespace ExpandedTradingFramework.Framework
{
    internal static class GearResolver
    {
        private static readonly Dictionary<string, GearItem> s_PrefabCache = new(StringComparer.OrdinalIgnoreCase);

        internal static bool TryResolve(string gearName, out GearItemData gearItemData)
        {
            gearItemData = null;
            if (!TryResolvePrefab(gearName, out GearItem prefab)) return false;

            try
            {
                gearItemData = prefab.GearItemData;
                if (IsUnityObjectAlive(gearItemData)) return true;

                s_PrefabCache.Remove(gearName);
                Core.Log?.Error($"GearItem prefab '{gearName}' has no valid GearItemData.");
            }
            catch (Exception e)
            {
                s_PrefabCache.Remove(gearName);
                Core.Log?.Error($"Exception while reading GearItemData for '{gearName}': {e}");
            }

            gearItemData = null;
            return false;
        }

        internal static bool TryResolvePrefab(string gearName, out GearItem prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(gearName)) return false;

            if (s_PrefabCache.TryGetValue(gearName, out prefab))
            {
                if (IsValidGearPrefab(prefab)) return true;
                s_PrefabCache.Remove(gearName);
            }

            try
            {
                prefab = GearItem.LoadGearItemPrefab(gearName);
                if (!IsValidGearPrefab(prefab))
                {
                    Core.Log?.Error($"GearItem.LoadGearItemPrefab('{gearName}') returned no valid prefab or GearItemData.");
                    prefab = null;
                    return false;
                }

                s_PrefabCache[gearName] = prefab;
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Exception while resolving '{gearName}': {e}");
                prefab = null;
                return false;
            }
        }

        internal static bool IsUnityObjectAlive(UnityEngine.Object value)
        {
            if (value == null) return false;

            try
            {
                _ = value.GetInstanceID();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsStackablePrefab(GearItem prefab)
        {
            try
            {
                return IsValidGearPrefab(prefab) && prefab.GetStackableItem() != null;
            }
            catch { }

            return false;
        }

        internal static string GetReferenceKey(GearItemData gearItemData, string fallback)
        {
            if (IsUnityObjectAlive(gearItemData) && !string.IsNullOrEmpty(gearItemData.name)) return gearItemData.name;
            return fallback;
        }

        internal static string GetDisplayName(GearItemData gearItemData, string fallback)
        {
            try
            {
                if (IsUnityObjectAlive(gearItemData)) return gearItemData.DisplayName;
            }
            catch { }

            return fallback;
        }

        internal static Texture2D GetInventoryIcon(GearItem prefab)
        {
            if (!IsValidGearPrefab(prefab)) return null;

            try
            {
                Texture2D icon = prefab.GetInventoryIconTexture();
                return CustomTradeIconResolver.IsTextureAlive(icon) ? icon : null;
            }
            catch { }

            return null;
        }

        internal static bool IsSameGear(GearItem gearItem, GearItemData expectedData, string expectedGearName)
        {
            if (!IsUnityObjectAlive(gearItem)) return false;

            try
            {
                GearItemData actualData = gearItem.GearItemData;
                if (IsUnityObjectAlive(actualData) && IsUnityObjectAlive(expectedData) && !string.IsNullOrEmpty(actualData.name) && string.Equals(actualData.name, expectedData.name, StringComparison.OrdinalIgnoreCase)) return true;
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

        private static bool IsValidGearPrefab(GearItem prefab)
        {
            if (!IsUnityObjectAlive(prefab)) return false;

            try
            {
                return IsUnityObjectAlive(prefab.GearItemData);
            }
            catch
            {
                return false;
            }
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
