[CmdletBinding()]
param(
    [string]$Repository = "JYYMVP/codex-desktop-pet",
    [string]$ReleaseDirectory,
    [string]$InstallDirectory,
    [switch]$DoNotLaunch
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$usingTestPaths = -not [string]::IsNullOrWhiteSpace($ReleaseDirectory) -or
                  -not [string]::IsNullOrWhiteSpace($InstallDirectory)
if ($usingTestPaths -and $env:CODEX_DESKTOP_PET_ENABLE_TEST_RELEASE -ne "1") {
    throw "Custom release and install directories are available only in test mode."
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("CodexDesktopPet\update-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$zipPath = Join-Path $temporaryRoot "CodexDesktopPet-win-x64.zip"
$checksumPath = "$zipPath.sha256"

function Copy-Or-DownloadAssets {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
        $source = [IO.Path]::GetFullPath($ReleaseDirectory)
        Copy-Item -LiteralPath (Join-Path $source "CodexDesktopPet-win-x64.zip") `
            -Destination $zipPath
        Copy-Item -LiteralPath (Join-Path $source "CodexDesktopPet-win-x64.zip.sha256") `
            -Destination $checksumPath
        return "local-test-release"
    }

    $headers = @{
        "User-Agent" = "CodexDesktopPet-Updater"
        "Accept" = "application/vnd.github+json"
    }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" `
        -Headers $headers
    $zipAsset = $release.assets | Where-Object name -eq "CodexDesktopPet-win-x64.zip" |
        Select-Object -First 1
    $checksumAsset = $release.assets |
        Where-Object name -eq "CodexDesktopPet-win-x64.zip.sha256" | Select-Object -First 1
    if ($null -eq $zipAsset -or $null -eq $checksumAsset) {
        throw "The latest GitHub release does not contain the Windows ZIP and checksum assets."
    }
    foreach ($asset in @($zipAsset, $checksumAsset)) {
        $uri = [Uri]$asset.browser_download_url
        if ($uri.Scheme -ne "https" -or $uri.Host -ne "github.com") {
            throw "Release asset URL is not a trusted GitHub HTTPS URL."
        }
    }
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $headers
    Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumPath `
        -Headers $headers
    return [string]$release.tag_name
}

function Expand-ReleaseSafely([string]$Archive, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveFile = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        if ($archiveFile.Entries.Count -gt 500 -or
            ($archiveFile.Entries | Measure-Object Length -Sum).Sum -gt 300MB) {
            throw "Release archive is too large."
        }
        $safeRoot = [IO.Path]::GetFullPath($Destination) + [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $archiveFile.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release archive contains an unsafe path."
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                [IO.Directory]::CreateDirectory($target) | Out-Null
                continue
            }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally {
        $archiveFile.Dispose()
    }
}

try {
    $releaseName = Copy-Or-DownloadAssets
    $checksumText = [IO.File]::ReadAllText($checksumPath).Trim()
    $match = [regex]::Match($checksumText, "^[a-fA-F0-9]{64}")
    if (-not $match.Success) { throw "Release checksum file is invalid." }
    $expectedHash = $match.Value.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) { throw "Release checksum verification failed." }

    $stagingDirectory = Join-Path $temporaryRoot "staging"
    [IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
    Expand-ReleaseSafely $zipPath $stagingDirectory
    $stagedExe = Join-Path $stagingDirectory "CodexDesktopPet.exe"
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "Release archive does not contain CodexDesktopPet.exe."
    }
    if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $stagedExe
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "The downloaded desktop pet signature is $($signature.Status); installation was stopped."
        }
    }

    if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
        $InstallDirectory = Join-Path $env:LOCALAPPDATA "CodexDesktopPet\app"
    }
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
    [IO.Directory]::CreateDirectory($InstallDirectory) | Out-Null
    $installedExe = Join-Path $InstallDirectory "CodexDesktopPet.exe"

    if (-not $usingTestPaths) {
        $installedFullPath = [IO.Path]::GetFullPath($installedExe)
        Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue |
            Where-Object {
                try { [IO.Path]::GetFullPath($_.Path) -eq $installedFullPath }
                catch { $false }
            } | Stop-Process -Force
        Start-Sleep -Milliseconds 300
    }

    Copy-Item -Path (Join-Path $stagingDirectory "*") -Destination $InstallDirectory `
        -Recurse -Force
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw "Desktop pet installation did not produce $installedExe"
    }
    if (-not $usingTestPaths) {
        $programsDirectory = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::Programs)
        $shortcutPath = Join-Path $programsDirectory "Codex Desktop Pet.lnk"
        $shell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $installedExe
            $shortcut.Arguments = "--show"
            $shortcut.WorkingDirectory = $InstallDirectory
            $shortcut.IconLocation = "$installedExe,0"
            $shortcut.Description = "显示 Codex Desktop Pet"
            $shortcut.Save()
        } finally {
            if ($null -ne $shortcut) {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
            }
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
        }
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe).FileVersion
    if (-not $DoNotLaunch) {
        Start-Process -FilePath $installedExe -ArgumentList "--show" -WindowStyle Hidden
    }
    Write-Output "Installed Codex Desktop Pet $version from $releaseName."
    Write-Output $installedExe
} finally {
    try {
        $fullTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $allowedRoot = [IO.Path]::GetFullPath(
            (Join-Path ([IO.Path]::GetTempPath()) "CodexDesktopPet")) +
            [IO.Path]::DirectorySeparatorChar
        if ($fullTemporaryRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $fullTemporaryRoot)) {
            Remove-Item -LiteralPath $fullTemporaryRoot -Recurse -Force
        }
    } catch { }
}
