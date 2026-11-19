[CmdletBinding()]
param(
    [string]$TestSettingsDirectory
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

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

$defaults = [ordered]@{
    OfficialPetsCompanionMode = $false
    PetHidden = $false
    HotKey = "Ctrl+Alt+Q"
    OcrHotKey = "Ctrl+Alt+R"
    TogglePetHotKey = "Ctrl+Alt+H"
    ScreenWatchingEnabled = $false
    ScreenWatchingIntervalMinutes = 10
    WindowChangeCommentsEnabled = $false
    WindowChangeCommentCooldownMinutes = 2
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
                title = "Use official Pets Quick Chat"
                description = "Hide the standalone pet window and fill selected-text questions into official Pets for review before sending."
            }
            PetHidden = [ordered]@{
                type = "boolean"
                title = "Hide standalone pet"
                description = "Keep the Windows helper running but hide its standalone pet window until the show/hide shortcut is pressed."
            }
            HotKey = [ordered]@{
                type = "string"
                title = "Selected-text shortcut"
                description = "A Windows global shortcut such as Ctrl+Alt+Q."
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            OcrHotKey = [ordered]@{
                type = "string"
                title = "OCR region shortcut"
                description = "Freeze the desktop, drag a region, and ask Codex to recognize and explain it."
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            TogglePetHotKey = [ordered]@{
                type = "string"
                title = "Show/hide pet shortcut"
                description = "A Windows global shortcut such as Ctrl+Alt+H."
                minLength = 3
                maxLength = 64
                pattern = "^(?=.*\\+)(?:(?:Ctrl|Alt|Shift|Win)\\+)+(?:[A-Za-z0-9]|F(?:[1-9]|1[0-2])|Space|Enter|Tab|Escape|Delete|Insert|Home|End|PageUp|PageDown|Up|Down|Left|Right)$"
            }
            ScreenWatchingEnabled = [ordered]@{
                type = "boolean"
                title = "Periodic screen comments"
                description = "Periodically capture the current displays and show one privacy-aware pet comment. Off by default."
            }
            ScreenWatchingIntervalMinutes = [ordered]@{
                type = "integer"
                title = "Comment interval (minutes)"
                description = "Used only when periodic screen comments are enabled."
                minimum = 1
                maximum = 240
            }
            WindowChangeCommentsEnabled = [ordered]@{
                type = "boolean"
                title = "Comment after switching windows"
                description = "After a new window or page stays active briefly, capture one screenshot and comment. Off by default."
            }
            WindowChangeCommentCooldownMinutes = [ordered]@{
                type = "integer"
                title = "Window comment cooldown (minutes)"
                description = "Minimum time between automatic comments triggered by window or page changes."
                minimum = 1
                maximum = 60
            }
            StayOnTop = [ordered]@{
                type = "boolean"
                title = "Keep standalone pet on top"
            }
            UseFastMode = [ordered]@{
                type = "boolean"
                title = "Use Fast mode"
                description = "Faster supported-model responses with higher plan usage."
            }
        }
        required = @(
            "OfficialPetsCompanionMode",
            "PetHidden",
            "HotKey",
            "OcrHotKey",
            "TogglePetHotKey",
            "ScreenWatchingEnabled",
            "ScreenWatchingIntervalMinutes",
            "WindowChangeCommentsEnabled",
            "WindowChangeCommentCooldownMinutes",
            "StayOnTop",
            "UseFastMode"
        )
    }
}

function Get-SettingsLayout {
    return @(
        [ordered]@{
            kind = "group"
            title = "Interaction"
            items = @(
                [ordered]@{ kind = "property"; property = "OfficialPetsCompanionMode" },
                [ordered]@{ kind = "property"; property = "HotKey" },
                [ordered]@{ kind = "property"; property = "OcrHotKey" }
            )
        },
        [ordered]@{
            kind = "group"
            title = "Visibility"
            items = @(
                [ordered]@{ kind = "property"; property = "PetHidden" },
                [ordered]@{ kind = "property"; property = "TogglePetHotKey" },
                [ordered]@{ kind = "property"; property = "StayOnTop" }
            )
        },
        [ordered]@{
            kind = "group"
            title = "Automatic comments"
            items = @(
                [ordered]@{ kind = "property"; property = "ScreenWatchingEnabled" },
                [ordered]@{ kind = "property"; property = "ScreenWatchingIntervalMinutes" },
                [ordered]@{ kind = "property"; property = "WindowChangeCommentsEnabled" },
                [ordered]@{ kind = "property"; property = "WindowChangeCommentCooldownMinutes" }
            )
        },
        [ordered]@{
            kind = "group"
            title = "Appearance and performance"
            items = @(
                [ordered]@{ kind = "property"; property = "UseFastMode" },
                [ordered]@{
                    kind = "tool"
                    tool = "pet.restart"
                    description = "Restart the installed helper to apply saved settings."
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
            "ScreenWatchingEnabled" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "WindowChangeCommentsEnabled" {
                $updates[$property.Name] = Convert-StrictBoolean $property.Value $property.Name
            }
            "StayOnTop" {
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
            title = "Read desktop pet settings"
            description = "Read the native Windows desktop pet settings used by the plugin details page."
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            outputSchema = $readOutputSchema
            annotations = [ordered]@{ readOnlyHint = $true }
        },
        [ordered]@{
            name = "settings.update"
            title = "Update desktop pet settings"
            description = "Persist changed Windows desktop pet settings. Restart the helper to apply them."
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
            name = "pet.restart"
            title = "Restart desktop pet"
            description = "Restart the installed Windows helper so saved settings take effect."
            inputSchema = [ordered]@{ type = "object"; additionalProperties = $false }
            annotations = [ordered]@{ destructiveHint = $false }
        }
    )
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
            return [ordered]@{
                content = @([ordered]@{
                    type = "text"
                    text = "Settings saved. Restart the desktop pet to apply them."
                })
                structuredContent = [ordered]@{ values = $saved }
            }
        }
        "pet.restart" {
            if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
                throw "CodexDesktopPet.exe is not installed at $installedExe"
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
            return [ordered]@{
                content = @([ordered]@{ type = "text"; text = "Desktop pet restarted." })
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
                            version = "0.6.0"
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
                        version = "0.6.0"
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
