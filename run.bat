@echo off
rem O app precisa ser iniciado fora de F:\Projetos: aquela arvore tem rotulo obrigatorio de
rem integridade baixa, que faria o SteamBridge (e o steam.exe por ele iniciado) rodar restrito.
if exist "%USERPROFILE%\SteamBridge\SteamBridge.exe" (
    start "" "%USERPROFILE%\SteamBridge\SteamBridge.exe"
    exit /b 0
)
echo Copia em nivel normal nao encontrada em %USERPROFILE%\SteamBridge.
echo Execute tools\install-local.bat apos o build-native.bat, ou abra o atalho SteamBridge do Menu Iniciar.
echo Nao abra o app de F:\Projetos: ele roda com integridade baixa e nao consegue controlar o Steam.
pause
