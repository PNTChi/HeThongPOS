@echo off
chcp 65001 >nul
cd /d "%~dp0"
set MSBuildEnableWorkloadResolver=false
dotnet build tests\Cashier.ProductImageChecks\Cashier.ProductImageChecks.csproj -c Release -m:1 --nologo
if errorlevel 1 goto failed
dotnet run --no-build -c Release --project tests\Cashier.ProductImageChecks\Cashier.ProductImageChecks.csproj
if errorlevel 1 goto failed
dotnet build tests\Cashier.UiSmoke\Cashier.UiSmoke.csproj -c Release -m:1 --nologo
if errorlevel 1 goto failed
dotnet run --no-build -c Release --project tests\Cashier.UiSmoke\Cashier.UiSmoke.csproj -- verification\ui
if errorlevel 1 goto failed
echo Da chay kiem tra anh va cua so. Xem verification\ui.
pause
exit /b 0
:failed
echo Kiem tra that bai. Xem loi phia tren.
pause
exit /b 1
