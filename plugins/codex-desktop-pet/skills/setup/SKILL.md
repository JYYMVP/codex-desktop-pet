---
name: setup-codex-desktop-pet
description: Set up or troubleshoot the Windows Codex Desktop Pet, including its executable, global shortcut, selection capture, pixel-pet files, and local diagnostic log. Use only for this desktop companion, not ChatGPT Work Pets or real animals.
---

# Codex Desktop Pet setup

Help the user install, start, configure, or diagnose the Windows desktop companion in this repository.

Preserve these boundaries:

- The Windows process owns the transparent window, global shortcut, selection capture, and pixel rendering.
- The plugin supplies discovery, onboarding, and a trusted session-start hook; it does not make Codex continuously observe the desktop.
- Selected text is sent to Codex only after the user presses the configured shortcut.
- Do not copy code or assets from OpenAI's proprietary Work Pets plugin. Independently implement any compatible exporter from documented file contracts.

For setup, first check Windows, the .NET 8 runtime, `codex.exe`, and the expected executable path. Explain any missing dependency before changing the system. Build or install only when the user requested setup, and verify the resulting process and log.

For troubleshooting, inspect `%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log`, then distinguish hotkey registration, selection capture, Codex connectivity, and rendering failures. Prefer the smallest repair and retest the actual shortcut path.

When the user asks about conversation memory, explain the three intended modes: isolated (default), in-memory session, and explicit persistent history. Never silently enable persistent history.
