@echo off
REM SolidGround Revit add-in installer entry point (double-click path).
REM
REM Runs Install-SolidGround.ps1 with a process-scoped PowerShell execution-policy override for this
REM one invocation only. This never calls Set-ExecutionPolicy and never changes this machine's
REM persistent execution policy; it needs no administrator rights. If your organization enforces
REM PowerShell execution policy at MachinePolicy/UserPolicy scope, that policy can still override
REM this -- contact your administrator in that case (see the INSTALL.md file at the root of the
REM extracted zip, one level up from this install\ folder).
REM
REM New-ReleasePackage.ps1 copies this file (scripts/install.cmd in the repository) into the release
REM zip's own install\ folder, alongside Install-SolidGround.ps1; %~dp0 then resolves to that same
REM install\ folder at run time, so the relative reference below always finds its sibling.
REM
REM PowerShell's own exit code is captured below and this window pauses before it closes, so a
REM non-developer who double-clicked this file can actually read the result -- or an error -- rather
REM than watching the window vanish immediately. An automated/non-interactive run should redirect
REM stdin from NUL so `pause` returns immediately instead of waiting for a keypress:
REM   cmd /c install.cmd < NUL
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SolidGround.ps1" %*
set "SOLIDGROUND_INSTALL_EXITCODE=%ERRORLEVEL%"
echo.
pause
exit /b %SOLIDGROUND_INSTALL_EXITCODE%
