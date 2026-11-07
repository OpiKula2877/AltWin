<#
.SYNOPSIS
  Tests and publishes Alt Account Switcher; optionally builds the installer and fills in the winget manifest.

.EXAMPLE
  ./build.ps1                      # test + publish artifacts\publish\AltAccounts.exe
  ./build.ps1 -Installer           # also artifacts\AltWin-Setup-<version>.exe (needs Inno Setup 6)
  ./build.ps1 -Installer -InstallerUrl https://github.com/OpiKula2877/AltWin/releases/download/v1.0.0/AltWin-Setup-1.0.0.exe
#>
param(
    [switch]$SkipTests,
    [switch]$Installer,
    [string]$InstallerUrl
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$proj = Get-Content 'src\AltAccounts.App\AltAccounts.App.csproj'
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

if (-not $SkipTests) {
    Write-Host "== Tests" -ForegroundColor Cyan
    dotnet test tests\AltAccounts.Core.Tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "== Publish $version" -ForegroundColor Cyan
dotnet publish src\AltAccounts.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts\publish --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
$exe = Get-Item artifacts\publish\AltAccounts.exe
Write-Host ("Published {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green

if ($Installer) {
    Write-Host "== Installer" -ForegroundColor Cyan
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
    & $iscc "/DAppVersion=$version" installer\AltAccounts.iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

    $setup = Get-Item "artifacts\AltWin-Setup-$version.exe"
    $hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash
    Write-Host "Installer: $($setup.FullName)  SHA256 $hash" -ForegroundColor Green

    $manifest = "winget\manifests\o\OpiKula2877\AltWin\$version\OpiKula2877.AltWin.installer.yaml"
    if (Test-Path $manifest) {
        $text = Get-Content $manifest -Raw
        $text = $text -replace 'InstallerSha256: .*', "InstallerSha256: $hash"
        if ($InstallerUrl) { $text = $text -replace 'InstallerUrl: .*', "InstallerUrl: $InstallerUrl" }
        [IO.File]::WriteAllText((Resolve-Path $manifest).Path, $text, (New-Object Text.UTF8Encoding $false)) # no BOM
        Write-Host "Updated $manifest" -ForegroundColor Green
    }
}
