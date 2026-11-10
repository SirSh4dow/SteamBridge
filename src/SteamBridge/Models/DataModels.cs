namespace SteamBridge.Models;

public record SteamUser(string Id3, string AccountName, string PersonaName, bool MostRecent)
{
    public override string ToString() => string.IsNullOrWhiteSpace(PersonaName) ? (AccountName ?? Id3) : $"{PersonaName} ({AccountName})";
}

public record SteamInfo(string InstallPath, string ExePath, List<SteamUser> Users, List<string> Libraries)
{
    public SteamUser? SelectedUser { get; set; }
}

public record ScannedGame
{
    public string Key { get; init; } = "";
    public string Title { get; set; } = "";
    public string Source { get; init; } = "folder"; // "epic", "xbox", "folder", "steam"
    public string Exe { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string LaunchArgs { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string Uri { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string IconPath { get; set; } = "";
    public uint AppId { get; set; }
    public bool Selected { get; set; } = true;
    public bool AlreadyInSteam { get; set; }
    public Dictionary<string, string> Meta { get; init; } = new();

    public string Badge => Source switch
    {
        "epic" => "EPIC",
        "xbox" => "XBOX",
        "steam" => "STEAM",
        _ => "LOCAL"
    };
}

public record SteamShortcut
{
    public uint AppId { get; set; }
    public string AppName { get; set; } = "";
    public string Exe { get; set; } = "";
    public string StartDir { get; set; } = "";
    public string Icon { get; set; } = "";
    public string LaunchOptions { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int IsHidden { get; set; }
    public int AllowDesktopConfig { get; set; } = 1;
    public int AllowOverlay { get; set; } = 1;
    public int OpenVR { get; set; }
    public int Devkit { get; set; }
    public string DevkitGameId { get; set; } = "";
    public int DevkitOverrideAppId { get; set; }
    public int LastPlayTime { get; set; }
    public string FlatpakAppId { get; set; } = "";
    public string ShortcutPath { get; set; } = "";

    // Original VDF keys (including casing) and nested map/string/int values.
    // Known properties above are authoritative when serializing an edited shortcut.
    public Dictionary<string, object> OriginalFields { get; set; } = new(StringComparer.Ordinal);
    internal string? OriginalIndex { get; set; }
}

public record SteamCollection(string Id, string Name, HashSet<uint> Added, HashSet<uint> Removed)
{
    public bool IsDynamic { get; set; }
}

public record ArtOption(string Url, string ThumbUrl, int Width, int Height, string Mime, string Style, int Score, int Upvotes);
