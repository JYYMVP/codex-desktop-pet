$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
. (Join-Path $PSScriptRoot "..\scripts\shared-paths.ps1")

try {
    $inputJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputJson)) { exit 0 }
    $eventData = $inputJson | ConvertFrom-Json
    $eventName = [string]$eventData.hook_event_name
    $sessionId = [string]$eventData.session_id
    if ([string]::IsNullOrWhiteSpace($sessionId)) { exit 0 }

    $activityDirectory = Join-Path (Get-CodexDesktopPetDataRoot) "activity"
    [IO.Directory]::CreateDirectory($activityDirectory) | Out-Null
    $healthPath = Join-Path (Split-Path -Parent $activityDirectory) "hook-health.json"

    function Write-Health([string]$Event, [string]$State, [string]$SessionHash) {
        $healthPayload = [ordered]@{
            event = $Event
            state = $State
            sessionHash = $SessionHash
            updatedAtUtc = [DateTime]::UtcNow.ToString("o")
        } | ConvertTo-Json -Compress
        $temporaryHealthPath = "$healthPath.$PID.tmp"
        [IO.File]::WriteAllText(
            $temporaryHealthPath, $healthPayload, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporaryHealthPath -Destination $healthPath -Force
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sessionId))
    } finally {
        $sha.Dispose()
    }
    $safeSessionId = ([BitConverter]::ToString($hashBytes) -replace '-', '').Substring(0, 24)
    $statusPath = Join-Path $activityDirectory "$safeSessionId.json"

    if ($eventName -eq "SessionEnd") {
        Remove-Item -LiteralPath $statusPath -Force -ErrorAction SilentlyContinue
        Write-Health $eventName "Ended" $safeSessionId
        exit 0
    }

    $state = switch ($eventName) {
        "UserPromptSubmit" { "Running" }
        "PreModelRequest" { "Running" }
        "PreToolUse" { "Running" }
        "PostToolUse" { "Running" }
        "SubagentStart" { "Running" }
        "SubagentStop" { "Running" }
        "PermissionRequest" { "NeedsInput" }
        "Interrupt" { "Blocked" }
        "Stop" { "Ready" }
        "SessionStart" { "Ready" }
        default { $null }
    }
    if ($null -eq $state) { exit 0 }

    $payload = [ordered]@{
        state = $state
        sessionHash = $safeSessionId
        event = $eventName
        updatedAtUtc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json -Compress

    $temporaryPath = "$statusPath.$PID.tmp"
    [IO.File]::WriteAllText($temporaryPath, $payload, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $statusPath -Force
    Write-Health $eventName $state $safeSessionId
} catch {
    # Activity mirroring is best-effort and must never block the user's turn.
    exit 0
}

exit 0
