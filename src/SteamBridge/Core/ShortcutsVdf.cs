using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SteamBridge.Models;

namespace SteamBridge.Core;

public static class ShortcutsVdf
{
    private const byte TypeMap = 0x00;
    private const byte TypeString = 0x01;
    private const byte TypeInt32 = 0x02;
    private const byte TypeEnd = 0x08;

    private const int MaxDepth = 64;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static List<SteamShortcut> Read(string filePath)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(filePath); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }

        int index = 0;
        var root = ReadPairs(bytes, ref index, 0);
        if (index != bytes.Length || root.Count != 1 ||
            !root.TryGetValue("shortcuts", out var value) || value is not Dictionary<string, object> items)
            throw new InvalidDataException("Raiz de shortcuts.vdf inválida ou dados após o terminador.");

        var shortcuts = new List<SteamShortcut>();
        foreach (var (key, item) in items)
        {
            if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                item is not Dictionary<string, object> fields)
                throw new InvalidDataException("Entrada de shortcut inválida.");
            var shortcut = MapToShortcut(fields);
            shortcut.OriginalFields = fields;
            shortcut.OriginalIndex = key;
            shortcuts.Add(shortcut);
        }
        return shortcuts;
    }

    public static void Write(string filePath, List<SteamShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        // Refuse to replace a malformed existing document, even with a new list.
        _ = Read(filePath);
        var items = new Dictionary<string, object>(StringComparer.Ordinal);
        var reserved = shortcuts.Where(s => s.OriginalIndex != null).Select(s => s.OriginalIndex!).ToHashSet(StringComparer.Ordinal);
        int next = 0;
        foreach (var shortcut in shortcuts)
        {
            string? key = shortcut.OriginalIndex;
            if (key == null || items.ContainsKey(key))
            {
                do { key = (next++).ToString(CultureInfo.InvariantCulture); }
                while (items.ContainsKey(key) || reserved.Contains(key));
            }
            items.Add(key, ShortcutToMap(shortcut));
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
            WritePairs(writer, new Dictionary<string, object> { ["shortcuts"] = items }, 0);
        SafeFile.WriteAllBytes(filePath, stream.ToArray());
    }

    private static Dictionary<string, object> ShortcutToMap(SteamShortcut s)
    {
        var map = CloneMap(s.OriginalFields, 0);
        bool isNew = s.OriginalIndex == null;
        void Set(string name, object value, object defaultValue)
        {
            string? key = FindKey(map, name);
            if (isNew || key != null || !value.Equals(defaultValue))
                map[key ?? name] = value;
        }

        Set("appid", unchecked((int)s.AppId), 0);
        Set("AppName", s.AppName, "");
        Set("Exe", s.Exe, "");
        Set("StartDir", s.StartDir, "");
        Set("icon", s.Icon, "");
        Set("ShortcutPath", s.ShortcutPath, "");
        Set("LaunchOptions", s.LaunchOptions, "");
        Set("IsHidden", s.IsHidden, 0);
        Set("AllowDesktopConfig", s.AllowDesktopConfig, 1);
        Set("AllowOverlay", s.AllowOverlay, 1);
        Set("openvr", s.OpenVR, 0);
        Set("Devkit", s.Devkit, 0);
        Set("DevkitGameID", s.DevkitGameId, "");
        Set("DevkitOverrideAppID", s.DevkitOverrideAppId, 0);
        Set("LastPlayTime", s.LastPlayTime, 0);
        Set("FlatpakAppID", s.FlatpakAppId, "");

        string? tagsKey = FindKey(map, "tags");
        if (tagsKey != null && map[tagsKey] is not Dictionary<string, object>)
            throw new InvalidDataException("tags deve ser um mapa VDF.");
        var tags = tagsKey == null ? new Dictionary<string, object>(StringComparer.Ordinal)
            : (Dictionary<string, object>)map[tagsKey];
        // Retain non-string metadata and original tag keys, including gaps.
        var stringKeys = tags.Where(pair => pair.Value is string).Select(pair => pair.Key).ToList();
        for (int i = 0; i < stringKeys.Count; i++)
        {
            if (i < s.Tags.Count) tags[stringKeys[i]] = s.Tags[i];
            else tags.Remove(stringKeys[i]);
        }
        int next = 0;
        for (int i = stringKeys.Count; i < s.Tags.Count; i++)
        {
            string key;
            do { key = (next++).ToString(CultureInfo.InvariantCulture); } while (tags.ContainsKey(key));
            tags.Add(key, s.Tags[i]);
        }
        if (isNew || tagsKey != null || s.Tags.Count != 0) map[tagsKey ?? "tags"] = tags;
        return map;
    }

    private static string? FindKey(Dictionary<string, object> map, string name)
    {
        string? found = null;
        foreach (string key in map.Keys)
        {
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (found != null) throw new InvalidDataException($"Campo VDF ambíguo: {name}.");
            found = key;
        }
        return found;
    }

    private static string ReadString(Dictionary<string, object> map, string name)
    {
        string? key = FindKey(map, name);
        return key == null ? "" : map[key] as string
            ?? throw new InvalidDataException($"Campo VDF {name} deve ser string.");
    }

    private static int ReadInt(Dictionary<string, object> map, string name, int defaultValue)
    {
        string? key = FindKey(map, name);
        return key == null ? defaultValue : map[key] is int number ? number
            : throw new InvalidDataException($"Campo VDF {name} deve ser int32.");
    }

    private static Dictionary<string, object> CloneMap(Dictionary<string, object> map, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("VDF excede a profundidade permitida.");
        var clone = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in map)
            clone.Add(key, value is Dictionary<string, object> nested ? CloneMap(nested, depth + 1) : value);
        return clone;
    }

    private static void WritePairs(BinaryWriter writer, Dictionary<string, object> map, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("VDF excede a profundidade permitida.");
        foreach (var (key, value) in map)
        {
            byte type = value switch
            {
                Dictionary<string, object> => TypeMap,
                string => TypeString,
                int or uint => TypeInt32,
                _ => throw new InvalidDataException($"Tipo VDF não suportado no campo {key}.")
            };
            writer.Write(type);
            WriteNullString(writer, key);
            switch (value)
            {
                case Dictionary<string, object> nested: WritePairs(writer, nested, depth + 1); break;
                case string text: WriteNullString(writer, text); break;
                case int number: writer.Write(number); break;
                case uint number: writer.Write(number); break;
            }
        }
        writer.Write(TypeEnd);
    }

    private static SteamShortcut MapToShortcut(Dictionary<string, object> map)
    {
        var shortcut = new SteamShortcut
        {
            AppId = unchecked((uint)ReadInt(map, "appid", 0)),
            AppName = ReadString(map, "AppName"),
            Exe = ReadString(map, "Exe"),
            StartDir = ReadString(map, "StartDir"),
            Icon = ReadString(map, "icon"),
            ShortcutPath = ReadString(map, "ShortcutPath"),
            LaunchOptions = ReadString(map, "LaunchOptions"),
            IsHidden = ReadInt(map, "IsHidden", 0),
            AllowDesktopConfig = ReadInt(map, "AllowDesktopConfig", 1),
            AllowOverlay = ReadInt(map, "AllowOverlay", 1),
            OpenVR = ReadInt(map, "openvr", 0),
            Devkit = ReadInt(map, "Devkit", 0),
            DevkitGameId = ReadString(map, "DevkitGameID"),
            DevkitOverrideAppId = ReadInt(map, "DevkitOverrideAppID", 0),
            LastPlayTime = ReadInt(map, "LastPlayTime", 0),
            FlatpakAppId = ReadString(map, "FlatpakAppID")
        };
        string? tagsKey = FindKey(map, "tags");
        if (tagsKey != null)
        {
            if (map[tagsKey] is not Dictionary<string, object> tags)
                throw new InvalidDataException("tags deve ser um mapa VDF.");
            shortcut.Tags = tags.Values.OfType<string>().ToList();
        }
        return shortcut;
    }

    private static void WriteNullString(BinaryWriter writer, string value)
    {
        if (value == null || value.Contains('\0')) throw new InvalidDataException("String VDF inválida.");
        try { writer.Write(Utf8.GetBytes(value)); }
        catch (EncoderFallbackException ex) { throw new InvalidDataException("String VDF UTF-8 inválida.", ex); }
        writer.Write((byte)0);
    }

    private static Dictionary<string, object> ReadPairs(byte[] data, ref int index, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("VDF excede a profundidade permitida.");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            if (index >= data.Length) throw new InvalidDataException("VDF truncado: terminador ausente.");
            byte type = data[index++];
            if (type == TypeEnd) return result;
            if (type != TypeMap && type != TypeString && type != TypeInt32)
                throw new InvalidDataException($"Tipo VDF não suportado: 0x{type:X2}.");
            string key = ReadNullString(data, ref index);
            object value;
            if (type == TypeMap) value = ReadPairs(data, ref index, depth + 1);
            else if (type == TypeString) value = ReadNullString(data, ref index);
            else
            {
                if (data.Length - index < sizeof(int)) throw new InvalidDataException("VDF truncado: int32 incompleto.");
                value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(index, sizeof(int)));
                index += sizeof(int);
            }
            if (!result.TryAdd(key, value)) throw new InvalidDataException($"Chave VDF duplicada: {key}.");
        }
    }

    private static string ReadNullString(byte[] data, ref int index)
    {
        int start = index;
        while (index < data.Length && data[index] != 0) index++;
        if (index == data.Length) throw new InvalidDataException("VDF truncado: string sem terminador.");
        try { return Utf8.GetString(data, start, index++ - start); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("String VDF UTF-8 inválida.", ex); }
    }
}
