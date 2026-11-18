# Plugin branch

This branch experiments with packaging Codex Desktop Pet as a local Codex plugin while keeping the Windows companion as a separate process.

The plugin currently contains:

- a portable `plugin.json` manifest;
- a repo-local marketplace entry;
- an onboarding and troubleshooting skill;
- a trusted `SessionStart` hook that starts an already installed companion;
- deterministic install and uninstall scripts for the published Windows files;
- design contracts for pixel pets and optional conversation context.

The plugin does not bundle a runtime-specific Windows payload. Its setup flow accepts a published directory, installs it under `%LOCALAPPDATA%\CodexDesktopPet\app`, and keeps settings and logs outside that removable application directory. The next packaging milestone is attaching a signed release payload so end users do not need a local .NET build.

The official OpenAI Work Pets package is not a dependency. Its installed manifest declares a proprietary license, so this branch uses an independent implementation.
