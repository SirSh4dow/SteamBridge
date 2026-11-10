@echo off
setlocal
where dotnet >nul 2>&1
if errorlevel 1 (
    echo Instale o SDK .NET 8 para compilar. O aplicativo publicado nao precisa dele.
    exit /b 1
)
set "TEMP=%~dp0tests"
set "TMP=%~dp0tests"
dotnet run --project "%~dp0tests\SteamBridge.CoreChecks\SteamBridge.CoreChecks.csproj" -c Release
if errorlevel 1 exit /b 1
dotnet run --project "%~dp0tests\SteamBridge.DesktopChecks\SteamBridge.DesktopChecks.csproj" -c Release
if errorlevel 1 exit /b 1
dotnet publish "%~dp0src\SteamBridge.Desktop\SteamBridge.Desktop.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0publish-native" --nologo
if errorlevel 1 exit /b 1
echo.
echo Build pronto em publish-native. O app deve ser copiado para C: (fora de pastas com rotulo de integridade baixa).
echo Use tools\install-local.ps1 para criar a copia em C:\Users\<usuario>\SteamBridge e o atalho do Menu Iniciar.
endlocal
