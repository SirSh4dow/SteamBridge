using System.Text.RegularExpressions;
using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.Core;

public static class SteamInstalledScanner
{
    private static readonly Regex AppIdRegex = new(@"\""appid\""\s+\""(\d+)\""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"\""name\""\s+\""([^\""]+)\""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstallDirRegex = new(@"\""installdir\""\s+\""([^\""]+)\""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<ScannedGame> ScanInstalledGames(List<string> libraryFolders)
    {
        var games = new List<ScannedGame>();
        var seenAppIds = new HashSet<uint>();

        foreach (string lib in libraryFolders)
        {
            string steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (string acf in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
            {
                try
                {
                    string text = File.ReadAllText(acf);
                    var matchId = AppIdRegex.Match(text);
                    var matchName = NameRegex.Match(text);
                    var matchDir = InstallDirRegex.Match(text);

                    if (matchId.Success && uint.TryParse(matchId.Groups[1].Value, out uint appId))
                    {
                        if (seenAppIds.Contains(appId)) continue;
                        seenAppIds.Add(appId);

                        string name = matchName.Success ? matchName.Groups[1].Value : $"App {appId}";
                        string installDir = matchDir.Success ? Path.Combine(steamapps, "common", matchDir.Groups[1].Value) : "";

                        games.Add(new ScannedGame
                        {
                            Key = $"steam:{appId}",
                            Title = name,
                            Source = "steam",
                            AppId = appId,
                            InstallDir = installDir,
                            AlreadyInSteam = true
                        });
                    }
                }
                catch { }
            }
        }

        return games;
    }
}
