param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$sourceExe = Join-Path $source "CodexDesktopPet.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "CodexDesktopPet.exe was not found in $source"
}

$destination = Join-Path $env:LOCALAPPDATA "CodexDesktopPet\app"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force

$installedExe = Join-Path $destination "CodexDesktopPet.exe"
if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
    throw "The desktop pet installation did not produce $installedExe"
}

$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsDirectory "Codex Desktop Pet.lnk"
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExe
    $shortcut.Arguments = "--show"
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = "$installedExe,0"
    $shortcut.Description = "显示 Codex Desktop Pet"
    $shortcut.Save()
} finally {
    if ($null -ne $shortcut) {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    }
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
}

Write-Output $installedExe
Write-Output $shortcutPath
