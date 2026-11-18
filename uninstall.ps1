[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "$env:LOCALAPPDATA\CodexDesktopPet\app",
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$installFullPath = [IO.Path]::GetFullPath($InstallDirectory)
$dataRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexDesktopPet'))
$volumeRoot = [IO.Path]::GetPathRoot($installFullPath)
if ($installFullPath.TrimEnd('\') -eq $volumeRoot.TrimEnd('\')) {
    throw '拒绝把磁盘根目录作为卸载目标。'
}
$installedExe = Join-Path $installFullPath 'CodexDesktopPet.exe'
if ((Test-Path -LiteralPath $installFullPath) -and -not (Test-Path -LiteralPath $installedExe)) {
    throw "目标目录不包含 CodexDesktopPet.exe，拒绝递归删除：$installFullPath"
}
$running = Get-Process CodexDesktopPet -ErrorAction SilentlyContinue
if ($running) { throw 'Codex 桌宠正在运行。请从托盘菜单退出后再卸载。' }

$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 桌宠.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Desktop Pet\Codex 桌宠.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 桌宠.lnk')
)
foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

if (Test-Path -LiteralPath $installFullPath) {
    if ($PSCmdlet.ShouldProcess($installFullPath, '删除桌宠程序文件')) {
        Remove-Item -LiteralPath $installFullPath -Recurse -Force
    }
}

if ($RemoveUserData -and (Test-Path -LiteralPath $dataRoot)) {
    if ($PSCmdlet.ShouldProcess($dataRoot, '删除设置、日志、宠物和对话映射')) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
} else {
    Write-Host "已保留用户数据：$dataRoot"
}
Write-Host '卸载完成。' -ForegroundColor Green
