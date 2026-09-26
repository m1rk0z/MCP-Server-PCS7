@echo off
rem PCS7 MCP Agent - disinstallazione (anche da Programmi e funzionalita')
setlocal
set "EXE=%ProgramFiles(x86)%\Pcs7Agent\Pcs7Agent.exe"
if not exist "%EXE%" set "EXE=%ProgramFiles%\Pcs7Agent\Pcs7Agent.exe"
if not exist "%EXE%" set "EXE=%~dp0Pcs7Agent.exe"
"%EXE%" uninstall %*
exit /b %errorlevel%
