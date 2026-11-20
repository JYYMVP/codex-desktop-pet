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

The plugin exposes structured settings through its local `desktop-pet-settings` MCP server. Prefer **Show pet** on the plugin details page when the helper is stopped or the pet was hidden: it clears `PetHidden`, starts the installed helper, and makes the pet visible. The same page also provides native controls for Codex activity and pointer following, the show/hide shortcut, official Pets mode, selection and OCR shortcuts, periodic and window-change comments, timing, always-on-top, and Fast mode. These controls persist `%LOCALAPPDATA%/CodexDesktopPet/settings.json` and automatically restart the installed helper after a successful update. Use **Restart desktop pet** only as a recovery action when settings were changed outside the plugin page.

Use **Check activity connection** on that same page when activity animation does not follow Codex. It reads `%LOCALAPPDATA%/CodexDesktopPet/hook-health.json`, which contains only the last event name, mapped state, a session hash, and timestamp. If there is no event, tell the user to review and trust the current plugin definition in **Settings → Hooks**, then start a new turn. Never claim that installing or enabling the plugin automatically trusts a changed hook hash.

`BindLifetimeToCodex` is enabled by default. Session start and user-prompt hooks ensure the helper is running, so after the user chooses Exit it returns on the next Codex prompt. The helper watches only the trusted `WindowsApps\\OpenAI.Codex_*\\app\\ChatGPT.exe` desktop host and exits after that host remains absent through a short debounce. It deliberately ignores Codex CLI and app-server child processes. Changing these hooks requires the user to review and trust the new hook hash again.

`IdleActivityLevel` accepts 0 (quiet), 1 (standard), or 2 (lively) and controls occasional wave or hop gestures. `ReduceMotion` disables floating, thinking pulses, hover hops, and random idle gestures. Prefer these plugin-page controls instead of asking the user to edit JSON.

For setup, first check Windows, the .NET 8 runtime, `codex.exe`, and the expected executable path. Explain any missing dependency before changing the system. Build or install only when the user requested setup. After publishing the Windows project, run `${PLUGIN_ROOT}/scripts/install-pet.ps1 -SourceDirectory <publish-directory>` and verify `%LOCALAPPDATA%/CodexDesktopPet/app/CodexDesktopPet.exe`, the process, and the log. Use `uninstall-pet.ps1` when the user asks to remove the installed application files; it preserves settings and logs.

For troubleshooting, inspect `%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log`, then distinguish hotkey registration, selection capture, Codex connectivity, and rendering failures. Prefer the smallest repair and retest the actual shortcut path.

The plugin's structured settings include `HotKey` for selected text, `OcrHotKey` for region recognition, `TogglePetHotKey` for showing or hiding the standalone pet, and `PetHidden` for its persisted visibility. The three shortcuts must be valid, distinct Windows modifier combinations. Their defaults are `Ctrl+Alt+Q`, `Ctrl+Alt+R`, and `Ctrl+Alt+H`.

The local pet library can clean-room import an existing ChatGPT Pets v1 or v2 sprite sheet from PNG or a Windows-decodable WebP. Validate its documented dimensions, required cells, transparent unused cells, and alpha background before extracting frames. Never download or redistribute built-in artwork on the user's behalf unless the user has separately established permission; importing a local file does not grant redistribution rights.

The pixel editor supports independent animation timelines for every documented action, frame duplication and reordering, per-frame previews, onion skinning, loop preview, and a shared frame duration. Keep at least one non-empty Idle frame. Public sharing is opt-in and uses a user-owned public GitHub Gist; show the audience and redistribution confirmation before creation, and require a second explicit confirmation before deleting the Gist to revoke the link. Never publish a pet merely because the user opened the library.

The system tray shows a privacy-preserving activity list for concurrent Codex chats using only session hashes and state metadata. **Voice input (Win+H)** focuses the pet input and invokes Windows voice typing; it never sends the resulting text automatically.

Use **Install / update desktop pet** on the plugin page to download the latest GitHub Release ZIP, verify its SHA-256 sidecar, install it under `%LOCALAPPDATA%/CodexDesktopPet/app`, and start it. A public tagged release must have a valid Windows Authenticode signature; local source builds may be unsigned.

`FollowCodexActivity` enables official-style activity mirroring for the standalone pet. Updating these hooks changes their trust hash, so tell the user to review and trust the plugin hooks again in **Settings → Hooks**. If they decline, direct questions, OCR, and screen comments still work, but activity from other Codex chats will not animate the pet.

The standalone pet can be dragged to the top and other edges of the Windows virtual desktop while keeping the pet and action dock recoverable. Quick input uses a separate popup anchored above the pet; Windows flips it below near the top edge. If the user reports that input covers the pet, verify they are running version 0.10.1 or later before changing layout offsets.

When the user asks about conversation memory, explain that each pet uses its own persistent Codex thread. “New pet conversation” switches to a new thread without deleting old Codex history. Screen comments use a separate ephemeral thread so they do not pollute the pet conversation.
