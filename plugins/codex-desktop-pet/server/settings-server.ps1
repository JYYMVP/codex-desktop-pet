[CmdletBinding()]
param(
    [string]$TestSettingsDirectory
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$settingsDirectory = if ([string]::IsNullOrWhiteSpace($TestSettingsDirectory)) {
    Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
        "CodexDesktopPet"
} else {
    if ($env:CODEX_DESKTOP_PET_ENABLE_TEST_ROOT -ne "1") {
        throw "TestSettingsDirectory is available only when CODEX_DESKTOP_PET_ENABLE_TEST_ROOT=1."
    }
    [IO.Path]::GetFullPath($TestSettingsDirectory)
}
$settingsPath = Join-Path $settingsDirectory "settings.json"
$installedExe = Join-Path $settingsDirectory "app\CodexDesktopPet.exe"
$hookHealthPath = Join-Path $settingsDirectory "hook-health.json"
$installLatestScript = Join-Path $PSScriptRoot "..\scripts\install-latest-release.ps1"
$uiTextPath = Join-Path $PSScriptRoot "settings-ui.zh-CN.json"
$uiText = if (Test-Path -LiteralPath $uiTextPath -PathType Leaf) {
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::ReadAllText($uiTextPath, $utf8) | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}

function Get-UiText([string]$Name, [string]$Fallback) {
    $property = $uiText.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return $Fallback
    }
    return [string]$property.Value
}

$defaults = [ordered]@{
    OfficialPetsCompanionMode = $false
    PetHidden = $false
    BindLifetimeToCodex = $true
    IdleActivityLevel = 1
    ReduceMotion = $false
    HotKey = "Ctrl+Alt+Q"
    OcrHotKey = "Ctrl+Alt+R"
    TogglePetHotKey = "Ctrl+Alt+H"
    ScreenWatchingEnabled = $false
    ScreenWatchingIntervalMinutes = 10
    WindowChangeCommentsEnabled = $false
    WindowChangeCommentCooldownMinutes = 2
    CommentBubbleSeconds = 14
    FollowCodexActivity = $true
    FollowPointerWithPet = $true
    StayOnTop = $true
    UseFastMode = $false
}

function Write-ProtocolMessage([object]$Message) {
    $json = $Message | ConvertTo-Json -Depth 30 -Compress
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Write-Result([object]$Id, [object]$Result) {
    Write-ProtocolMessage ([ordered]@{
        jsonrpc = "2.0"
        id = $Id
        result = $Result
    })
}

function Write-ErrorResponse([object]$Id, [int]$Code, [string]$Message) {
    Write-ProtocolMessage ([ordered]@{
        jsonrpc = "2.0"
        id = $Id
        error = [ordered]@{ code = $Code; message = $Message }
    })
}

function Read-SettingsState {
    $raw = [pscustomobject]@{}
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        try { $raw = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json }
        catch { $raw = [pscustomobject]@{} }
    }

    $values = [ordered]@{}
    foreach ($entry in $defaults.GetEnumerator()) {
        $property = $raw.PSObject.Properties[$entry.Key]
        $values[$entry.Key] = if ($null -eq $property -or $null -eq $property.Value) {
            $entry.Value
        } else {
            $property.Value
        }
    }
    return [pscustomobject]@{ Raw = $raw; Values = $values }
}

function Write-SettingsState([object]$Raw, [Collections.IDictionary]$ChangedValues) {
    foreach ($entry in $ChangedValues.GetEnumerator()) {
        $property = $Raw.PSObject.Properties[$entry.Key]
        if ($null -eq $property) {
            $Raw | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        } else {
            $property.Value = $entry.Value
        }
    }

    [IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null
    $temporaryPath = Join-Path $settingsDirectory "settings.json.tmp"
    $json = $Raw | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $settingsPath -Force
}

function Get-SettingsSchema {
    return [ordered]@{
        type = "object"
        properties = [ordered]@{
            OfficialPetsCompanionMode = [ordered]@{
                type = "boolean"
                title = (Get-UiText "OfficialPetsTitle" "Experimental official Pets mode")
                description = (Get-UiText "OfficialPetsDescription" "Retained for compatibility testing; standalone pet development is prioritized.")
            }
            PetHidden = [ordered]@{
                type = "boolean"
                title = (Get-UiText "HidePetTitle" "Hide desktop pet")
                description = (Get-UiText "HidePetDescription" "Keep the helper running but hide the pet until the show/hide shortcut is pressed.")
            }
            BindLifetimeToCodex = [ordered]@{
                type = "boolean"
                title = (Get-UiText "BindLifetimeTitle" "Start and exit with Codex")
                description = (Get-UiText "BindLifetimeDescription" "Start automatically from Codex hooks and exit after the trusted Codex desktop process closes.")
            }
            IdleActivityLevel = [ordered]@{
                type = "integer"
                title = (Get-UiText "IdleActivityTitle" "Idle activity")
                description = (Get-UiText "IdleActivityDescription" "0 quiet, 1 standard, 2 lively; controls occasional idle gestures.")
                minimum = 0
                maximum = 2
            }
            ReduceMotion = [ordered]@{
                type = "boolean"
                title = (Get-UiText "ReduceMotionTitle" "Reduce motion")
                description = (Get-UiText "ReduceMotionDescription" "Disable bobbing, pulsing, hover hops, and random idle gestures.")
            }
            HotKey = [ordered]@{
                type = "string"
                title = (Get-UiText "SelectionHotKeyTitle" "Selected-text shortcut")
                description = (Get-UiText "SelectionHotKeyDescription" "A Windows global shortcut such as Ctrl+Alt+Q.")
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            OcrHotKey = [ordered]@{
                type = "string"
                title = (Get-UiText "OcrHotKeyTitle" "OCR region shortcut")
                description = (Get-UiText "OcrHotKeyDescription" "Freeze the desktop, drag a region, and ask Codex to recognize and explain it.")
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            TogglePetHotKey = [ordered]@{
                type = "string"
                title = (Get-UiText "ToggleHotKeyTitle" "Show/hide shortcut")
                description = (Get-UiText "ToggleHotKeyDescription" "Remains available while the pet is hidden, for example Ctrl+Alt+H.")
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            ScreenWatchingEnabled = [ordered]@{
                type = "boolean"
                title = (Get-UiText "PeriodicCommentsTitle" "Periodic screen comments")
                description = (Get-UiText "PeriodicCommentsDescription" "Capture the displays at an interval and show a separate comment bubble. Off by default.")
            }
            ScreenWatchingIntervalMinutes = [ordered]@{
                type = "integer"
                title = (Get-UiText "PeriodicIntervalTitle" "Periodic comment interval (minutes)")
                description = (Get-UiText "PeriodicIntervalDescription" "Used only when periodic screen comments are enabled.")
                minimum = 1
                maximum = 240
            }
            WindowChangeCommentsEnabled = [ordered]@{
                type = "boolean"
                title = (Get-UiText "WindowCommentsTitle" "Comment after switching windows")
                description = (Get-UiText "WindowCommentsDescription" "Capture and comment after a new window or page remains active. Off by default.")
            }
            WindowChangeCommentCooldownMinutes = [ordered]@{
                type = "integer"
                title = (Get-UiText "WindowCooldownTitle" "Window comment cooldown (minutes)")
                description = (Get-UiText "WindowCooldownDescription" "Minimum time between comments triggered by window or page changes.")
                minimum = 1
                maximum = 60
            }
            CommentBubbleSeconds = [ordered]@{
                type = "integer"
                title = (Get-UiText "BubbleDurationTitle" "Comment bubble duration (seconds)")
                description = (Get-UiText "BubbleDurationDescription" "How long the separate comment bubble remains visible.")
                minimum = 4
                maximum = 60
            }
            FollowCodexActivity = [ordered]@{
                type = "boolean"
                title = (Get-UiText "FollowActivityTitle" "Follow Codex activity")
                description = (Get-UiText "FollowActivityDescription" "Animate from lifecycle status only, without reading prompts, answers, or chat history.")
            }
            FollowPointerWithPet = [ordered]@{
                type = "boolean"
                title = (Get-UiText "FollowPointerTitle" "Look toward the pointer")
                description = (Get-UiText "FollowPointerDescription" "Use the 16 look-direction frames in v2 pet packs. Other packs are ignored safely.")
            }
            StayOnTop = [ordered]@{
                type = "boolean"
                title = (Get-UiText "StayOnTopTitle" "Keep desktop pet on top")
            }
            UseFastMode = [ordered]@{
                type = "boolean"
                title = (Get-UiText "FastModeTitle" "Use Fast mode")
                description = (Get-UiText "FastModeDescription" "Faster supported-model responses with higher plan usage.")
            }
        }
        required = @(
            "OfficialPetsCompanionMode",
            "PetHidden",
            "BindLifetimeToCodex",
            "IdleActivityLevel",
            "ReduceMotion",
            "HotKey",
            "OcrHotKey",
            "TogglePetHotKey",
            "ScreenWatchingEnabled",
            "ScreenWatchingIntervalMinutes",
            "WindowChangeCommentsEnabled",
            "WindowChangeCommentCooldownMinutes",
            "CommentBubbleSeconds",
            "FollowCodexActivity",
            "FollowPointerWithPet",
            "StayOnTop",
            "UseFastMode"
        )
    }
}

function Get-SettingsLayout {
    return @(
        [ordered]@{
            kind = "group"
            title = (Get-UiText "QuickControlsGroup" "Quick controls")
            items = @(
                [ordered]@{ kind = "property"; property = "PetHidden" },
                [ordered]@{ kind = "property"; property = "BindLifetimeToCodex" },
                [ordered]@{ kind = "property"; property = "IdleActivityLevel" },
                [ordered]@{ kind = "property"; property = "ReduceMotion" },
                [ordered]@{ kind = "property"; property = "FollowCodexActivity" },
                [ordered]@{ kind = "property"; property = "FollowPointerWithPet" },
                [ordered]@{ kind = "property"; property = "ScreenWatchingEnabled" },
                [ordered]@{ kind = "property"; property = "WindowChangeCommentsEnabled" },
                [ordered]@{ kind = "property"; property = "StayOnTop" },
                [ordered]@{
                    kind = "tool"
                    tool = "pet.install-latest"
                    description = (Get-UiText "InstallLatestDescription" "Download, verify, install, and start the latest GitHub release.")
                },
                [ordered]@{
                    kind = "tool"
                    tool = "pet.activity-diagnostics"
                    description = (Get-UiText "ActivityDiagnosticsDescription" "Check whether trusted Codex lifecycle hooks are reaching the desktop pet.")
                }
            )
        },
        [ordered]@{
            kind = "group"
            title = (Get-UiText "ShortcutsGroup" "Shortcuts")
            items = @(
                [ordered]@{ kind = "property"; property = "HotKey" },
                [ordered]@{ kind = "property"; property = "OcrHotKey" },
                [ordered]@{ kind = "property"; property = "TogglePetHotKey" }
            )
        },
        [ordered]@{
            kind = "group"
            title = (Get-UiText "CommentTimingGroup" "Screen comment timing")
            items = @(
                [ordered]@{ kind = "property"; property = "ScreenWatchingIntervalMinutes" },
                [ordered]@{ kind = "property"; property = "WindowChangeCommentCooldownMinutes" },
                [ordered]@{ kind = "property"; property = "CommentBubbleSeconds" }
            )
        },
        [ordered]@{
            kind = "group"
            title = (Get-UiText "PerformanceGroup" "Performance and experiments")
            items = @(
                [ordered]@{ kind = "property"; property = "UseFastMode" },
                [ordered]@{ kind = "property"; property = "OfficialPetsCompanionMode" },
                [ordered]@{
                    kind = "tool"
                    tool = "pet.restart"
                    description = (Get-UiText "RestartDescription" "Restart the installed pet to apply saved settings immediately.")
                }
            )
        }
    )
}

function Get-SettingsReadResult {
    $state = Read-SettingsState
    return [ordered]@{
        schema = Get-SettingsSchema
        values = $state.Values
        layout = @(Get-SettingsLayout)
    }
}

function Test-HotKey([string]$Value) {
    return $Value -match '^(?=.*\+)(?:(?:Ctrl|Alt|Shift|Win)\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$'
}

function Convert-StrictBoolean([object]$Value, [string]$Name) {
    if ($Value -isnot [bool]) {
        throw "$Name must be a JSON boolean."
    }
    return [bool]$Value
}

function Convert-StrictInteger(
    [object]$Value,
    [string]$Name,
    [int]$Minimum,
    [int]$Maximum
) {
    if ($Value -is [bool] -or $Value -is [string] -or $null -eq $Value) {
        throw "$Name must be a JSON integer."
    }
    try { $decimalValue = [decimal]$Value }
    catch { throw "$Name must be a JSON integer." }
    if ([decimal]::Truncate($decimalValue) -ne $decimalValue -or
        $decimalValue -lt $Minimum -or $decimalValue -gt $Maximum) {
        throw "$Name must be between $Minimum and $Maximum."
    }
    return [int]$decimalValue
}

function Convert-SettingUpdates([object]$SetObject) {
    if ($null -eq $SetObject) { throw "The set object is required." }
    $allowed = @($defaults.Keys)
    $updates = [ordered]@{}
    foreach ($property in $SetObject.PSObject.Properties) {
        if ($allowed -notcontains $property.Name) {
            throw "Unsupported setting: $($property.Name)"
        }
        switch ($property.Name) {
            "OfficialPetsCompanionMode" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "PetHidden" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "BindLifetimeToCodex" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "ReduceMotion" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "ScreenWatchingEnabled" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "WindowChangeCommentsEnabled" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "StayOnTop" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "FollowCodexActivity" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "FollowPointerWithPet" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "UseFastMode" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "HotKey" {
                $value = [string]$property.Value
                if (-not (Test-HotKey $value)) { throw "HotKey is not a supported modifier combination." }
                $updates[$property.Name] = $value
            }
            "OcrHotKey" {
                $value = [string]$property.Value
                if (-not (Test-HotKey $value)) { throw "OcrHotKey is not a supported modifier combination." }
                $updates[$property.Name] = $value
            }
            "TogglePetHotKey" {
                $value = [string]$property.Value
                if (-not (Test-HotKey $value)) { throw "TogglePetHotKey is not a supported modifier combination." }
                $updates[$property.Name] = $value
            }
            "ScreenWatchingIntervalMinutes" {
                $updates[$property.Name] = Convert-StrictInteger `
                    $property.Value $property.Name 1 240
            }
            "WindowChangeCommentCooldownMinutes" {
                $updates[$property.Name] = Convert-StrictInteger `
                    $property.Value $property.Name 1 60
            }
            "CommentBubbleSeconds" {
                $updates[$property.Name] = Convert-StrictInteger `
                    $property.Value $property.Name 4 60
            }
            "IdleActivityLevel" {
                $updates[$property.Name] = Convert-StrictInteger `
                    $property.Value $property.Name 0 2
            }
        }
    }
    if ($updates.Count -eq 0) { throw "At least one changed setting is required." }
    $current = (Read-SettingsState).Values
    $selectionHotKey = if ($updates.Contains("HotKey")) { $updates["HotKey"] } else { $current.HotKey }
    $ocrHotKey = if ($updates.Contains("OcrHotKey")) { $updates["OcrHotKey"] } else { $current.OcrHotKey }
    $togglePetHotKey = if ($updates.Contains("TogglePetHotKey")) {
        $updates["TogglePetHotKey"]
    } else { $current.TogglePetHotKey }
    $normalizedHotKeys = @($selectionHotKey, $ocrHotKey, $togglePetHotKey) |
        ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique
    if ($normalizedHotKeys.Count -ne 3) {
        throw "HotKey, OcrHotKey and TogglePetHotKey must be different."
    }
    return $updates
}

function Get-ToolDefinitions {
    $readOutputSchema = [ordered]@{
        type = "object"
        properties = [ordered]@{
            schema = [ordered]@{ type = "object" }
            values = [ordered]@{ type = "object" }
            layout = [ordered]@{ type = "array"; items = [ordered]@{ type = "object" } }
        }
        required = @("schema", "values")
    }
    $settingProperties = (Get-SettingsSchema).properties
    return @(
        [ordered]@{
            name = "settings.read"
            title = (Get-UiText "ReadSettingsTitle" "Read desktop pet settings")
            description = (Get-UiText "ReadSettingsDescription" "Read the native Windows desktop pet settings used by the plugin details page.")
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            outputSchema = $readOutputSchema
            annotations = [ordered]@{ readOnlyHint = $true }
        },
        [ordered]@{
            name = "settings.update"
            title = (Get-UiText "UpdateSettingsTitle" "Update desktop pet settings")
            description = (Get-UiText "UpdateSettingsDescription" "Persist changed Windows desktop pet settings and restart the helper to apply them.")
            inputSchema = [ordered]@{
                type = "object"
                properties = [ordered]@{
                    set = [ordered]@{
                        type = "object"
                        properties = $settingProperties
                        minProperties = 1
                        additionalProperties = $false
                    }
                }
                required = @("set")
                additionalProperties = $false
            }
            outputSchema = [ordered]@{
                type = "object"
                properties = [ordered]@{ values = [ordered]@{ type = "object" } }
                required = @("values")
            }
        },
        [ordered]@{
            name = "pet.install-latest"
            title = (Get-UiText "InstallLatestTitle" "Install or update desktop pet")
            description = (Get-UiText "InstallLatestDescription" "Download, verify, install, and start the latest GitHub release.")
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            annotations = [ordered]@{ destructiveHint = $false; openWorldHint = $true }
        },
        [ordered]@{
            name = "pet.activity-diagnostics"
            title = (Get-UiText "ActivityDiagnosticsTitle" "Check activity connection")
            description = (Get-UiText "ActivityDiagnosticsDescription" "Check whether trusted Codex lifecycle hooks are reaching the desktop pet.")
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            annotations = [ordered]@{ readOnlyHint = $true }
        },
        [ordered]@{
            name = "pet.restart"
            title = (Get-UiText "RestartTitle" "Restart desktop pet")
            description = (Get-UiText "RestartDescription" "Restart the installed Windows helper so saved settings take effect.")
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            annotations = [ordered]@{ destructiveHint = $false }
        }
    )
}

function Restart-InstalledPet([bool]$RequireInstalled = $true) {
    if (-not [string]::IsNullOrWhiteSpace($TestSettingsDirectory)) {
        return
    }
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        if ($RequireInstalled) {
            throw "CodexDesktopPet.exe is not installed at $installedExe"
        }
        return
    }
    $installedFullPath = [IO.Path]::GetFullPath($installedExe)
    Get-Process -Name "CodexDesktopPet" -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path) -eq $installedFullPath
            } catch { $false }
        } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 250
    Start-Process -FilePath $installedExe -WindowStyle Hidden
}

function Invoke-Tool([string]$Name, [object]$Arguments) {
    switch ($Name) {
        "settings.read" {
            $structured = Get-SettingsReadResult
            return [ordered]@{ content = @(); structuredContent = $structured }
        }
        "settings.update" {
            $updates = Convert-SettingUpdates $Arguments.set
            $state = Read-SettingsState
            Write-SettingsState $state.Raw $updates
            $saved = (Read-SettingsState).Values
            Restart-InstalledPet $false
            return [ordered]@{
                content = @()
                structuredContent = [ordered]@{ values = $saved }
            }
        }
        "pet.restart" {
            Restart-InstalledPet
            return [ordered]@{
                content = @([ordered]@{
                    type = "text"
                    text = (Get-UiText "RestartedMessage" "Desktop pet restarted.")
                })
            }
        }
        "pet.install-latest" {
            if (-not (Test-Path -LiteralPath $installLatestScript -PathType Leaf)) {
                throw "The release installer script is missing from the plugin package."
            }
            $output = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
                -File $installLatestScript 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw (($output | Out-String).Trim())
            }
            return [ordered]@{
                content = @([ordered]@{
                    type = "text"
                    text = (($output | Out-String).Trim())
                })
            }
        }
        "pet.activity-diagnostics" {
            $message = if (-not (Test-Path -LiteralPath $hookHealthPath -PathType Leaf)) {
                Get-UiText "ActivityDiagnosticsMissing" "No lifecycle hook event has reached the pet. Review and trust the plugin hook in Codex Settings > Hooks, then start a new turn."
            } else {
                try {
                    $health = Get-Content -Raw -LiteralPath $hookHealthPath | ConvertFrom-Json
                    $format = Get-UiText "ActivityDiagnosticsHealthy" "Last hook event: {0}; state: {1}; time: {2}."
                    [string]::Format($format, $health.event, $health.state, $health.updatedAtUtc)
                } catch {
                    Get-UiText "ActivityDiagnosticsInvalid" "The hook health record is unreadable. Review the plugin hook and retry."
                }
            }
            return [ordered]@{
                content = @([ordered]@{ type = "text"; text = $message })
            }
        }
        default { throw "Unknown tool: $Name" }
    }
}

$settingsCapability = [ordered]@{
    readTool = "settings.read"
    updateTool = "settings.update"
}

while ($null -ne ($line = [Console]::In.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $request = $line | ConvertFrom-Json
        $hasId = $null -ne $request.PSObject.Properties["id"]
        if (-not $hasId) { continue }
        switch ([string]$request.method) {
            "server/discover" {
                Write-Result $request.id ([ordered]@{
                    resultType = "complete"
                    supportedVersions = @("2026-07-28")
                    _meta = [ordered]@{
                        "io.modelcontextprotocol/serverInfo" = [ordered]@{
                            name = "codex-desktop-pet-settings"
                            version = "0.10.1"
                        }
                    }
                    capabilities = [ordered]@{
                        tools = [ordered]@{}
                        extensions = [ordered]@{ "openai/settings" = $settingsCapability }
                    }
                })
            }
            "initialize" {
                $protocolVersion = if ($request.params.protocolVersion) {
                    [string]$request.params.protocolVersion
                } else { "2025-11-25" }
                Write-Result $request.id ([ordered]@{
                    protocolVersion = $protocolVersion
                    serverInfo = [ordered]@{
                        name = "codex-desktop-pet-settings"
                        version = "0.10.1"
                    }
                    capabilities = [ordered]@{
                        tools = [ordered]@{ listChanged = $false }
                        extensions = [ordered]@{ "openai/settings" = $settingsCapability }
                        experimental = [ordered]@{ "openai/settings" = $settingsCapability }
                    }
                })
            }
            "ping" { Write-Result $request.id ([ordered]@{}) }
            "tools/list" { Write-Result $request.id ([ordered]@{ tools = @(Get-ToolDefinitions) }) }
            "tools/call" {
                try {
                    Write-Result $request.id (Invoke-Tool ([string]$request.params.name) $request.params.arguments)
                } catch {
                    Write-Result $request.id ([ordered]@{
                        content = @([ordered]@{ type = "text"; text = $_.Exception.Message })
                        isError = $true
                    })
                }
            }
            default { Write-ErrorResponse $request.id -32601 "Method not found." }
        }
    } catch {
        Write-ErrorResponse $null -32603 $_.Exception.Message
    }
}
