using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SteamBridge.Models;

namespace SteamBridge.Core;

public class SteamGridDbClient
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2";
    private readonly HttpClient _http;
    private string _apiKey = "";

    public SteamGridDbClient(string apiKey = "")
    {
        _apiKey = apiKey;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamBridge/2.0 (Windows)");
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<int?> SearchGameIdAsync(string gameTitle)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(gameTitle)) return null;
        var matches = await SearchGamesAsync(gameTitle);
        return matches.Count == 0 ? null : matches[0].Id;
    }

    public async Task<List<GridGameMatch>> SearchGamesAsync(string gameTitle)
    {
        var matches = new List<GridGameMatch>();
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(gameTitle)) return matches;

        string url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(gameTitle.Trim())}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return matches;

        string json = await res.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        if (node?["success"]?.GetValue<bool>() != true || node["data"] is not JsonArray arr) return matches;
        foreach (var item in arr)
        {
            if (item?["id"]?.GetValue<int>() is not int id || id <= 0) continue;
            string name = item["name"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(name) || matches.Any(m => m.Id == id)) continue;
            matches.Add(new GridGameMatch(id, name));
            if (matches.Count == 8) break;
        }
        return matches;
    }

    public async Task<List<ArtOption>> GetArtOptionsAsync(int gameId, string kind)
    {
        // kind: "grids" (portrait/wide), "heroes", "logos", "icons"
        var options = new List<ArtOption>();
        if (string.IsNullOrWhiteSpace(_apiKey)) return options;

        string endpoint = kind switch
        {
            "hero" => "heroes/game",
            "logo" => "logos/game",
            "icon" => "icons/game",
            _ => "grids/game"
        };

        string url = $"{BaseUrl}/{endpoint}/{gameId}";
        if (kind == "portrait") url += "?dimensions=600x900,342x482,660x930";
        else if (kind == "wide") url += "?dimensions=920x430,460x215";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return options;

        string json = await res.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        if (node?["success"]?.GetValue<bool>() == true && node["data"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item == null) continue;
                string itemUrl = item["url"]?.ToString() ?? "";
                string thumb = item["thumb"]?.ToString() ?? itemUrl;
                int w = item["width"]?.GetValue<int>() ?? 0;
                int h = item["height"]?.GetValue<int>() ?? 0;
                string mime = item["mime"]?.ToString() ?? "";
                string style = item["style"]?.ToString() ?? "";
                int score = item["score"]?.GetValue<int>() ?? 0;
                int upvotes = item["upvotes"]?.GetValue<int>() ?? 0;

                options.Add(new ArtOption(itemUrl, thumb, w, h, mime, style, score, upvotes));
            }
        }

        return options;
    }

    public async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            return await _http.GetByteArrayAsync(url);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record GridGameMatch(int Id, string Name)
{
    public override string ToString() => Name;
}
