@echo off
rem Apply the PCL CE region-gate binary patch to an official single-file build.
rem
rem usage: patch.cmd <official.exe> <output.exe> [--dry-run]
rem
rem Builds the patcher on first use (requires .NET SDK 10). The patcher resolves the
rem target bundle, locates the IL of the gate methods and rewrites them in place, so the
rem output keeps the official file size, layout and all baked-in secrets.
setlocal enabledelayedexpansion
set "ROOT=%~dp0"
if "%~1"=="" goto :usage
if "%~2"=="" goto :usage

set "DOTNET=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
  ) else (
    echo [error] .NET SDK not found. Install .NET 10 SDK or add dotnet to PATH.
    exit /b 1
  )
)

set "TOOL=%ROOT%tools\BundlePatch\bin\Release\net10.0\BundlePatch.exe"
if not exist "%TOOL%" (
  echo [build] compiling BundlePatch...
  "%DOTNET%" build "%ROOT%tools\BundlePatch\BundlePatch.csproj" -c Release --nologo -v q
  if errorlevel 1 (
    echo [error] build failed.
    exit /b 1
  )
)

"%TOOL%" %*
exit /b %ERRORLEVEL%

:usage
echo usage: patch.cmd ^<official.exe^> ^<output.exe^> [--dry-run]
echo example: patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"
echo.
echo   --dry-run   only locate and print the patched offsets, do not write output
exit /b 1
