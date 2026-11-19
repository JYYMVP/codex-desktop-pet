$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

try {
    $inputJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputJson)) { exit 0 }
    $eventData = $inputJson | ConvertFrom-Json
    $eventName = [string]$eventData.hook_event_name
    $sessionId = [string]$eventData.session_id
    if ([string]::IsNullOrWhiteSpace($sessionId)) { exit 0 }

    $activityDirectory = Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
        "CodexDesktopPet\activity"
    [IO.Directory]::CreateDirectory($activityDirectory) | Out-Null

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
        sessionId = $sessionId
        turnId = [string]$eventData.turn_id
        event = $eventName
        updatedAtUtc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json -Compress

    $temporaryPath = "$statusPath.$PID.tmp"
    [IO.File]::WriteAllText($temporaryPath, $payload, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $statusPath -Force
} catch {
    # Activity mirroring is best-effort and must never block the user's turn.
    exit 0
}

exit 0
