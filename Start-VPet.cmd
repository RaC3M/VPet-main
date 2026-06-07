@echo off
setlocal

set "ROOT=%~dp0"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
set "PROJECT=%ROOT%VPet-Simulator.Windows\VPet-Simulator.Windows.csproj"
set "APP_ROOT=%ROOT%VPet-Simulator.Windows\bin\x64\Debug\net8.0-windows"
set "APP_DIR=%APP_ROOT%\win-x64"
set "APP=%APP_DIR%\VPet-Simulator.Windows.exe"
set "APP_DLL=%APP_DIR%\VPet-Simulator.Windows.dll"
set "APP_FALLBACK=%APP_ROOT%\VPet-Simulator.Windows.exe"
set "APP_FALLBACK_DLL=%APP_ROOT%\VPet-Simulator.Windows.dll"

setx VPET_VISION_MODEL llava:7b >nul
set "VPET_SKIP_STEAM=true"

if not exist "%APP_DIR%\mod" if exist "%APP_ROOT%\mod\0000_core" (
    mklink /J "%APP_DIR%\mod" "%APP_ROOT%\mod" >nul
)

if exist "%APP%" if exist "%APP_DLL%" if exist "%APP_DIR%\mod\0000_core" (
    start "" /D "%APP_DIR%" "%APP%"
    exit /b 0
)

if exist "%APP_FALLBACK%" if exist "%APP_FALLBACK_DLL%" (
    powershell -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '%APP_FALLBACK%' -WorkingDirectory '%APP_ROOT%' -WindowStyle Hidden"
    exit /b 0
)

if not exist "%DOTNET%" (
    echo .NET SDK was not found at:
    echo %DOTNET%
    echo.
    echo Install .NET SDK or update Start-VPet.cmd with the correct dotnet.exe path.
    pause
    exit /b 1
)

echo Building VPet...
"%DOTNET%" build "%PROJECT%" -c Debug -p:Platform=x64
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Starting VPet...
if exist "%APP%" if exist "%APP_DLL%" if exist "%APP_DIR%\mod\0000_core" (
    start "" /D "%APP_DIR%" "%APP%"
) else if exist "%APP_FALLBACK%" if exist "%APP_FALLBACK_DLL%" (
    start "" /D "%APP_ROOT%" "%APP_FALLBACK%"
) else (
    echo Built successfully, but executable was not found.
    pause
    exit /b 1
)

endlocal
