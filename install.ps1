[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:LOCALAPPDATA\CodexDesktopPet\app",
    [switch]$StartWithWindows,
    [switch]$DoNotLaunch
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'src\CodexDesktopPet\CodexDesktopPet.csproj'
$installFullPath = [IO.Path]::GetFullPath($InstallDirectory)
$dataRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexDesktopPet'))

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '没有找到 .NET 8 SDK。请先安装 .NET 8 SDK，再重新运行 install.ps1。'
}

$running = Get-Process CodexDesktopPet -ErrorAction SilentlyContinue
if ($running) {
    throw 'Codex 桌宠正在运行。请从托盘菜单退出后再安装。'
}

if (-not $installFullPath.StartsWith($dataRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "将安装到自定义目录：$installFullPath"
}

New-Item -ItemType Directory -Force -Path $installFullPath | Out-Null
dotnet publish $projectFile -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o $installFullPath
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败。' }

$exe = Join-Path $installFullPath 'CodexDesktopPet.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "没有生成程序：$exe" }

$shell = New-Object -ComObject WScript.Shell
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 桌宠.lnk'
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Desktop Pet'
New-Item -ItemType Directory -Force -Path $startMenuDirectory | Out-Null
$startMenuShortcut = Join-Path $startMenuDirectory 'Codex 桌宠.lnk'

foreach ($shortcutPath in @($desktopShortcut, $startMenuShortcut)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $installFullPath
    $shortcut.Description = 'Codex Desktop Pet'
    $shortcut.Save()
}

$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 桌宠.lnk'
if ($StartWithWindows) {
    Copy-Item -LiteralPath $startMenuShortcut -Destination $startupShortcut -Force
} elseif (Test-Path -LiteralPath $startupShortcut) {
    Remove-Item -LiteralPath $startupShortcut -Force
}

Write-Host "安装完成：$exe" -ForegroundColor Green
Write-Host '桌宠设置、日志和宠物库保存在 %LOCALAPPDATA%\CodexDesktopPet。'
if (-not $DoNotLaunch) { Start-Process -FilePath $exe }
