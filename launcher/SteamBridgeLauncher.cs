// SteamBridgeLauncher - tiny helper that launches UWP/Xbox games (via
// ApplicationActivationManager) and launcher URIs (e.g. Epic) from a Steam
// shortcut, keeping the process alive so Steam tracks playtime.
//
// Build with build.ps1 (uses the .NET Framework csc.exe shipped with Windows).
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SteamBridge
{
    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            int options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    internal static class Program
    {
        private const int TimeoutMs = 45000;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    return Fail("Uso:\n  SteamBridgeLauncher.exe --aumid \"PFN!AppId\" [--wait-exe nome.exe]\n  SteamBridgeLauncher.exe --uri \"com.epicgames.launcher://...\"\n  SteamBridgeLauncher.exe --exe \"C:\\jogo\\jogo.exe\" [--args \"...\"] [--cwd \"...\"]");
                }

                string mode = args[0].ToLowerInvariant();
                string target = args[1];
                string waitExe = GetOption(args, "--wait-exe");
                string extraArgs = GetOption(args, "--args");
                string cwd = GetOption(args, "--cwd");

                switch (mode)
                {
                    case "--aumid":
                        return LaunchAumid(target, waitExe);
                    case "--uri":
                        return LaunchUri(target, waitExe);
                    case "--exe":
                        return LaunchExe(target, extraArgs, cwd);
                    default:
                        return Fail("Modo desconhecido: " + mode);
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static int LaunchAumid(string aumid, string waitExe)
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            uint pid;
            int hr = manager.ActivateApplication(aumid, null, 0, out pid);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            Process process = null;
            if (pid != 0)
            {
                process = TryGetProcess((int)pid);
            }

            if (process == null && !string.IsNullOrEmpty(waitExe))
            {
                process = WaitForProcessByName(waitExe, TimeoutMs);
            }

            if (process != null)
            {
                // A UWP launch may hand off to a different process; if the one we
                // got exits quickly, try to adopt the real game process once.
                if (!process.WaitForExit(8000) && !string.IsNullOrEmpty(waitExe))
                {
                    process.WaitForExit();
                    return 0;
                }
                if (process.HasExited && !string.IsNullOrEmpty(waitExe))
                {
                    Process adopted = WaitForProcessByName(waitExe, 20000);
                    if (adopted != null)
                    {
                        adopted.WaitForExit();
                        return 0;
                    }
                }
                return 0;
            }

            Thread.Sleep(1500);
            return 0;
        }

        private static int LaunchUri(string uri, string waitExe)
        {
            var info = new ProcessStartInfo(uri);
            info.UseShellExecute = true;
            Process.Start(info);

            if (!string.IsNullOrEmpty(waitExe))
            {
                Process process = WaitForProcessByName(waitExe, TimeoutMs);
                if (process != null)
                {
                    process.WaitForExit();
                    return 0;
                }
            }

            Thread.Sleep(2000);
            return 0;
        }

        private static int LaunchExe(string exe, string extraArgs, string cwd)
        {
            var info = new ProcessStartInfo(exe);
            info.UseShellExecute = false;
            if (!string.IsNullOrEmpty(extraArgs))
            {
                info.Arguments = extraArgs;
            }
            if (!string.IsNullOrEmpty(cwd))
            {
                info.WorkingDirectory = cwd;
            }

            using (Process process = Process.Start(info))
            {
                if (process != null)
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            return 0;
        }

        private static Process TryGetProcess(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch
            {
                return null;
            }
        }

        private static Process WaitForProcessByName(string name, int timeoutMs)
        {
            string wanted = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4)
                : name;
            int waited = 0;
            while (waited < timeoutMs)
            {
                Process[] all = Process.GetProcesses();
                foreach (Process p in all)
                {
                    try
                    {
                        if (string.Equals(p.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return p;
                        }
                    }
                    catch
                    {
                        // Process died while we were inspecting it.
                    }
                }
                Thread.Sleep(750);
                waited += 750;
            }
            return null;
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 2; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static int Fail(string message)
        {
            MessageBox.Show(message, "SteamBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
