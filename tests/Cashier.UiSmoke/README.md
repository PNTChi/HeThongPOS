# WinForms runtime smoke

Run on Windows with .NET SDK 8 or later:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/Cashier.UiSmoke/Cashier.UiSmoke.csproj -c Release -- verification/ui
```

The harness runs a real STA WinForms message loop and verifies that asynchronous continuations stay on that thread. Forms are fully transparent, placed offscreen, excluded from the taskbar and marked nonactivating. It invokes wired control events, renders the complete windows to PNGs and writes assertions to `verification/ui/UiSmokeResults.txt`. Receipts, shifts, accounts and checkout journals use isolated memory stores. It does not open printer dialogs, print, or use the user's application data.

The checks cover product selection and filters, preservation of existing cards, inline promotion errors, option controls, unavailable choices in an edited line, restored cashier sessions, closing during asynchronous loading, shared-journal exclusion within and across processes, cash shortage, bank-transfer confirmation, receipt/history viewers, and the unchanged reconciliation checks. The existing Cashier.Tests and Manager.Tests remain responsible for the broader business and persistence cases.
