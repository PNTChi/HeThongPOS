@echo off
cd /d "%~dp0"
dotnet publish src\Manager.WinForms\Manager.WinForms.csproj -c Release -r win-x64 --self-contained false -o Windows_Demo_Manager
if errorlevel 1 exit /b 1
echo Ban chay Manager duoc tao trong Windows_Demo_Manager. Can .NET Desktop Runtime 8 x64.
pause

