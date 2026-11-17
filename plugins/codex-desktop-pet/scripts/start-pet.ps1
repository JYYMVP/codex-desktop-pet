$ErrorActionPreference = "Stop"

if ($null -ne (Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue)) {
    exit 0
}

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:CODEX_DESKTOP_PET_EXE)) {
    $candidates += $env:CODEX_DESKTOP_PET_EXE
}

$candidates += Join-Path $env:LOCALAPPDATA "CodexDesktopPet\CodexDesktopPet.exe"
$candidates += Join-Path $PSScriptRoot "..\..\..\artifacts\win-x64\CodexDesktopPet.exe"

foreach ($candidate in $candidates) {
    $resolved = Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue
    if ($null -ne $resolved) {
        Start-Process -FilePath $resolved.Path
        exit 0
    }
}

Write-Output "Codex Desktop Pet executable is not installed. Run the plugin setup skill."
exit 0
