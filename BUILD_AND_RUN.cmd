@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Chua co .NET SDK 8. Cai .NET SDK 8 hoac Visual Studio 2022 voi .NET desktop development.
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
dotnet build src\Cashier.WinForms\Cashier.WinForms.csproj -c Release
if errorlevel 1 (
    echo Build that bai. Xem thong bao loi phia tren.
    pause
    exit /b 1
)
dotnet run --no-build -c Release --project src\Cashier.WinForms\Cashier.WinForms.csproj
pause
