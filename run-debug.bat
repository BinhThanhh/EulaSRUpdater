@echo off
chcp 65001 >nul

dotnet run --configuration Release

echo.
echo Nhấn phím bất kỳ để thoát...
pause >nul
