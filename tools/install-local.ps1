# Copia o SteamBridge publicado para fora da arvore F:\Projetos e cria o atalho do Menu Iniciar.
# Necessario porque rotulos obrigatorios de integridade baixa em F:\Projetos fariam o app (e o
# steam.exe por ele iniciado) rodar com integridade baixa, impedindo o Steam de gravar
# HKCU\Software\Valve\Steam. Nao altera ACLs, nao toca em arquivos do Steam e nao apaga nada.
param(
    [string]$Source = "",
    [string]$Target = (Join-Path $env:USERPROFILE "SteamBridge")
)

$ErrorActionPreference = "Stop"
if (-not $Source) {
    $Source = Join-Path (Split-Path $PSScriptRoot -Parent) "publish-native"
}
if (-not (Test-Path -LiteralPath (Join-Path $Source "SteamBridge.Desktop.exe"))) {
    throw "Build publicado nao encontrado em '$Source'. Rode build-native.bat primeiro."
}

# Preserva a copia anterior em vez de sobrescrever silenciosamente.
if (Test-Path -LiteralPath $Target) {
    $staging = "$Target-preparando-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss'))"
    Rename-Item -LiteralPath $Target -NewName (Split-Path $staging -Leaf)
    Write-Output "Versao anterior preservada em: $staging"
}

New-Item -ItemType Directory -Path $Target -Force | Out-Null
# robocopy copia a arvore completa (subpastas de recursos inclusas). Codigo 0-7 = sucesso.
& robocopy $Source $Target /E /NFL /NDL /NJH /NJS /NP | Out-Null
$robocopyResult = $LASTEXITCODE
if ($robocopyResult -ge 8) { throw "robocopy falhou com codigo $robocopyResult." }

# Conferencia por SHA-256 de todos os arquivos.
$sourceFiles = Get-ChildItem -LiteralPath $Source -File -Recurse
$mismatch = @()
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring((Resolve-Path -LiteralPath $Source).Path.Length)
    $copy = Join-Path $Target $relative.TrimStart("\")
    if (-not (Test-Path -LiteralPath $copy)) { $mismatch += "ausente: $relative"; continue }
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
        $mismatch += "divergente: $relative"
    }
}
if ($mismatch.Count -gt 0) { throw "Copia com divergencias:`n$($mismatch -join "`n")" }
Write-Output "Copia verificada: $($sourceFiles.Count) arquivos identicos em $Target"

# O destino nao pode herdar rotulo de integridade; apenas relata, sem alterar permissoes.
$acl = (Get-Acl -LiteralPath $Target).Sddl
if ($acl -match "S-1-16-(4096|12288|16384)") {
    Write-Warning "O destino carrega um rotulo de integridade abaixo do normal. Escolha outra pasta."
} else {
    Write-Output "Destino sem rotulo de integridade baixa."
}

# Atalho no Menu Iniciar (somente no perfil do usuario; sem elevacao).
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcut = $shell.CreateShortcut((Join-Path $startMenu "SteamBridge.lnk"))
$shortcut.TargetPath = Join-Path $Target "SteamBridge.Desktop.exe"
$shortcut.WorkingDirectory = $Target
$shortcut.Description = "SteamBridge - biblioteca unificada (copia em nivel normal)"
$shortcut.Save()
Write-Output "Atalho criado em: $startMenu\SteamBridge.lnk"
Write-Output "Abra o SteamBridge por esse atalho, nunca por F:\Projetos."
