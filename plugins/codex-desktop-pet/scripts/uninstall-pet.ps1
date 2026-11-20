$ErrorActionPreference = "Stop"
$destination = Join-Path $env:LOCALAPPDATA "CodexDesktopPet\app"
$shortcutPath = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "Codex Desktop Pet.lnk"

$running = Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($destination, [StringComparison]::OrdinalIgnoreCase) }
if ($null -ne $running) {
    $running | Stop-Process
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Write-Output "Removed the Codex Desktop Pet application files and Start menu shortcut. Settings and logs were preserved."
