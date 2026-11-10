using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SteamBridge.Models;

namespace SteamBridge.Core;

public static class CollectionsManager
{
    private const string CloudFileName = "cloud-storage-namespace-1.json";
    private const string KeyPrefix = "user-collections.";

    public static string GetCloudFilePath(string steamPath, string userId)
    {
        return Path.Combine(steamPath, "userdata", userId, "config", "cloudstorage", CloudFileName);
    }

    public static List<SteamCollection> LoadCollections(string steamPath, string userId)
    {
        var root = ReadDocument(GetCloudFilePath(steamPath, userId));
        var result = new List<SteamCollection>();
        foreach (var item in root)
        {
            if (item is not JsonArray pair || PairKey(pair) is not string key || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                continue;
            var entry = (JsonObject)pair[1]!;
            if (IsDeleted(entry)) continue;
            JsonObject payload;
            try { payload = DecodePayload(entry); }
            catch (JsonException) { continue; } // Python also omits invalid individual values when listing.
            catch (InvalidDataException) { continue; }
            string id = Text(payload["id"]) is string payloadId && payloadId.Length > 0 ? payloadId : key[KeyPrefix.Length..];
            if (IsBuiltin(id) || IsBuiltin(key[KeyPrefix.Length..])) continue;
            result.Add(new SteamCollection(id, Text(payload["name"]) ?? id,
                ReadIds(payload["added"]), ReadIds(payload["removed"]))
            {
                IsDynamic = payload.ContainsKey("filterSpec")
            });
        }
        return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SaveCollection(string steamPath, string userId, SteamCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        string id = ValidateId(collection.Id);
        string name = collection.Name?.Trim() ?? "";
        if (name.Length == 0) throw new ArgumentException("Nome de coleção não pode ser vazio.", nameof(collection));
        string path = GetCloudFilePath(steamPath, userId);
        var root = ReadDocument(path);
        string key = KeyPrefix + id;
        var pair = FindPair(root, key);
        bool isNew = pair == null;
        var entry = isNew ? new JsonObject { ["key"] = key } : (JsonObject)pair![1]!;
        if (IsDeleted(entry)) throw new InvalidDataException($"Coleção removida: {id}.");
        var payload = isNew ? new JsonObject() : DecodePayload(entry);
        if (!payload.ContainsKey("id")) payload["id"] = id;
        payload["name"] = name;
        // Preserve the legacy Save contract: callers supply both complete membership sets.
        payload["added"] = new JsonArray(collection.Added.Select(value => (JsonNode)value).ToArray());
        payload["removed"] = new JsonArray(collection.Removed.Select(value => (JsonNode)value).ToArray());
        entry["value"] = payload.ToJsonString();
        UpdateTimestamp(entry);
        if (isNew)
        {
            entry["conflictResolutionMethod"] = "custom";
            entry["strMethodId"] = "union-collections";
            root.Add(new JsonArray { key, entry });
        }
        SafeFile.WriteAllText(path, root.ToJsonString());
    }

    public static void DeleteCollection(string steamPath, string userId, string collectionId)
    {
        string id = ValidateId(collectionId);
        string path = GetCloudFilePath(steamPath, userId);
        var root = ReadDocument(path);
        var pair = FindPair(root, KeyPrefix + id);
        if (pair == null) return;
        var entry = (JsonObject)pair[1]!;
        entry["is_deleted"] = true;
        entry.Remove("value");
        UpdateTimestamp(entry);
        SafeFile.WriteAllText(path, root.ToJsonString());
    }

    private static JsonArray ReadDocument(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new JsonArray(); }
        catch (DirectoryNotFoundException) { return new JsonArray(); }
        return JsonNode.Parse(text) as JsonArray
            ?? throw new InvalidDataException("Arquivo de coleções deve conter um array JSON.");
    }

    private static JsonArray? FindPair(JsonArray root, string key)
    {
        JsonArray? found = null;
        foreach (var item in root)
        {
            if (item is not JsonArray pair || pair.Count == 0 || Text(pair[0]) != key) continue;
            if (PairKey(pair) == null || found != null)
                throw new InvalidDataException($"Entrada de coleção inválida ou duplicada: {key}.");
            found = pair;
        }
        return found;
    }

    private static string? PairKey(JsonArray pair) =>
        pair.Count >= 2 && pair[1] is JsonObject ? Text(pair[0]) : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool IsDeleted(JsonObject entry) =>
        entry["is_deleted"] is JsonValue value && value.TryGetValue<bool>(out var deleted) && deleted;

    private static bool IsBuiltin(string id) => id is "favorite" or "hidden";

    private static string ValidateId(string id)
    {
        // The WinForms client also creates IDs without the Python uc- prefix.
        if (string.IsNullOrWhiteSpace(id) || IsBuiltin(id) || id != id.Trim())
            throw new ArgumentException("ID de coleção inválido ou reservado.", nameof(id));
        return id;
    }

    private static JsonObject DecodePayload(JsonObject entry)
    {
        if (entry["value"] is JsonObject payload) return (JsonObject)payload.DeepClone();
        string? raw = Text(entry["value"]);
        if (raw == null || JsonNode.Parse(raw) is not JsonObject decoded)
            throw new InvalidDataException("Coleção sem value JSON válido.");
        return decoded;
    }

    private static HashSet<uint> ReadIds(JsonNode? node)
    {
        var result = new HashSet<uint>();
        if (node is JsonArray array)
            foreach (var value in array)
                if (uint.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                    result.Add(id);
        return result;
    }

    private static void UpdateTimestamp(JsonObject entry)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entry["timestamp"] = timestamp;
        entry["version"] = timestamp.ToString(CultureInfo.InvariantCulture);
    }
}
