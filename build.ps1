# Build out\Biubiu.exe and run tests.
#   .\build.ps1
#   .\out\Biubiu.exe
#   .\out\Biubiu.exe list
#   .\out\Biubiu.exe tasks

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "out"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$webExt = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll"
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe was not found"
}

& $csc /nologo /target:exe /codepage:65001 "/out:$out\Biubiu.exe" /r:System.dll /r:"$webExt" /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/win32icon:$root\assets\biubiu.ico" `
    "/resource:$root\hooks\biubiu-status.ps1,Biubiu.status.ps1" `
    "/resource:$root\assets\biubiu-img.png,Biubiu.icon.png" `
    "$root\src\CursorTitle.cs" `
    "$root\src\WorkspaceCatalog.cs" `
    "$root\src\CursorWindows.cs" `
    "$root\src\WindowActivator.cs" `
    "$root\src\HookInstaller.cs" `
    "$root\src\TaskBoard.cs" `
    "$root\src\ComposerHistory.cs" `
    "$root\src\GitBranch.cs" `
    "$root\src\ProjectCards.cs" `
    "$root\src\CardOrder.cs" `
    "$root\src\CardNotes.cs" `
    "$root\src\UiSettings.cs" `
    "$root\src\CardPainter.cs" `
    "$root\src\AppIcon.cs" `
    "$root\src\AppMenu.cs" `
    "$root\src\SettingsForm.cs" `
    "$root\src\AboutForm.cs" `
    "$root\src\MainForm.cs" `
    "$root\src\Program.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $csc /nologo /target:exe /codepage:65001 "/out:$out\Biubiu.Tests.exe" /r:System.dll /r:"$webExt" /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$root\src\CursorTitle.cs" `
    "$root\src\WorkspaceCatalog.cs" `
    "$root\src\CursorWindows.cs" `
    "$root\src\HookInstaller.cs" `
    "$root\src\TaskBoard.cs" `
    "$root\src\ComposerHistory.cs" `
    "$root\src\GitBranch.cs" `
    "$root\src\ProjectCards.cs" `
    "$root\src\CardOrder.cs" `
    "$root\src\CardNotes.cs" `
    "$root\src\UiSettings.cs" `
    "$root\src\CardPainter.cs" `
    "$root\tests\CursorTitleTests.cs" `
    "$root\tests\HookTests.cs" `
    "$root\tests\CardTests.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$out\Biubiu.Tests.exe"
exit $LASTEXITCODE
