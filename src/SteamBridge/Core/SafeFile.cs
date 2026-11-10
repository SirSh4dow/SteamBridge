using System.Text;

namespace SteamBridge.Core;

internal static class SafeFile
{
    internal static string? CreateBackup(string filePath)
    {
        string path = Path.GetFullPath(filePath);
        FileStream source;
        try
        {
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        using (source)
        {
            string directory = Path.Combine(Path.GetDirectoryName(path)!, "backups_steambridge");
            Directory.CreateDirectory(directory);
            string name = $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}{Path.GetExtension(path)}";
            string backup = Path.Combine(directory, name);
            using var destination = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);
            return backup;
        }
    }

    internal static void WriteAllText(string filePath, string text) =>
        WriteAllBytes(filePath, new UTF8Encoding(false, true).GetBytes(text));

    internal static void WriteAllBytes(string filePath, byte[] bytes)
    {
        CreateBackup(filePath);
        WriteAtomic(filePath, bytes);
    }

    internal static void WriteAtomic(string filePath, byte[] bytes)
    {
        string path = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
