@echo off
rem ==========================================================================
rem  PCS7 MCP Agent - installazione sulla macchina PCS 7 (VM)
rem  Windows 7 SP1 / 8.1 / 10 / 11, Server 2008 R2 SP1 ... 2022 (32 o 64 bit)
rem
rem  Uso:   setup.cmd                       installazione guidata
rem         setup.cmd --silent [--port 8765] [--access-mode read-only^|read-write]
rem                            [--allow 192.168.56.1] [--new-token]
rem ==========================================================================
setlocal
cd /d "%~dp0"

rem no "pause" in unattended installs
set "SILENT="
echo %* | "%SystemRoot%\System32\find.exe" /i "--silent" >nul && set "SILENT=1"

if not exist "%~dp0Pcs7Agent.exe" (
  echo Pcs7Agent.exe non trovato. Estrarre TUTTO lo zip in una cartella prima di eseguire setup.cmd.
  if not defined SILENT pause
  exit /b 1
)

rem --- .NET Framework 4.8 richiesto (Release >= 528040)
set "NETREL="
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| "%SystemRoot%\System32\find.exe" "Release"') do set "NETREL=%%a"
if not defined NETREL goto :nonet
set /a NETREL_DEC=%NETREL% 2>nul
if not defined NETREL_DEC goto :nonet
if %NETREL_DEC% LSS 528040 goto :nonet

rem Pcs7Agent.exe asks for administrator rights, waits for the installer and returns its exit code
"%~dp0Pcs7Agent.exe" install %*
exit /b %errorlevel%

:nonet
echo.
echo  Su questa macchina manca .NET Framework 4.8, necessario per l'agente.
echo.
echo  Scaricarlo da Microsoft (installer offline, circa 110 MB):
echo    https://dotnet.microsoft.com/download/dotnet-framework/net48
echo  Su Windows 7 serve il Service Pack 1. Dopo l'installazione riavviare
echo  e rieseguire setup.cmd.
echo.
if not defined SILENT pause
exit /b 1
