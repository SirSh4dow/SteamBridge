using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamBridge.Core;

public class AppConfig
{
    private static string ConfigDirectory => Path.GetFullPath(
        Environment.GetEnvironmentVariable("STEAMBRIDGE_HOME") is string home && !string.IsNullOrWhiteSpace(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamBridge"));

    public static string ConfigFilePath => Path.Combine(ConfigDirectory, "desktop-config.json");

    public string SteamGridDbKey { get; set; } = "";
    public string CustomSteamPath { get; set; } = "";
    public List<string> ScannedFolders { get; set; } = new();
    public List<string> ManualExecutables { get; set; } = new();
    public Dictionary<string, string> ManualTitles { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();

    public static AppConfig Load()
    {
        string? json = ReadIfPresent(ConfigFilePath)
            ?? ReadIfPresent(Path.Combine(ConfigDirectory, "config.json"));
        if (json != null)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("A configuração deve ser um objeto JSON.");

            var config = JsonSerializer.Deserialize<AppConfig>(json)!;
            var root = document.RootElement;
            // PascalCase takes precedence; Python fields remain in ExtensionData verbatim.
            if (!root.TryGetProperty(nameof(SteamGridDbKey), out _) && root.TryGetProperty("sgdb_key", out var key))
                config.SteamGridDbKey = key.Deserialize<string>() ?? throw new JsonException("sgdb_key não pode ser nulo.");
            if (!root.TryGetProperty(nameof(CustomSteamPath), out _) && root.TryGetProperty("steam_path", out var path))
                config.CustomSteamPath = path.Deserialize<string>() ?? throw new JsonException("steam_path não pode ser nulo.");
            if (!root.TryGetProperty(nameof(ScannedFolders), out _) && root.TryGetProperty("roots", out var roots))
                config.ScannedFolders = roots.Deserialize<List<string>>() ?? throw new JsonException("roots não pode ser nulo.");
            config.Validate();
            return config;
        }

        var defaults = new AppConfig();
        // An isolated home must not inspect real game directories.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STEAMBRIDGE_HOME")))
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                foreach (string name in new[] { "Games", "Jogos" })
                {
                    string folder = Path.Combine(drive.RootDirectory.FullName, name);
                    if (Directory.Exists(folder)) defaults.ScannedFolders.Add(folder);
                }
            }
        }
        return defaults;
    }

    public void Save()
    {
        Validate();
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(ConfigFilePath, json);
    }

    private void Validate()
    {
        if (SteamGridDbKey == null || CustomSteamPath == null || ScannedFolders == null || ScannedFolders.Any(p => p == null)
            || ManualExecutables == null || ManualExecutables.Any(p => p == null)
            || ManualTitles == null || ManualTitles.Any(p => p.Key == null || p.Value == null))
            throw new JsonException("Campos de configuração não podem ser nulos.");
    }

    private static string? ReadIfPresent(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
