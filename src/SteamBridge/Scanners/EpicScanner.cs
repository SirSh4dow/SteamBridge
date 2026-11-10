using System.Text.Json.Nodes;
using SteamBridge.Models;

namespace SteamBridge.Scanners;

public static class EpicScanner
{
    public static List<ScannedGame> Scan()
    {
        var games = new List<ScannedGame>();
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string manifestsDir = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir)) return games;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(manifestsDir, "*.item"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var node = JsonNode.Parse(json);
                if (node == null) continue;

                if (node["bIsIncompleteInstall"]?.GetValue<bool>() == true) continue;

                string installLocation = node["InstallLocation"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) continue;

                string launchExe = node["LaunchExecutable"]?.ToString() ?? "";
                string exePath = FindExe(installLocation, launchExe);
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) continue;

                string title = node["DisplayName"]?.ToString()
                    ?? node["MandatoryAppFolderName"]?.ToString()
                    ?? Path.GetFileName(installLocation);

                string appName = node["AppName"]?.ToString() ?? "";
                string ns = node["CatalogNamespace"]?.ToString() ?? "";
                string catalogId = node["CatalogItemId"]?.ToString() ?? "";

                string uri = "";
                if (!string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(ns) && !string.IsNullOrEmpty(catalogId))
                {
                    uri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(catalogId)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true";
                }

                string key = $"epic:{exePath.ToLowerInvariant()}";
                if (seen.Contains(key)) continue;
                seen.Add(key);

                games.Add(new ScannedGame
                {
                    Key = key,
                    Title = title,
                    Source = "epic",
                    Exe = exePath,
                    LaunchArgs = node["LaunchCommand"]?.ToString() ?? "",
                    Cwd = installLocation,
                    InstallDir = installLocation,
                    Uri = uri,
                    IconPath = exePath
                });
            }
            catch { }
        }

        return games;
    }

    private static string FindExe(string installDir, string relativeExe)
    {
        string raw = relativeExe.Replace('/', '\\').Trim().Trim('"');
        if (string.IsNullOrEmpty(raw)) return "";

        string cand = Path.Combine(installDir, raw.TrimStart('\\'));
        if (File.Exists(cand)) return cand;

        string fileName = Path.GetFileName(raw);
        if (!string.IsNullOrEmpty(fileName))
        {
            try
            {
                var match = Directory.GetFiles(installDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return match;
            }
            catch { }
        }

        return "";
    }
}
