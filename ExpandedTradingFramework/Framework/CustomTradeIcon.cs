namespace ExpandedTradingFramework.Framework
{
    public enum CustomTradeIconSource
    {
        None,
        CustomIcon
    }

    public sealed class CustomTradeIcon
    {
        public CustomTradeIconSource Source = CustomTradeIconSource.None;
        public string Path = "";
        public Assembly OwnerAssembly;

        public static CustomTradeIcon None()
        {
            return new CustomTradeIcon();
        }

        public static CustomTradeIcon CustomIcon(string path)
        {
            return new CustomTradeIcon
            {
                Source = CustomTradeIconSource.CustomIcon,
                Path = path ?? "",
                OwnerAssembly = Assembly.GetCallingAssembly()
            };
        }

        public static CustomTradeIcon CustomIcon(Assembly ownerAssembly, string path)
        {
            return new CustomTradeIcon
            {
                Source = CustomTradeIconSource.CustomIcon,
                Path = path ?? "",
                OwnerAssembly = ownerAssembly
            };
        }

        public bool HasIcon => Source != CustomTradeIconSource.None && !string.IsNullOrEmpty(Path);
    }

    internal static class CustomTradeIconResolver
    {
        private static readonly Dictionary<string, Texture2D> s_LoadedIcons = new(StringComparer.OrdinalIgnoreCase);
        private static Texture2D s_ErrorIcon;

        internal static Texture2D Resolve(CustomTradeIcon icon, string ownerId, string usage)
        {
            if (icon == null || !icon.HasIcon) return null;

            Assembly ownerAssembly = icon.OwnerAssembly ?? Assembly.GetCallingAssembly();
            string cacheKey = BuildCacheKey(ownerAssembly, icon.Path);
            if (s_LoadedIcons.TryGetValue(cacheKey, out Texture2D cached)) return cached;

            Texture2D loaded = TryLoadIcon(ownerAssembly, icon.Path, ownerId, usage);
            if (loaded == null)
            {
                loaded = GetErrorIcon();
                Core.Log?.Error($"Custom icon load failed for '{ownerId}' ({usage}) -> '{icon.Path}'. Error icon will be displayed; no vanilla fallback is used.");
            }

            s_LoadedIcons[cacheKey] = loaded;
            return loaded;
        }

        private static Texture2D TryLoadIcon(Assembly ownerAssembly, string path, string ownerId, string usage)
        {
            if (string.IsNullOrEmpty(path)) return null;

            byte[] bytes = TryReadFileIcon(ownerAssembly, path) ?? TryReadEmbeddedIcon(ownerAssembly, path);
            if (bytes == null || bytes.Length == 0) return null;

            try
            {
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                texture.name = $"ETF_CustomIcon_{SanitizeName(ownerId)}_{SanitizeName(usage)}";
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Custom icon decode failed for '{ownerId}' ({usage}) -> '{path}': {e.Message}");
                return null;
            }
        }

        private static byte[] TryReadFileIcon(Assembly ownerAssembly, string path)
        {
            try
            {
                if (Path.IsPathRooted(path) && File.Exists(path)) return File.ReadAllBytes(path);

                string assemblyPath = ownerAssembly?.Location ?? "";
                string assemblyDirectory = !string.IsNullOrEmpty(assemblyPath) ? Path.GetDirectoryName(assemblyPath) ?? "" : "";
                if (string.IsNullOrEmpty(assemblyDirectory)) return null;

                string normalizedPath = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string fullPath = Path.Combine(assemblyDirectory, normalizedPath);
                return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not read custom icon file '{path}': {e.Message}");
                return null;
            }
        }

        private static byte[] TryReadEmbeddedIcon(Assembly ownerAssembly, string path)
        {
            if (ownerAssembly == null) return null;

            try
            {
                string resourceName = FindEmbeddedResourceName(ownerAssembly, path);
                if (string.IsNullOrEmpty(resourceName)) return null;

                using Stream stream = ownerAssembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                using MemoryStream memory = new();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not read embedded custom icon '{path}' from '{ownerAssembly.GetName().Name}': {e.Message}");
                return null;
            }
        }

        private static string FindEmbeddedResourceName(Assembly ownerAssembly, string path)
        {
            string normalized = NormalizeResourcePath(path);
            string suffix = "." + normalized;
            string[] names = ownerAssembly.GetManifestResourceNames();

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)) return name;
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name;
            }

            Core.Log?.Error($"Embedded custom icon resource '{path}' was not found in assembly '{ownerAssembly.GetName().Name}'. Available resources: {string.Join(", ", names)}");
            return "";
        }

        private static string NormalizeResourcePath(string path)
        {
            return (path ?? "").Trim().Replace('\\', '.').Replace('/', '.');
        }

        private static string BuildCacheKey(Assembly assembly, string path)
        {
            string assemblyName = assembly != null ? assembly.FullName ?? assembly.GetName().Name ?? "unknown" : "unknown";
            return assemblyName + "|" + (path ?? "");
        }

        private static Texture2D GetErrorIcon()
        {
            if (s_ErrorIcon != null) return s_ErrorIcon;

            Texture2D texture = new(64, 64, TextureFormat.RGBA32, false);
            texture.name = "ETF_CustomIcon_ERROR_MissingOrInvalid";
            Color magenta = new(1f, 0f, 1f, 1f);
            Color black = new(0f, 0f, 0f, 1f);
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool block = ((x / 8) + (y / 8)) % 2 == 0;
                    pixels[y * 64 + x] = block ? magenta : black;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            s_ErrorIcon = texture;
            return s_ErrorIcon;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}