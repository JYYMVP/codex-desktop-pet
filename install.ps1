[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$StartWithWindows,
    [switch]$DoNotLaunch
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'plugins\codex-desktop-pet\scripts\shared-paths.ps1')
$projectFile = Join-Path $projectRoot 'src\CodexDesktopPet\CodexDesktopPet.csproj'
$dataRoot = [IO.Path]::GetFullPath((Get-CodexDesktopPetDataRoot))
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $dataRoot 'app'
}
$installFullPath = [IO.Path]::GetFullPath($InstallDirectory)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '没有找到 .NET 8 SDK。请先安装 .NET 8 SDK，再重新运行 install.ps1。'
}

$installedExePath = [IO.Path]::GetFullPath((Join-Path $installFullPath 'CodexDesktopPet.exe'))
$running = @(Get-Process CodexDesktopPet -ErrorAction SilentlyContinue | Where-Object {
    try { [IO.Path]::GetFullPath($_.Path) -eq $installedExePath }
    catch { $false }
})
if ($running.Count -gt 0) {
    foreach ($process in $running) { $process.Dispose() }
    throw 'Codex 桌宠正在运行。请从托盘菜单退出后再安装。'
}

if (-not $installFullPath.StartsWith($dataRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "将安装到自定义目录：$installFullPath"
}

$installParent = [IO.Path]::GetDirectoryName($installFullPath)
[IO.Directory]::CreateDirectory($installParent) | Out-Null
$switchId = [guid]::NewGuid().ToString('N')
$publishDirectory = Join-Path $installParent ".app.publish-$switchId"
$backupDirectory = Join-Path $installParent ".app.backup-$switchId"
$switched = $false
try {
    dotnet publish $projectFile -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败。' }

    $publishedExe = Join-Path $publishDirectory 'CodexDesktopPet.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw "没有生成程序：$publishedExe" }
    if (Test-Path -LiteralPath $installFullPath) {
        Move-Item -LiteralPath $installFullPath -Destination $backupDirectory
    }
    try {
        Move-Item -LiteralPath $publishDirectory -Destination $installFullPath
        $switched = $true
    } catch {
        if (Test-Path -LiteralPath $backupDirectory) {
            Move-Item -LiteralPath $backupDirectory -Destination $installFullPath
        }
        throw
    }

    $exe = Join-Path $installFullPath 'CodexDesktopPet.exe'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 桌宠.lnk'
        $startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Desktop Pet'
        New-Item -ItemType Directory -Force -Path $startMenuDirectory | Out-Null
        $startMenuShortcut = Join-Path $startMenuDirectory 'Codex 桌宠.lnk'

        foreach ($shortcutPath in @($desktopShortcut, $startMenuShortcut)) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            try {
                $shortcut.TargetPath = $exe
                $shortcut.Arguments = '--show'
                $shortcut.WorkingDirectory = $installFullPath
                $shortcut.Description = 'Codex Desktop Pet'
                $shortcut.Save()
            } finally {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
            }
        }
    } finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }

    $startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 桌宠.lnk'
    if ($StartWithWindows) {
        Copy-Item -LiteralPath $startMenuShortcut -Destination $startupShortcut -Force
    } elseif (Test-Path -LiteralPath $startupShortcut) {
        Remove-Item -LiteralPath $startupShortcut -Force
    }

    Write-Host "安装完成：$exe" -ForegroundColor Green
    Write-Host '桌宠设置、日志和宠物库保存在 %LOCALAPPDATA%\CodexDesktopPet。'
    if (-not $DoNotLaunch) {
        Start-Process -FilePath $exe -ArgumentList '--show'
    }
    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
} catch {
    if ($switched -and (Test-Path -LiteralPath $backupDirectory)) {
        try {
            if (Test-Path -LiteralPath $installFullPath) {
                Remove-Item -LiteralPath $installFullPath -Recurse -Force
            }
            Move-Item -LiteralPath $backupDirectory -Destination $installFullPath
        } catch { }
    }
    throw
} finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        try { Remove-Item -LiteralPath $publishDirectory -Recurse -Force } catch { }
    }
}
