<#
.SYNOPSIS
  Builds, tests and packages Piping ISO Suite into a standalone installer .exe.
.EXAMPLE
  ./build/build.ps1                 # Release build + tests + installer in ./dist
  ./build/build.ps1 -SkipTests -Version 1.2.0
#>
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "out\addin"

dotnet build "$root\PipingIsoSuite.sln" -c $Configuration -p:Version=$Version
if ($LASTEXITCODE) { throw "Build failed" }

if (-not $SkipTests) {
    dotnet test "$root\tests\PipingIsoSuite.Core.Tests" -c $Configuration --no-build
    if ($LASTEXITCODE) { throw "Tests failed" }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
Copy-Item "$root\src\PipingIsoSuite.Revit\bin\$Configuration\net8.0-windows\*" $out -Recurse

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) and re-run." }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$out" "$root\installer\PipingIsoSuite.iss"
if ($LASTEXITCODE) { throw "Installer build failed" }
Get-ChildItem "$root\dist\*.exe" | ForEach-Object { Write-Host "Installer: $($_.FullName)" }
