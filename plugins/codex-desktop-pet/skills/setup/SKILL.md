---
name: setup-codex-desktop-pet
description: Set up or troubleshoot the Windows Codex Desktop Pet, including its executable, global shortcut, selection capture, pixel-pet files, and local diagnostic log. Use only for this desktop companion, not ChatGPT Work Pets or real animals.
---

# Codex Desktop Pet setup

Help the user install, start, configure, or diagnose the Windows desktop companion in this repository.

Preserve these boundaries:

- The Windows process owns the transparent window, global shortcut, selection capture, and pixel rendering.
- The plugin supplies discovery, onboarding, and a trusted session-start hook. Screen watching belongs to the Windows process, is off by default, and must be enabled by the user.
- Selected text is sent to Codex only after the user presses the configured shortcut.
- Do not copy code or assets from OpenAI's proprietary Work Pets plugin. Independently implement any compatible exporter from documented file contracts.

For setup, first check Windows, the .NET 8 runtime, `codex.exe`, and the expected executable path. Explain any missing dependency before changing the system. Build or install only when the user requested setup. After publishing the Windows project, run `${PLUGIN_ROOT}/scripts/install-pet.ps1 -SourceDirectory <publish-directory>` and verify `%LOCALAPPDATA%/CodexDesktopPet/app/CodexDesktopPet.exe`, the process, and the log. Use `uninstall-pet.ps1` when the user asks to remove the installed application files; it preserves settings and logs.

For troubleshooting, inspect `%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log`, then distinguish hotkey registration, selection capture, Codex connectivity, and rendering failures. Prefer the smallest repair and retest the actual shortcut path.

When the user asks about conversation memory, explain that each pet uses its own persistent Codex thread. “New pet conversation” switches to a new thread without deleting old Codex history. Screen comments use a separate ephemeral thread so they do not pollute the pet conversation.
