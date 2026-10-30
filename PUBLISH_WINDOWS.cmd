@echo off
cd /d "%~dp0"
dotnet publish src\Cashier.WinForms\Cashier.WinForms.csproj -c Release -r win-x64 --self-contained false -o Windows_Demo
if errorlevel 1 exit /b 1
echo Ban chay duoc tao trong Windows_Demo. Can .NET Desktop Runtime 8 x64.
pause
