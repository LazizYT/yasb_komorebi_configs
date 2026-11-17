param(
    [switch]$SkipStartup,
    [switch]$SkipStart
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ConfigRoot = Join-Path $env:USERPROFILE ".config"
$YasbTarget = Join-Path $ConfigRoot "yasb"
$KomorebiTarget = Join-Path $ConfigRoot "komorebi"

New-Item -ItemType Directory -Path $YasbTarget -Force | Out-Null
New-Item -ItemType Directory -Path $KomorebiTarget -Force | Out-Null

Write-Host "Restoring YASB, Komorebi, and whkdrc to $ConfigRoot ..."
Copy-Item -Path (Join-Path $RepoRoot "yasb\*") -Destination $YasbTarget -Recurse -Force
Copy-Item -Path (Join-Path $RepoRoot "komorebi\*") -Destination $KomorebiTarget -Recurse -Force
Copy-Item -Path (Join-Path $RepoRoot "whkdrc") -Destination (Join-Path $ConfigRoot "whkdrc") -Force

# Set user environment variables used by Komorebi and whkd
[Environment]::SetEnvironmentVariable("KOMOREBI_CONFIG_HOME", $KomorebiTarget, "User")
[Environment]::SetEnvironmentVariable("WHKD_CONFIG_HOME", $ConfigRoot, "User")

if (-not $SkipStartup) {
    $RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if (Test-Path "C:\Program Files\YASB\yasb.exe") {
        Set-ItemProperty -Path $RunKey -Name "YASB" -Value "C:\Program Files\YASB\yasb.exe"
    }
    Set-ItemProperty -Path $RunKey -Name "Komorebi" -Value "`"$YasbTarget\komorebi_launcher.exe`""
    Set-ItemProperty -Path $RunKey -Name "TaskbarHider" -Value "`"$YasbTarget\taskbar_hider.exe`""
}

if (-not $SkipStart) {
    if (Test-Path "C:\Program Files\YASB\yasb.exe") {
        Start-Process "C:\Program Files\YASB\yasb.exe"
    }
    Start-Process (Join-Path $YasbTarget "taskbar_hider.exe")
    Start-Process (Join-Path $YasbTarget "komorebi_launcher.exe")
}

Write-Host ""
Write-Host "Done! Notes:"
Write-Host "1. If weather widget uses an API key, set YASB_WEATHER_API_KEY in User environment variables."
Write-Host "2. If your Windows username is not 'Laziko', update hardcoded paths in yasb/config.yaml, whkdrc, and .cmd/.ps1 scripts."
