using System.Diagnostics;
using Microsoft.Win32;
using SteamBridge.Models;

namespace SteamBridge.Core;

public static class SteamLocator
{
    public static SteamInfo FindSteam(string? overridePath = null)
    {
        string installPath = "";

        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
        {
            installPath = overridePath;
        }
        else
        {
            // Registry lookup: HKCU\Software\Valve\Steam or HKLM\SOFTWARE\WOW6432Node\Valve\Steam
            try
            {
                using var cuKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (cuKey?.GetValue("SteamPath") is string p && Directory.Exists(p))
                {
                    installPath = p.Replace('/', '\\');
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(installPath))
            {
                try
                {
                    using var lmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (lmKey?.GetValue("InstallPath") is string p && Directory.Exists(p))
                    {
                        installPath = p.Replace('/', '\\');
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(installPath))
            {
                string def1 = @"C:\Program Files (x86)\Steam";
                string def2 = @"C:\Program Files\Steam";
                if (Directory.Exists(def1)) installPath = def1;
                else if (Directory.Exists(def2)) installPath = def2;
            }
        }

        string exePath = Path.Combine(installPath, "steam.exe");
        var users = FindUsers(installPath);
        var libraries = FindLibraries(installPath);

        var info = new SteamInfo(installPath, exePath, users, libraries);
        if (users.Count > 0)
        {
            info.SelectedUser = users.FirstOrDefault(u => u.MostRecent) ?? users[0];
        }
        return info;
    }

    public static List<SteamUser> FindUsers(string steamPath)
    {
        var result = new List<SteamUser>();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath)) return result;

        string loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
        var userProps = new Dictionary<string, (string persona, string account, bool recent)>();

        if (File.Exists(loginUsersPath))
        {
            try
            {
                var root = VdfTextParser.Parse(File.ReadAllText(loginUsersPath));
                if (root.TryGetValue("users", out var usersObj) && usersObj is Dictionary<string, object> usersDict)
                {
                    foreach (var (steamId64, val) in usersDict)
                    {
                        if (val is Dictionary<string, object> fields)
                        {
                            string persona = fields.TryGetValue("PersonaName", out var p) ? p?.ToString() ?? "" : "";
                            string account = fields.TryGetValue("AccountName", out var a) ? a?.ToString() ?? "" : "";
                            bool recent = fields.TryGetValue("mostrecent", out var r) && r?.ToString() == "1";

                            if (ulong.TryParse(steamId64, out ulong id64))
                            {
                                uint id3 = (uint)(id64 & 0xFFFFFFFF);
                                userProps[id3.ToString()] = (persona, account, recent);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        string userDataDir = Path.Combine(steamPath, "userdata");
        if (Directory.Exists(userDataDir))
        {
            foreach (var dir in Directory.GetDirectories(userDataDir))
            {
                string id3 = Path.GetFileName(dir);
                if (!uint.TryParse(id3, out _)) continue;

                if (userProps.TryGetValue(id3, out var info))
                {
                    result.Add(new SteamUser(id3, info.account, info.persona, info.recent));
                }
                else
                {
                    result.Add(new SteamUser(id3, id3, id3, false));
                }
            }
        }

        return result.OrderByDescending(u => u.MostRecent).ToList();
    }

    public static List<string> FindLibraries(string steamPath)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath)) return list;

        list.Add(steamPath);
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var doc = VdfTextParser.Parse(File.ReadAllText(vdfPath));
                if (doc.TryGetValue("libraryfolders", out var foldersObj) && foldersObj is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value is Dictionary<string, object> folderData && folderData.TryGetValue("path", out var p))
                        {
                            string folderPath = p.ToString()?.Replace(@"\\", @"\") ?? "";
                            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                            {
                                if (!list.Any(x => string.Equals(x, folderPath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    list.Add(folderPath);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
        return list;
    }

    public static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static bool CloseSteam(int timeoutSeconds = 10, string? steamExePath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutSeconds);
        var stopwatch = Stopwatch.StartNew();
        Process[] processes = Array.Empty<Process>();
        try
        {
            processes = Process.GetProcessesByName("steam");
            if (processes.Length == 0) return true;
            if (timeoutSeconds == 0) return false;
            string? executable = null;
            foreach (var process in processes)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (path != null && Path.GetFileName(path).Equals("steam.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    {
                        executable = path;
                        break;
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            executable ??= steamExePath ?? FindSteam().ExePath;
            EnsureSteamRegistryAccess();

            // Cooperative shutdown only. A timeout never escalates to Kill().
            using var request = Process.Start(CreateSteamStartInfo(executable, shutdown: true));
            if (request == null) return false;
            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (!IsSteamRunning()) return true;
                double remaining = timeoutSeconds * 1000d - stopwatch.Elapsed.TotalMilliseconds;
                if (remaining > 0) Thread.Sleep((int)Math.Min(200, Math.Ceiling(remaining)));
            }
            return !IsSteamRunning();
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static ProcessStartInfo CreateSteamStartInfo(string steamExePath, bool shutdown = false)
    {
        if (string.IsNullOrWhiteSpace(steamExePath) ||
            !Path.GetFileName(steamExePath).Equals("steam.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(steamExePath))
            throw new FileNotFoundException("steam.exe não encontrado. Confira o caminho do Steam nas configurações.", steamExePath);

        string executable = Path.GetFullPath(steamExePath);
        return new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            Arguments = shutdown ? "-shutdown" : "steam://open/main",
            UseShellExecute = true
        };
    }

    private static void EnsureSteamRegistryAccess()
    {
        // Request access only: never create keys, write values or change permissions.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: true);
            if (key == null)
                throw new InvalidOperationException("A chave do Steam não está disponível no registro deste usuário. Abra o Steam diretamente pelo Menu Iniciar e tente de novo.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                "O SteamBridge está rodando com integridade baixa porque a pasta de onde foi aberto tem um rótulo obrigatório de integridade baixa do Windows. " +
                "Um Steam iniciado assim não consegue gravar seu caminho no registro e fica com avisos de erro. Nenhum comando foi enviado. " +
                "Abra o SteamBridge pelo atalho do Menu Iniciar (a cópia em C:\\Users\\<seu usuário>\\SteamBridge roda em nível normal).", ex);
        }
    }

    public static void StartSteam(string steamExePath)
    {
        var startInfo = CreateSteamStartInfo(steamExePath);
        EnsureSteamRegistryAccess();
        using var process = Process.Start(startInfo);
    }

    public static bool HasSteamInterface()
    {
        var processes = Process.GetProcessesByName("steamwebhelper");
        try { return IsSteamRunning() && processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
