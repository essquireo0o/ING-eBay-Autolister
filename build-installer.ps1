# Build installer for ING AutoLister
# Run this script to generate ING-AutoLister-Setup.exe from the compiled AutoListerB1.exe
#
# Requirements: AutoListerB1.exe must exist in the dist\ folder first.
# Run:  dotnet publish ... (see README) then run this script.

param(
    [string]$Version = "B1"
)

$ErrorActionPreference = "Stop"
$exeSource = "$PSScriptRoot\ING eBay AutoLister\dist\AutoListerB1.exe"
$outDir    = "$PSScriptRoot\installer-out"
$installer = "$outDir\ING-AutoLister-Setup-$Version.exe"

if (-not (Test-Path $exeSource)) {
    Write-Error "AutoListerB1.exe not found at: $exeSource`nBuild the project first with: dotnet publish"
    exit 1
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --- Embed the exe as base64 inside a self-extracting PS1, then compile to exe ---
Write-Host "Reading exe ($([math]::Round((Get-Item $exeSource).Length/1MB,1)) MB)..."
$bytes  = [System.IO.File]::ReadAllBytes($exeSource)
$b64    = [Convert]::ToBase64String($bytes)

$script = @"
`$ErrorActionPreference = 'Stop'
`$installDir = "`$env:LOCALAPPDATA\ING AutoLister"
`$exePath    = "`$installDir\AutoListerB1.exe"

Write-Host ""
Write-Host "  ING Listing Engine(tm) Setup" -ForegroundColor Cyan
Write-Host "  by ING Mining LLC" -ForegroundColor Cyan
Write-Host ""

# Extract
New-Item -ItemType Directory -Force -Path `$installDir | Out-Null
Write-Host "  Installing to `$installDir ..."
`$b64 = '$b64'
[System.IO.File]::WriteAllBytes(`$exePath, [Convert]::FromBase64String(`$b64))

# Desktop shortcut
`$ws  = New-Object -ComObject WScript.Shell
`$lnk = `$ws.CreateShortcut("`$env:USERPROFILE\Desktop\ING AutoLister.lnk")
`$lnk.TargetPath       = `$exePath
`$lnk.WorkingDirectory = `$installDir
`$lnk.Description      = "ING Listing Engine by ING Mining LLC"
`$lnk.Save()

# Start Menu shortcut
`$smDir = "`$env:APPDATA\Microsoft\Windows\Start Menu\Programs\ING Mining"
New-Item -ItemType Directory -Force -Path `$smDir | Out-Null
`$lnk2 = `$ws.CreateShortcut("`$smDir\ING AutoLister.lnk")
`$lnk2.TargetPath       = `$exePath
`$lnk2.WorkingDirectory = `$installDir
`$lnk2.Description      = "ING Listing Engine by ING Mining LLC"
`$lnk2.Save()

Write-Host "  Shortcuts created on Desktop and Start Menu." -ForegroundColor Green
Write-Host ""
Write-Host "  Launching ING AutoLister..." -ForegroundColor Cyan
Start-Process `$exePath
Start-Sleep -Seconds 3
Start-Process "http://localhost:9330"
Write-Host ""
Write-Host "  Done! The app is now running at http://localhost:9330" -ForegroundColor Green
Write-Host ""
"@

# Write the launcher script
$ps1Path = "$outDir\setup-temp.ps1"
$script | Out-File -FilePath $ps1Path -Encoding UTF8

# Compile to exe using ps2exe if available, otherwise wrap in a .cmd launcher
$ps2exe = Get-Command ps2exe -ErrorAction SilentlyContinue
if ($ps2exe) {
    Write-Host "Compiling with ps2exe..."
    ps2exe -inputFile $ps1Path -outputFile $installer -title "ING AutoLister Setup" -version "1.0.0.$Version" -noConsole:$false
    Remove-Item $ps1Path -Force
    Write-Host "Installer created: $installer" -ForegroundColor Green
} else {
    # Fallback: rename the ps1 to a self-contained batch that runs it
    $batPath = "$outDir\ING-AutoLister-Setup-$Version.bat"
    @"
@echo off
echo.
echo  ING Listing Engine Setup
echo  Please wait...
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0setup-temp.ps1"
pause
"@ | Out-File -FilePath $batPath -Encoding ASCII
    Write-Host ""
    Write-Host "ps2exe not found — created batch installer instead: $batPath" -ForegroundColor Yellow
    Write-Host "To create a proper .exe, install ps2exe:  Install-Module ps2exe -Scope CurrentUser" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Output folder: $outDir" -ForegroundColor Green
}
