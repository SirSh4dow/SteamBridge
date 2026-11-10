using System.Text.RegularExpressions;
using SteamBridge.Models;

namespace SteamBridge.Scanners;

public static class FolderScanner
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "system volume information", "windows", "windowsapps", "wpsystem",
        "wudownloadcache", "programdata", "program files", "program files (x86)", "users",
        "appdata", "node_modules", ".git", ".cache", "steamapps", "steamlibrary", "redist",
        "_commonredist", "directx", "dotnet", "support", "docs", "tools", "engine", "sdk",
        "easyanticheat", "battleye", "crashreport", "crashreporter"
    };

    private static readonly Regex DenyExeRegex = new(
        @"^(unins|uninst|uninstall|setup|installer|install|update|updater|patcher|dxsetup|dxwebsetup|vcredist|vc_redist|dotnet|oalinst|physx|crash|report|werfault|register|activation|eula|readme|benchmark|dedicated|server|editor|tool)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static List<ScannedGame> ScanDirectory(string rootPath, int maxDepth = 3)
    {
        var games = new List<ScannedGame>();
        if (!Directory.Exists(rootPath)) return games;

        ScanRecursive(rootPath, rootPath, 0, maxDepth, games);
        return games;
    }

    private static void ScanRecursive(string currentDir, string rootPath, int currentDepth, int maxDepth, List<ScannedGame> games)
    {
        string dirName = Path.GetFileName(currentDir);
        if (SkipDirs.Contains(dirName)) return;

        // Procura executáveis nesta pasta
        try
        {
            var exeFiles = Directory.GetFiles(currentDir, "*.exe")
                .Where(f => !DenyExeRegex.IsMatch(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            if (exeFiles.Count > 0)
            {
                // Escolhe o executável mais provável (maior tamanho ou nome mais próximo da pasta)
                string bestExe = PickBestExe(exeFiles, currentDir);
                if (!string.IsNullOrEmpty(bestExe))
                {
                    string title = Path.GetFileName(currentDir);
                    if (title.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("win64", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("x64", StringComparison.OrdinalIgnoreCase))
                    {
                        title = Path.GetFileName(Path.GetDirectoryName(currentDir) ?? currentDir);
                    }

                    games.Add(new ScannedGame
                    {
                        Key = $"folder:{bestExe.ToLowerInvariant()}",
                        Title = title,
                        Source = "folder",
                        Exe = bestExe,
                        Cwd = currentDir,
                        InstallDir = currentDir,
                        IconPath = bestExe
                    });
                    return; // Encontrou o jogo nesta pasta, não precisa descer mais fundo nela
                }
            }
        }
        catch { }

        if (currentDepth < maxDepth)
        {
            try
            {
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    ScanRecursive(subDir, rootPath, currentDepth + 1, maxDepth, games);
                }
            }
            catch { }
        }
    }

    private static string PickBestExe(List<string> exeFiles, string dirPath)
    {
        if (exeFiles.Count == 1) return exeFiles[0];

        string dirName = Path.GetFileName(dirPath);

        // 1. Tenta achar um exe com o mesmo nome da pasta
        var exactMatch = exeFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null) return exactMatch;

        // 2. Ordena pelo maior arquivo (geralmente executáveis principais são muito maiores que launchers/helpers)
        return exeFiles
            .OrderByDescending(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0; }
            })
            .FirstOrDefault() ?? "";
    }
}
