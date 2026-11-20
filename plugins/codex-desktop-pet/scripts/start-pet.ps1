$ErrorActionPreference = "Stop"

function Write-HookLog([string]$Message) {
    if ([string]::IsNullOrWhiteSpace($env:PLUGIN_DATA)) {
        return
    }
    New-Item -ItemType Directory -Path $env:PLUGIN_DATA -Force | Out-Null
    Add-Content -LiteralPath (Join-Path $env:PLUGIN_DATA "desktop-pet-hook.log") `
        -Value "$(Get-Date -Format o) $Message"
}

$settingsPath = Join-Path $env:LOCALAPPDATA "CodexDesktopPet\settings.json"
if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    try {
        $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
        $enabledProperty = $settings.PSObject.Properties["PetEnabled"]
        if ($null -ne $enabledProperty -and $enabledProperty.Value -eq $false) {
            Write-HookLog "Desktop pet startup skipped because PetEnabled is false."
            exit 0
        }
    } catch {
        Write-HookLog "Desktop pet settings could not be read; using the enabled default."
    }
}

if ($null -ne (Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue)) {
    Write-HookLog "Desktop pet is already running."
    exit 0
}

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:CODEX_DESKTOP_PET_EXE)) {
    $candidates += $env:CODEX_DESKTOP_PET_EXE
}

$candidates += Join-Path $env:LOCALAPPDATA "CodexDesktopPet\app\CodexDesktopPet.exe"
$candidates += Join-Path $env:LOCALAPPDATA "CodexDesktopPet\CodexDesktopPet.exe"
$candidates += Join-Path $PSScriptRoot "..\..\..\artifacts\win-x64\CodexDesktopPet.exe"

foreach ($candidate in $candidates) {
    $resolved = Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue
    if ($null -ne $resolved) {
        Start-Process -FilePath $resolved.Path -WindowStyle Hidden
        Write-HookLog "Started desktop pet from $($resolved.Path)."
        exit 0
    }
}

Write-HookLog "Desktop pet executable was not found."
exit 0
