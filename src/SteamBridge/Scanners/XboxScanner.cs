using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using SteamBridge.Models;

namespace SteamBridge.Scanners;

public static class XboxScanner
{
    public static List<ScannedGame> Scan()
    {
        var games = new List<ScannedGame>();

        string tempJson = Path.Combine(Path.GetTempPath(), $"steambridge_xbox_{Guid.NewGuid():N}.json");

        // Script PowerShell para extrair os aplicativos UWP/GDK
        string script = $$"""
        $ErrorActionPreference = 'Stop'
        trap { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        $packages = @(Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.InstallLocation })
        $apps = New-Object System.Collections.ArrayList

        foreach ($p in $packages) {
            # Um pacote inacessivel nao deve interromper a enumeracao dos demais.
            try {
                $manifest = Get-AppxPackageManifest -Package $p.PackageFullName
                if ($null -eq $manifest) { continue }

                $install = [string]$p.InstallLocation
                $cfgName = ''
                $cfgExe = ''
                $hasCfg = $false

                foreach ($cf in @((Join-Path $install 'MicrosoftGame.Config'), (Join-Path $install 'Content\\MicrosoftGame.Config'))) {
                    if (Test-Path -LiteralPath $cf) {
                        $hasCfg = $true
                        try {
                            [xml]$xml = Get-Content -LiteralPath $cf -Raw -Encoding UTF8
                            $sh = $xml.SelectSingleNode("//*[local-name()='ShellVisuals']")
                            if ($sh -and $sh.GetAttribute('DefaultDisplayName')) { $cfgName = $sh.GetAttribute('DefaultDisplayName') }
                            $nodes = $xml.SelectNodes("//*[local-name()='ExecutableList']/*[local-name()='Executable']")
                            foreach ($n in $nodes) {
                                $xName = $n.GetAttribute('Name')
                                if ($xName -and $xName -notmatch '(?i)gamelaunchhelper') { $cfgExe = $xName; break }
                            }
                        } catch {}
                        break
                    }
                }

                $entries = @($manifest.Package.Applications.Application)
                foreach ($app in $entries) {
                    if (-not $app -or -not $app.Id) { continue }
                    $vis = $app.VisualElements
                    $name = ''
                    if ($vis) { $name = [string]$vis.DisplayName }
                    if (-not $name -or $name -like '*ms-resource*') { $name = [string]$manifest.Package.Properties.DisplayName }
                    if ((-not $name -or $name -like '*ms-resource*') -and $cfgName) { $name = $cfgName }
                    if (-not $name -or $name -like '*ms-resource*') { $name = ($p.Name).Split('.')[-1] }

                    $exe = [string]$app.Executable
                    if ((-not $exe -or $exe -match '(?i)^gamelaunchhelper') -and $cfgExe) { $exe = $cfgExe }
                    $aumid = "$($p.PackageFamilyName)!$($app.Id)"

                    $isSys = $false
                    if ($install -like 'C:\\Windows\\*') { $isSys = $true }
                    elseif (-not $hasCfg -and ($install -like '*\\WindowsApps\\*' -or $p.Name -like 'Microsoft.*')) { $isSys = $true }

                    if (-not $exe -and -not $hasCfg) { continue }
                    if ($isSys) { continue }

                    $apps.Add([pscustomobject]@{
                        name = $name
                        aumid = $aumid
                        exe = $exe
                        install = $install
                    }) | Out-Null
                }
            } catch { continue }
        }
        ConvertTo-Json -InputObject @($apps.ToArray()) -Depth 4 -Compress | Out-File -LiteralPath '{{tempJson.Replace("'", "''")}}' -Encoding utf8
        """;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList =
                {
                    "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
                },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
            // Drenar stderr durante a execução evita bloquear o processo por buffer cheio.
            var errorTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(45000))
            {
                try
                {
                    // Encerrar somente o filho que este scanner criou, nunca outros processos.
                    proc.Kill(entireProcessTree: false);
                }
                catch (InvalidOperationException) when (proc.HasExited) { }

                if (!proc.WaitForExit(5000))
                    throw new TimeoutException("A busca Xbox excedeu 45 segundos e o encerramento do PowerShell não foi confirmado.");

                throw new TimeoutException("A busca Xbox excedeu 45 segundos. Tente novamente.");
            }

            string error = errorTask.GetAwaiter().GetResult().Trim();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"O PowerShell falhou ao enumerar os aplicativos Xbox (código {proc.ExitCode})."
                    : $"O PowerShell falhou ao enumerar os aplicativos Xbox: {error}");

            if (!File.Exists(tempJson))
                throw new InvalidDataException("O PowerShell não gerou o resultado da busca Xbox.");

            string json = File.ReadAllText(tempJson);
            var node = JsonNode.Parse(json);
            // Enumerar o objeto único diretamente, sem atribuir um novo parent ao JsonNode.
            IEnumerable<JsonNode?> items = node switch
            {
                JsonArray array => array,
                JsonObject single => new[] { single },
                _ => throw new InvalidDataException("O resultado da busca Xbox não contém um JSON válido de aplicativos.")
            };

            foreach (var item in items)
            {
                if (item == null) continue;
                string aumid = item["aumid"]?.ToString() ?? "";
                string name = item["name"]?.ToString() ?? "";
                string install = item["install"]?.ToString() ?? "";
                string exe = item["exe"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(aumid) || string.IsNullOrEmpty(install)) continue;

                string exePath = "";
                if (!string.IsNullOrEmpty(exe))
                {
                    string c1 = Path.Combine(install, exe);
                    string c2 = Path.Combine(install, "Content", exe);
                    if (File.Exists(c1)) exePath = c1;
                    else if (File.Exists(c2)) exePath = c2;
                }

                games.Add(new ScannedGame
                {
                    Key = $"xbox:{aumid}",
                    Title = name,
                    Source = "xbox",
                    Aumid = aumid,
                    Exe = exePath,
                    Cwd = install,
                    InstallDir = install
                });
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Não foi possível buscar os jogos Xbox. {ex.Message}", ex);
        }
        finally
        {
            // A limpeza não deve ocultar a causa original de uma falha na busca.
            try { File.Delete(tempJson); }
            catch (Exception ex) { Debug.WriteLine($"Falha ao remover o arquivo temporário Xbox: {ex.Message}"); }
        }

        return games;
    }
}
