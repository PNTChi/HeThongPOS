@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet run --project tests\Manager.Tests\Manager.Tests.csproj -c Release
if errorlevel 1 (
    echo Co loi build hoac test Manager khong dat. Xem thong bao phia tren.
    pause
    exit /b 1
)
pause

