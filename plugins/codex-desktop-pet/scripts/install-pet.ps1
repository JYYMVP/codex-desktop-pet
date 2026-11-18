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

Write-Output $installedExe
