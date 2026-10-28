using Il2CppTLD.Gear;

namespace ExpandedTradingFramework.Framework
{
    internal static class GearResolver
    {
        private static readonly Dictionary<string, GearItem> s_PrefabCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GearItemData> s_DataCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_Failed = new(StringComparer.OrdinalIgnoreCase);
        private static GameObject s_ReferenceRoot;

        internal static bool TryResolve(string gearName, out GearItemData gearItemData)
        {
            gearItemData = null;
            if (!TryResolvePrefab(gearName, out GearItem prefab)) return false;

            try
            {
                gearItemData = prefab.GearItemData;
                if (!IsUnityObjectAlive(gearItemData))
                {
                    RemoveCachedGear(gearName);
                    s_Failed.Add(gearName);
                    Core.Log?.Error($"GearItem prefab '{gearName}' has no valid GearItemData.");
                    return false;
                }

                s_DataCache[gearName] = gearItemData;
                return true;
            }
            catch (Exception e)
            {
                RemoveCachedGear(gearName);
                s_Failed.Add(gearName);
                Core.Log?.Error($"Exception while reading GearItemData for '{gearName}': {e}");
                return false;
            }
        }

        internal static bool TryResolvePrefab(string gearName, out GearItem prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(gearName)) return false;

            if (s_PrefabCache.TryGetValue(gearName, out prefab))
            {
                if (IsValidGearPrefab(prefab)) return true;
                RemoveCachedGear(gearName);
                prefab = null;
            }

            try
            {
                prefab = GearItem.InstantiateGearItem(gearName);
                if (!IsValidGearPrefab(prefab))
                {
                    RemoveCachedGear(gearName);
                    s_Failed.Add(gearName);
                    if (IsUnityObjectAlive(prefab)) UnityEngine.Object.Destroy(prefab.gameObject);
                    Core.Log?.Error($"GearItem.InstantiateGearItem('{gearName}') returned no valid GearItem or GearItemData.");
                    return false;
                }

                EnsureReferenceRoot();
                prefab.gameObject.SetActive(false);
                prefab.transform.SetParent(s_ReferenceRoot.transform, false);

                GearItemData gearItemData = prefab.GearItemData;
                s_PrefabCache[gearName] = prefab;
                s_DataCache[gearName] = gearItemData;
                s_Failed.Remove(gearName);
                return true;
            }
            catch (Exception e)
            {
                if (IsUnityObjectAlive(prefab)) UnityEngine.Object.Destroy(prefab.gameObject);
                RemoveCachedGear(gearName);
                s_Failed.Add(gearName);
                Core.Log?.Error($"Exception while resolving '{gearName}': {e}");
                return false;
            }
        }

        internal static int InvalidateDestroyedCacheEntries(bool clearAll = false)
        {
            if (clearAll)
            {
                HashSet<string> cachedKeys = new(StringComparer.OrdinalIgnoreCase);
                foreach (string key in s_PrefabCache.Keys) cachedKeys.Add(key);
                foreach (string key in s_DataCache.Keys) cachedKeys.Add(key);

                foreach (GearItem item in s_PrefabCache.Values) DestroyReferenceInstance(item);
                s_PrefabCache.Clear();
                s_DataCache.Clear();
                s_Failed.Clear();
                return cachedKeys.Count;
            }

            List<string> invalidKeys = [];
            foreach (KeyValuePair<string, GearItem> pair in s_PrefabCache)
            {
                if (!IsValidGearPrefab(pair.Value)) invalidKeys.Add(pair.Key);
            }

            foreach (KeyValuePair<string, GearItemData> pair in s_DataCache)
            {
                if (!IsUnityObjectAlive(pair.Value) && !ContainsString(invalidKeys, pair.Key)) invalidKeys.Add(pair.Key);
            }

            for (int i = 0; i < invalidKeys.Count; i++) RemoveCachedGear(invalidKeys[i]);
            return invalidKeys.Count;
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
                Texture2D texture = prefab.GetInventoryIconTexture();
                return CustomTradeIconResolver.IsTextureAlive(texture) ? texture : null;
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

        private static void EnsureReferenceRoot()
        {
            if (s_ReferenceRoot != null) return;
            s_ReferenceRoot = new GameObject("ETF_GearReferenceCache");
            UnityEngine.Object.DontDestroyOnLoad(s_ReferenceRoot);
            s_ReferenceRoot.SetActive(false);
        }

        private static void DestroyReferenceInstance(GearItem item)
        {
            if (!IsUnityObjectAlive(item)) return;
            UnityEngine.Object.Destroy(item.gameObject);
        }

        private static void RemoveCachedGear(string gearName)
        {
            if (string.IsNullOrEmpty(gearName)) return;
            if (s_PrefabCache.TryGetValue(gearName, out GearItem cached)) DestroyReferenceInstance(cached);
            s_PrefabCache.Remove(gearName);
            s_DataCache.Remove(gearName);
        }

        private static bool ContainsString(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }

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