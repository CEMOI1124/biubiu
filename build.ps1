# Build out\Biubiu.exe and run tests.
#   .\build.ps1
#   .\out\Biubiu.exe list
#   .\out\Biubiu.exe tasks
#   .\out\Biubiu.exe activate --project Biubiu

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "out"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$webExt = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll"
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe was not found"
}

& $csc /nologo /target:exe "/out:$out\Biubiu.exe" /r:System.dll /r:"$webExt" `
    "/resource:$root\hooks\biubiu-status.ps1,Biubiu.status.ps1" `
    "$root\src\CursorTitle.cs" `
    "$root\src\WorkspaceCatalog.cs" `
    "$root\src\CursorWindows.cs" `
    "$root\src\WindowActivator.cs" `
    "$root\src\HookInstaller.cs" `
    "$root\src\TaskBoard.cs" `
    "$root\src\Program.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $csc /nologo /target:exe "/out:$out\Biubiu.Tests.exe" /r:System.dll /r:"$webExt" `
    "$root\src\CursorTitle.cs" `
    "$root\src\WorkspaceCatalog.cs" `
    "$root\src\HookInstaller.cs" `
    "$root\src\TaskBoard.cs" `
    "$root\tests\CursorTitleTests.cs" `
    "$root\tests\HookTests.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$out\Biubiu.Tests.exe"
exit $LASTEXITCODE
