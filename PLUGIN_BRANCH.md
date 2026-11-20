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
- a checksum-verified latest-release installer on the plugin page plus a Windows installer/release workflow with mandatory signing for tagged releases.

The plugin does not bundle a runtime-specific Windows payload. Its setup flow can accept a local published directory or download the latest GitHub Release, verify its SHA-256 sidecar, install it under `%LOCALAPPDATA%\CodexDesktopPet\app`, and keep settings and logs outside that removable application directory. Tagged releases are deliberately blocked unless the repository is configured with a Windows signing certificate; this prevents an unsigned build from being presented as a trusted public installer.

The official OpenAI Pets package is not copied or bundled. Companion mode only uses the documented `Windows+Alt+P` Quick Chat shortcut and ordinary clipboard paste, while keeping the final send action under user control.

For a complete Windows installation, verification, known-hook-limitations, and removal walkthrough, see
[`docs/plugin-installation.md`](docs/plugin-installation.md).
