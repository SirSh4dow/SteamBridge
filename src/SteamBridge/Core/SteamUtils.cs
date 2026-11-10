using System.Text;

namespace SteamBridge.Core;

public static class SteamUtils
{
    private static readonly uint[] CrcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        uint[] table = new uint[256];
        const uint polynomial = 0xEDB88320;
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    public static uint ComputeCrc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte index = (byte)((crc & 0xFF) ^ bytes[i]);
            crc = (crc >> 8) ^ CrcTable[index];
        }
        return ~crc;
    }

    public static uint CalculateShortcutAppId(string exe, string appName)
    {
        // Steam CRC32 formula: (CRC32(Exe + AppName) | 0x80000000) & 0xFFFFFFFF
        byte[] input = Encoding.UTF8.GetBytes(exe + appName);
        uint crc = ComputeCrc32(input);
        return crc | 0x80000000u;
    }

    public static string QuoteIfNeeded(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string trimmed = path.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"')) return trimmed;
        return $"\"{trimmed}\"";
    }

    public static string Unquote(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        string trimmed = s.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed[1..^1];
        }
        return trimmed;
    }
}
