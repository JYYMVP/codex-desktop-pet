# Plugin branch

This branch experiments with packaging Codex Desktop Pet as a local Codex plugin while keeping the Windows companion as a separate process.

The plugin currently contains:

- a portable `plugin.json` manifest;
- a repo-local marketplace entry;
- an onboarding and troubleshooting skill;
- trusted lifecycle hooks that start the companion and mirror content-free Codex activity states across chats;
- deterministic install and uninstall scripts for the published Windows files;
- design contracts for local pixel pets and persistent per-pet conversation context;
- an official-Pets-style compact surface, optional screen comments, and local pet library from `main`.
- an opt-in official Pets companion mode that hides the duplicate pet window and fills selected text into the official Pets Quick Chat for confirmation.
- an explicit OCR region shortcut that freezes the desktop, crops only the dragged region, asks Codex to recognize it, and deletes the temporary captures.
- native structured settings for both global shortcuts and the helper's privacy-sensitive automatic features.
- a multi-chat activity tray, Windows voice-typing entry, animated pixel-pet editor, and revocable public sharing through user-owned GitHub Gists.
- Codex-bound lifecycle management, hidden non-interactive hooks, detailed task-phase labels, and configurable idle/reduced-motion behavior.
- a version-pinned, checksum-verified, atomic release installer on the plugin page plus a local Windows packaging and signing script.
- an official-style **Show pet** action that clears hidden state and starts the installed helper even when no global-hotkey process is running.

The plugin does not bundle a runtime-specific Windows payload. Its setup flow can accept a local published directory or download the GitHub Release matching the plugin version, verify its executable version, SHA-256 sidecar, and signature, atomically install it under `%LOCALAPPDATA%\CodexDesktopPet\app`, and keep settings and logs outside that removable application directory. Public release artifacts are built and signed explicitly on a trusted Windows machine; the project does not require CI.

The official OpenAI Pets package is not copied or bundled. Companion mode only uses the documented `Windows+Alt+P` Quick Chat shortcut and ordinary clipboard paste, while keeping the final send action under user control.

For a complete Windows installation, verification, known-hook-limitations, and removal walkthrough, see
[`docs/plugin-installation.md`](docs/plugin-installation.md).
