@echo off
chcp 65001 >nul
echo ====================================
echo    EULA SR Updater - Build Release
echo ====================================
echo.

echo Cleaning previous build...
dotnet clean --configuration Release

echo.
echo Building Release configuration...
dotnet build --configuration Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ✅ Build thành công!
    echo.
    echo Release files location:
    echo %CD%\bin\Release\net9.0\
    echo.
    echo Tools folder đã được copy tự động.
    echo.
    pause
) else (
    echo.
    echo ❌ Build thất bại!
    echo.
    pause
    exit /b 1
)
