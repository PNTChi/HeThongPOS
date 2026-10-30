@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet run --project tests\Cashier.Tests\Cashier.Tests.csproj -c Release
if errorlevel 1 (
    echo Co loi build hoac test khong dat. Xem thong bao phia tren.
    pause
    exit /b 1
)
pause
