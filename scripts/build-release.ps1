[CmdletBinding()]
param(
    [string]$Version = "0.10.4",
    [string]$OutputDirectory,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $OutputDirectory "publish-win-x64"
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
[IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

dotnet publish (Join-Path $repositoryRoot "src\CodexDesktopPet\CodexDesktopPet.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:Version=$Version -p:FileVersion="$Version.0" -p:AssemblyVersion="$Version.0" `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

function Get-SignTool {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -File -Recurse `
        -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $candidate) { throw "signtool.exe was not found." }
    return $candidate.FullName
}

function Sign-File([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) { return }
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Signing certificate was not found: $CertificatePath"
    }
    $signTool = Get-SignTool
    & $signTool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com `
        /f $CertificatePath /p $CertificatePassword $Path
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $Path" }
}

Get-ChildItem -LiteralPath $publishDirectory -Filter *.exe -File | ForEach-Object {
    Sign-File $_.FullName
}

$zipPath = Join-Path $OutputDirectory "CodexDesktopPet-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath `
    -CompressionLevel Optimal

function Write-Checksum([string]$Path) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$Path.sha256"
    [IO.File]::WriteAllText($checksumPath, "$hash  $([IO.Path]::GetFileName($Path))`n",
        [Text.UTF8Encoding]::new($false))
}

Write-Checksum $zipPath

if (-not $SkipInstaller) {
    $innoCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $innoCompiler = $innoCandidates | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf)
    } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        throw "Inno Setup 6 was not found. Install it or use -SkipInstaller."
    }
    & $innoCompiler "/DAppVersion=$Version" "/DPublishDir=$publishDirectory" `
        "/DOutputDir=$OutputDirectory" `
        (Join-Path $repositoryRoot "packaging\CodexDesktopPet.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    $installerPath = Join-Path $OutputDirectory "CodexDesktopPet-Setup-x64.exe"
    Sign-File $installerPath
    Write-Checksum $installerPath
}

Get-ChildItem -LiteralPath $OutputDirectory -File |
    Where-Object { $_.Name -match '^CodexDesktopPet-(win-x64\.zip|Setup-x64\.exe)(\.sha256)?$' } |
    Select-Object Name, Length, LastWriteTime
