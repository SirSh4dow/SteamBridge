namespace SteamBridge.Core;

public static class SteamArtManager
{
    private static readonly Dictionary<string, string> Suffixes = new()
    {
        { "portrait", "p" },
        { "wide", "" },
        { "hero", "_hero" },
        { "logo", "_logo" },
        { "icon", "_icon" }
    };

    public static string GetGridDirectory(string steamPath, string userId)
    {
        return Path.Combine(steamPath, "userdata", userId, "config", "grid");
    }

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".ico", ".webp" };

    private static string GetSuffix(string kind)
    {
        if (kind == null || !Suffixes.TryGetValue(kind, out string? suffix))
            throw new ArgumentException("Tipo de arte inválido.", nameof(kind));
        return suffix;
    }

    public static string? FindExistingArt(string gridDir, uint appId, string kind)
    {
        string suffix = GetSuffix(kind);
        if (!Directory.Exists(gridDir)) return null;
        foreach (string ext in Extensions)
        {
            string candidate = Path.Combine(gridDir, $"{appId}{suffix}{ext}");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static void SaveArt(string gridDir, uint appId, string kind, byte[] imageBytes, string ext = ".png")
    {
        string suffix = GetSuffix(kind);
        if (ext == null || !Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Extensão de arte inválida.", nameof(ext));
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0) throw new ArgumentException("Arte vazia.", nameof(imageBytes));

        string directory = Path.GetFullPath(gridDir);
        Directory.CreateDirectory(directory);
        string baseName = $"{appId}{suffix}";
        string target = Path.Combine(directory, baseName + ext.ToLowerInvariant());
        var originals = Directory.GetFiles(directory)
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(baseName, StringComparison.OrdinalIgnoreCase)
                && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToList();

        // Back up every variant before publishing or removing any of them.
        foreach (string original in originals)
            if (SafeFile.CreateBackup(original) == null)
                throw new IOException($"Arte original desapareceu antes do backup: {original}");

        SafeFile.WriteAtomic(target, imageBytes);
        foreach (string original in originals)
            if (!original.Equals(target, StringComparison.OrdinalIgnoreCase)) File.Delete(original);
    }

    // Steam shows the small icon from appcache/librarycache, not only from config/grid.
    // Existing files keep their names so the client does not discard them as a new download.
    public static void PublishLibraryIcon(string steamRoot, uint appId, byte[] png, byte[] jpeg)
    {
        if (string.IsNullOrWhiteSpace(steamRoot)) throw new ArgumentException("Pasta do Steam inválida.", nameof(steamRoot));
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(jpeg);
        if (png.Length == 0 || jpeg.Length == 0) throw new ArgumentException("Ícone inválido.");
        string id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string cache = Path.Combine(Path.GetFullPath(steamRoot), "appcache", "librarycache");
        string folder = Path.Combine(cache, id);
        Directory.CreateDirectory(folder);
        var existing = Directory.GetFiles(folder)
            .Where(file => Path.GetFileName(file).Contains("icon", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (existing.Count == 0)
            SafeFile.WriteAtomic(Path.Combine(folder, id + "_icon.jpg"), jpeg);
        else
        {
            foreach (string file in existing)
            {
                string ext = Path.GetExtension(file);
                byte[] data = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? jpeg : png;
                SafeFile.WriteAtomic(file, data);
            }
        }
        Directory.CreateDirectory(cache);
        SafeFile.WriteAtomic(Path.Combine(cache, id + "_icon.jpg"), jpeg);
    }
}
