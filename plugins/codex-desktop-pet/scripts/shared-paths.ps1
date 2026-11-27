function Get-CodexDesktopPetLocalApplicationData {
    $reported = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::LocalApplicationData)

    # Codex Desktop may launch plugin commands in an isolated MSIX sandbox.
    # Its redirected LocalApplicationData contains a per-process sandbox id,
    # so it cannot be used to communicate with the native desktop helper.
    if ($reported -match '[\\/]AC[\\/]#!sandbox\.\{[^}]+\}(?:[\\/]|$)') {
        $profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if (-not [string]::IsNullOrWhiteSpace($profile)) {
            return [IO.Path]::GetFullPath((Join-Path $profile 'AppData\Local'))
        }
    }

    return [IO.Path]::GetFullPath($reported)
}

function Get-CodexDesktopPetDataRoot {
    if ($env:CODEX_DESKTOP_PET_ENABLE_TEST_ROOT -eq '1' -and
        -not [string]::IsNullOrWhiteSpace($env:CODEX_DESKTOP_PET_DATA_ROOT)) {
        return [IO.Path]::GetFullPath($env:CODEX_DESKTOP_PET_DATA_ROOT)
    }
    return Join-Path (Get-CodexDesktopPetLocalApplicationData) 'CodexDesktopPet'
}
