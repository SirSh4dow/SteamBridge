using System.Diagnostics;
using System.IO;
using System.Text;
using SteamBridge.Core;
using SteamBridge.Models;
using SteamBridge.Scanners;

namespace SteamBridge.Desktop;

public static class LibraryService
{
    public static string ShortcutPath(SteamInfo steam) => steam.SelectedUser == null
        ? throw new InvalidOperationException("Selecione uma conta Steam.")
        : Path.Combine(steam.InstallPath, "userdata", steam.SelectedUser.Id3, "config", "shortcuts.vdf");

    public static List<GameItem> Load(SteamInfo steam, IEnumerable<string> folders, bool discover, IProgress<string>? progress, List<string> warnings, IEnumerable<string>? manualExecutables = null, IReadOnlyDictionary<string, string>? manualTitles = null)
    {
        var games = SteamInstalledScanner.ScanInstalledGames(steam.Libraries);
        var shortcuts = steam.SelectedUser == null ? new List<SteamShortcut>() : ShortcutsVdf.Read(ShortcutPath(steam));
        var external = new List<ScannedGame>();
        if (discover)
        {
            void Scan(string name, Func<List<ScannedGame>> action)
            {
                progress?.Report("Procurando jogos · " + name);
                try { external.AddRange(action()); }
                catch (Exception ex) { warnings.Add(name + ": " + ex.Message); }
            }
            Scan("Epic Games", EpicScanner.Scan);
            Scan("Xbox / Game Pass", XboxScanner.Scan);
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) { warnings.Add("Pasta indisponível: " + folder); continue; }
                Scan(Path.GetFileName(folder), () => FolderScanner.ScanDirectory(folder));
            }
        }
        foreach (string path in manualExecutables ?? Array.Empty<string>())
        {
            try { external.Add(FromExecutable(path)); }
            catch (Exception ex) { warnings.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }
        var result = games.Select(g => new GameItem(g)).ToList();
        var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<uint>();
        foreach (var game in external)
        {
            var identity = !string.IsNullOrEmpty(game.Exe) ? game.Exe : game.Key;
            if (!seenExe.Add(identity)) continue;
            ApplyManualTitle(game, manualTitles);
            // A folder scan must not produce a second copy of a discovered Steam game.
            if (game.Source == "folder" && games.Any(g => IsInside(game.Exe, g.InstallDir))) continue;
            var existing = shortcuts.FirstOrDefault(s => !matched.Contains(s.AppId) && MatchesShortcut(game, s));
            game.AlreadyInSteam = existing != null;
            if (existing != null)
            {
                game.AppId = existing.AppId;
                if (!string.IsNullOrWhiteSpace(existing.AppName)) game.Title = existing.AppName;
                matched.Add(existing.AppId);
            }
            result.Add(new GameItem(game) { IsShortcut = existing != null });
        }
        foreach (var shortcut in shortcuts.Where(s => !matched.Contains(s.AppId)))
        {
            string tags = string.Join(" ", shortcut.Tags).ToLowerInvariant();
            string source = tags.Contains("epic") ? "epic" : tags.Contains("xbox") ? "xbox" : "folder";
            result.Add(new GameItem(new ScannedGame
            {
                Key = "shortcut:" + shortcut.AppId, AppId = shortcut.AppId, Title = shortcut.AppName,
                Source = source, Exe = SteamUtils.Unquote(shortcut.Exe), Cwd = SteamUtils.Unquote(shortcut.StartDir),
                InstallDir = SteamUtils.Unquote(shortcut.StartDir), IconPath = shortcut.Icon,
                LaunchArgs = shortcut.LaunchOptions, AlreadyInSteam = true
            }) { IsShortcut = true });
        }
        foreach (var game in result)
        {
            if (game.Game.AppId != 0) game.CoverFactory = () => CoverLoader.Load(CoverLoader.Find(steam, game.Game.AppId), 256);
        }
        return result.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ApplyManualTitle(ScannedGame game, IReadOnlyDictionary<string, string>? titles)
    {
        if (titles == null || string.IsNullOrWhiteSpace(game.Exe)) return;
        foreach (var pair in titles)
        {
            if (!pair.Key.Equals(game.Exe, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(pair.Value)) continue;
            game.Title = pair.Value.Trim();
            return;
        }
    }

    private static bool IsInside(string file, string directory) => !string.IsNullOrEmpty(directory)
        && file.StartsWith(directory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesShortcut(ScannedGame game, SteamShortcut shortcut)
    {
        string exe = SteamUtils.Unquote(shortcut.Exe);
        if (!string.IsNullOrWhiteSpace(game.Aumid))
            return Path.GetFileName(exe).Equals("SteamBridgeLauncher.exe", StringComparison.OrdinalIgnoreCase)
                && (shortcut.LaunchOptions == "--aumid " + QuoteArgument(game.Aumid)
                    || shortcut.LaunchOptions.StartsWith("--aumid " + QuoteArgument(game.Aumid) + " ", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(game.Uri) && (exe.Equals(game.Uri, StringComparison.OrdinalIgnoreCase)
            || (Path.GetFileName(exe).Equals("SteamBridgeLauncher.exe", StringComparison.OrdinalIgnoreCase)
                && shortcut.LaunchOptions.StartsWith("--uri " + QuoteArgument(game.Uri), StringComparison.Ordinal)))) return true;
        return !string.IsNullOrWhiteSpace(game.Exe) && exe.Equals(game.Exe, StringComparison.OrdinalIgnoreCase)
            && shortcut.LaunchOptions.Trim().Equals(game.LaunchArgs.Trim(), StringComparison.Ordinal);
    }

    public static readonly string[] ManualExtensions = [".exe", ".cmd", ".bat"];

    public static ScannedGame FromExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Selecione um arquivo.");
        string full = Path.GetFullPath(path);
        string ext = Path.GetExtension(full);
        if (!ManualExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha um arquivo .exe, .cmd ou .bat.");
        if (!File.Exists(full)) throw new FileNotFoundException("Arquivo não encontrado.", full);
        string title = Path.GetFileNameWithoutExtension(full);
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("O arquivo precisa ter um nome.");
        string cwd = Path.GetDirectoryName(full) ?? "";
        return new ScannedGame
        {
            Key = "manual:" + full,
            Title = title,
            Source = "folder",
            Exe = full,
            Cwd = cwd,
            InstallDir = cwd,
            IconPath = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? full : ""
        };
    }

    public static SteamShortcut CreateShortcut(ScannedGame game)
    {
        if (string.IsNullOrWhiteSpace(game.Title)) throw new InvalidOperationException("O jogo precisa ter um título.");
        string exe = game.Exe;
        string args = game.LaunchArgs;
        string cwd = game.Cwd;
        if (!string.IsNullOrWhiteSpace(game.Aumid))
        {
            exe = Path.Combine(AppContext.BaseDirectory, "SteamBridgeLauncher.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Launcher Xbox ausente. Mantenha SteamBridgeLauncher.exe junto do aplicativo.");
            args = "--aumid " + QuoteArgument(game.Aumid);
            if (!string.IsNullOrEmpty(game.Exe)) args += " --wait-exe " + QuoteArgument(Path.GetFileName(game.Exe));
            cwd = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }
        else if (string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(game.Uri))
        {
            exe = Path.Combine(AppContext.BaseDirectory, "SteamBridgeLauncher.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Launcher auxiliar ausente.");
            args = "--uri " + QuoteArgument(game.Uri);
            cwd = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }
        else if (!File.Exists(exe)) throw new FileNotFoundException("Executável não encontrado: " + exe);
        string quoted = SteamUtils.QuoteIfNeeded(exe);
        return new SteamShortcut
        {
            AppId = SteamUtils.CalculateShortcutAppId(quoted, game.Title), AppName = game.Title,
            Exe = quoted, StartDir = SteamUtils.QuoteIfNeeded(cwd), LaunchOptions = args,
            Icon = game.IconPath, Tags = new List<string> { game.Badge, "SteamBridge" }
        };
    }

    public static int Apply(SteamInfo steam, IEnumerable<GameItem> selection)
    {
        EnsureSteamClosed();
        string path = ShortcutPath(steam);
        var current = ShortcutsVdf.Read(path);
        int added = 0;
        foreach (var item in selection)
        {
            var shortcut = CreateShortcut(item.Game);
            if (current.Any(s => MatchesShortcut(item.Game, s))) continue;
            if (current.Any(s => s.AppId == shortcut.AppId)) throw new InvalidOperationException("Há um conflito de AppID. Renomeie o jogo antes de integrar: " + item.Title);
            current.Add(shortcut); added++;
        }
        if (added > 0) { EnsureSteamClosed(); ShortcutsVdf.Write(path, current); }
        return added;
    }

    public static void RenameIntegrated(SteamInfo steam, GameItem item)
    {
        if (item.Source == "steam") throw new InvalidOperationException("O nome de um jogo da Steam não pode ser alterado aqui.");
        if (!item.IsIntegrated) return;
        if (string.IsNullOrWhiteSpace(item.Game.Title)) throw new InvalidOperationException("Informe o título do jogo.");
        EnsureSteamClosed();
        string path = ShortcutPath(steam);
        var current = ShortcutsVdf.Read(path);
        var shortcut = current.FirstOrDefault(s => s.AppId == item.Game.AppId);
        if (shortcut == null) throw new InvalidOperationException("Atalho não encontrado para este jogo.");
        shortcut.AppName = item.Game.Title.Trim();
        ShortcutsVdf.Write(path, current);
    }

    public static void EnsureSteamClosed()
    {
        if (SteamLocator.IsSteamRunning()) throw new InvalidOperationException("Feche o Steam antes de gravar alterações. Nenhum processo será encerrado à força.");
    }

    public static void Launch(GameItem item)
    {
        if (item.IsIntegrated)
        {
            ulong id = item.Source == "steam" ? item.Game.AppId : ((ulong)item.Game.AppId << 32) | 0x02000000;
            Process.Start(new ProcessStartInfo("steam://rungameid/" + id) { UseShellExecute = true });
        }
        else
        {
            var shortcut = CreateShortcut(item.Game);
            var start = new ProcessStartInfo(SteamUtils.Unquote(shortcut.Exe))
            {
                Arguments = shortcut.LaunchOptions, UseShellExecute = true,
                WorkingDirectory = SteamUtils.Unquote(shortcut.StartDir)
            };
            Process.Start(start);
        }
    }

    public static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1).Append('"');
            else result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
