param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "shared-paths.ps1")
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$sourceExe = Join-Path $source "CodexDesktopPet.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "CodexDesktopPet.exe was not found in $source"
}

$destination = Join-Path (Get-CodexDesktopPetDataRoot) "app"
$destinationParent = [IO.Path]::GetDirectoryName($destination)
[IO.Directory]::CreateDirectory($destinationParent) | Out-Null
$switchId = [guid]::NewGuid().ToString("N")
$replacement = Join-Path $destinationParent ".app.new-$switchId"
$backup = Join-Path $destinationParent ".app.backup-$switchId"
$switched = $false
try {
    [IO.Directory]::CreateDirectory($replacement) | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $replacement -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $replacement "CodexDesktopPet.exe") -PathType Leaf)) {
        throw "The desktop pet replacement is incomplete."
    }
    $installedFullPath = [IO.Path]::GetFullPath((Join-Path $destination "CodexDesktopPet.exe"))
    $installedProcesses = @(Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue |
        Where-Object {
            try { [IO.Path]::GetFullPath($_.Path) -eq $installedFullPath }
            catch { $false }
        })
    foreach ($process in $installedProcesses) {
        $process | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    foreach ($process in $installedProcesses) {
        try { $process.WaitForExit(2000) } catch { }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $destination) {
        Move-Item -LiteralPath $destination -Destination $backup
    }
    try {
        Move-Item -LiteralPath $replacement -Destination $destination
        $switched = $true
    } catch {
        if (Test-Path -LiteralPath $backup) {
            Move-Item -LiteralPath $backup -Destination $destination
        }
        throw
    }

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

    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
    Write-Output $installedExe
    Write-Output $shortcutPath
} catch {
    if ($switched -and (Test-Path -LiteralPath $backup)) {
        try {
            if (Test-Path -LiteralPath $destination) {
                Remove-Item -LiteralPath $destination -Recurse -Force
            }
            Move-Item -LiteralPath $backup -Destination $destination
        } catch { }
    }
    throw
} finally {
    if (Test-Path -LiteralPath $replacement) {
        try { Remove-Item -LiteralPath $replacement -Recurse -Force } catch { }
    }
}
