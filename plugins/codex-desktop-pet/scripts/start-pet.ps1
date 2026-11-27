$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "shared-paths.ps1")

function Write-HookLog([string]$Message) {
    if ([string]::IsNullOrWhiteSpace($env:PLUGIN_DATA)) {
        return
    }
    New-Item -ItemType Directory -Path $env:PLUGIN_DATA -Force | Out-Null
    Add-Content -LiteralPath (Join-Path $env:PLUGIN_DATA "desktop-pet-hook.log") `
        -Value "$(Get-Date -Format o) $Message"
}

$dataRoot = Get-CodexDesktopPetDataRoot
$settingsPath = Join-Path $dataRoot "settings.json"
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

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:CODEX_DESKTOP_PET_EXE)) {
    $candidates += $env:CODEX_DESKTOP_PET_EXE
}

$candidates += Join-Path $dataRoot "app\CodexDesktopPet.exe"
$candidates += Join-Path $dataRoot "CodexDesktopPet.exe"
$candidates += Join-Path $PSScriptRoot "..\..\..\artifacts\win-x64\CodexDesktopPet.exe"

foreach ($candidate in $candidates) {
    $resolved = Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue
    if ($null -ne $resolved) {
        $candidateFullPath = [IO.Path]::GetFullPath($resolved.Path)
        $processes = @(Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue)
        $matchingProcessCount = 0
        try {
            foreach ($process in $processes) {
                try {
                    if ([IO.Path]::GetFullPath($process.Path) -eq $candidateFullPath) {
                        $matchingProcessCount++
                    }
                } catch { }
            }
        } finally {
            foreach ($process in $processes) { $process.Dispose() }
        }
        if ($matchingProcessCount -gt 0) {
            Write-HookLog "Desktop pet is already running from $candidateFullPath."
            exit 0
        }
        Start-Process -FilePath $resolved.Path -WindowStyle Hidden
        Write-HookLog "Started desktop pet from $($resolved.Path)."
        exit 0
    }
}

Write-HookLog "Desktop pet executable was not found."
exit 0
