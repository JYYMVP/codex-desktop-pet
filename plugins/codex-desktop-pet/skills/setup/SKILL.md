---
name: setup-codex-desktop-pet
description: Set up or troubleshoot the Windows Codex Desktop Pet, including official Pets companion mode, its executable, global shortcut, selection capture, pixel-pet files, and local diagnostic log. Use only for this desktop companion, not real animals.
---

# Codex Desktop Pet setup

Help the user install, start, configure, or diagnose the Windows desktop companion in this repository.

Preserve these boundaries:

- The Windows process owns the global shortcut and selection capture. In standalone mode it also owns the transparent window and pixel rendering.
- The plugin supplies discovery, onboarding, and a trusted session-start hook. Screen watching belongs to the Windows process, is off by default, and must be enabled by the user.
- Trusted lifecycle hooks mirror only per-session activity metadata into `%LOCALAPPDATA%/CodexDesktopPet/activity`. They never copy prompts, answers, selected text, tool arguments, or transcript contents. The standalone pet aggregates those files into Running, Needs input, Ready, and Blocked animations.
- Selected text is sent to Codex only after the user presses the configured shortcut.
- OCR uses a separate configurable shortcut. It must freeze the desktop for an explicit drag selection, send only the cropped region, and delete both temporary images on success, failure, or cancellation.
- Do not copy code or assets from OpenAI's proprietary Work Pets plugin. Independently implement any compatible exporter from documented file contracts.

Official Pets companion mode is opt-in. It hides the independent pet window, opens the official Pets Quick Chat with `Windows+Alt+P`, and pastes a bounded selected-text prompt. It must leave the prompt unsent so the user can inspect it and press Enter. If the Pets controls do not open, verify that the current ChatGPT desktop app supports Pets and that a pet is enabled before changing the helper.

Use the tray's **Official Pets diagnostics** report to distinguish a missing desktop process, an untrusted executable path, a Quick Chat focus timeout, and a completed safe paste. The diagnostic is read-only and cannot determine whether the host's private Pets toggle is enabled.

The plugin exposes structured settings through its local `desktop-pet-settings` MCP server. Prefer the native controls on the plugin details page for standalone-pet visibility, Codex activity and pointer following, the show/hide shortcut, official Pets mode, selection and OCR shortcuts, periodic and window-change comments, timing, always-on-top, and Fast mode. These controls persist `%LOCALAPPDATA%/CodexDesktopPet/settings.json` and automatically restart the installed helper after a successful update. Use **Restart desktop pet** only as a recovery action when settings were changed outside the plugin page.

For setup, first check Windows, the .NET 8 runtime, `codex.exe`, and the expected executable path. Explain any missing dependency before changing the system. Build or install only when the user requested setup. After publishing the Windows project, run `${PLUGIN_ROOT}/scripts/install-pet.ps1 -SourceDirectory <publish-directory>` and verify `%LOCALAPPDATA%/CodexDesktopPet/app/CodexDesktopPet.exe`, the process, and the log. Use `uninstall-pet.ps1` when the user asks to remove the installed application files; it preserves settings and logs.

For troubleshooting, inspect `%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log`, then distinguish hotkey registration, selection capture, Codex connectivity, and rendering failures. Prefer the smallest repair and retest the actual shortcut path.

The plugin's structured settings include `HotKey` for selected text, `OcrHotKey` for region recognition, `TogglePetHotKey` for showing or hiding the standalone pet, and `PetHidden` for its persisted visibility. The three shortcuts must be valid, distinct Windows modifier combinations. Their defaults are `Ctrl+Alt+Q`, `Ctrl+Alt+R`, and `Ctrl+Alt+H`.

The local pet library can clean-room import an existing ChatGPT Pets v1 or v2 sprite sheet from PNG or a Windows-decodable WebP. Validate its documented dimensions, required cells, transparent unused cells, and alpha background before extracting frames. Never download or redistribute built-in artwork on the user's behalf unless the user has separately established permission; importing a local file does not grant redistribution rights.

`FollowCodexActivity` enables official-style activity mirroring for the standalone pet. Updating these hooks changes their trust hash, so tell the user to review and trust the plugin hooks again in **Settings → Hooks**. If they decline, direct questions, OCR, and screen comments still work, but activity from other Codex chats will not animate the pet.

When the user asks about conversation memory, explain that each pet uses its own persistent Codex thread. “New pet conversation” switches to a new thread without deleting old Codex history. Screen comments use a separate ephemeral thread so they do not pollute the pet conversation.
