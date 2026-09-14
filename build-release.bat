@echo off
rem Compila os pacotes de release localmente (win-x64, win-arm64, linux-x64, linux-arm64).
rem Uso: build-release.bat [versao] [-SkipTests] [-Rids win-x64,linux-x64]
setlocal

where pwsh >nul 2>nul
if errorlevel 1 (
    echo [ERRO] PowerShell 7+ ^(pwsh^) nao encontrado. Instale com: winget install Microsoft.PowerShell
    set EXITCODE=1
    goto :end
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
set EXITCODE=%ERRORLEVEL%

:end
rem Mantem a janela aberta quando executado com duplo clique.
echo %cmdcmdline% | find /i "%~0" >nul && pause
exit /b %EXITCODE%
