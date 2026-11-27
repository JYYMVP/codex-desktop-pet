$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "shared-paths.ps1")
$destination = Join-Path (Get-CodexDesktopPetDataRoot) "app"
$shortcutPath = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "Codex Desktop Pet.lnk"
$installedExe = Join-Path $destination "CodexDesktopPet.exe"
$installedFullPath = [IO.Path]::GetFullPath($installedExe)

$running = Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue |
    Where-Object {
        try { [IO.Path]::GetFullPath($_.Path) -eq $installedFullPath }
        catch { $false }
    }
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
