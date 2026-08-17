@echo off
setlocal

set "ROOT_DIR=%~dp0..\..\..\"
set "PROJECT=%ROOT_DIR%MARS.Projects\MARS.AudioController\MARS.AudioController.csproj"
set "CONFIG=Release"

if "%~1"=="-h" goto show_help
if "%~1"=="--help" goto show_help
goto run_publish

:show_help
echo Usage: PublishAudioController.bat
echo.
echo Publishes MARS.AudioController in Release configuration.
echo Output: bin\Release\net10.0\publish\
exit /b 0

:run_publish
echo ============================================================
echo Publishing MARS.AudioController [%CONFIG%]
echo ============================================================
echo.

dotnet publish "%PROJECT%" --configuration %CONFIG% --self-contained false
if errorlevel 1 (
    echo.
    echo ERROR: Publish failed.
    exit /b 1
)

echo.
echo ============================================================
echo Publish complete: bin\Release\net10.0\publish\
echo ============================================================

exit /b 0
