# Compila o launcher auxiliar usando o csc.exe do .NET Framework ja presente no Windows.
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "csc.exe nao encontrado. Instale o .NET Framework 4.x."
}

$outDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'SteamBridgeAuxiliar.exe'

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    "/out:$out" `
    /reference:System.Windows.Forms.dll `
    (Join-Path $PSScriptRoot 'SteamBridgeLauncher.cs')

if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar (exit $LASTEXITCODE)" }
Write-Host "OK: $out ($((Get-Item $out).Length) bytes)"
