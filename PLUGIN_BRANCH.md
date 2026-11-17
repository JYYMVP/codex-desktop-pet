# Plugin branch

This branch experiments with packaging Codex Desktop Pet as a local Codex plugin while keeping the Windows companion as a separate process.

The plugin currently contains:

- a portable `plugin.json` manifest;
- a repo-local marketplace entry;
- an onboarding and troubleshooting skill;
- a trusted `SessionStart` hook that starts an already installed companion;
- design contracts for pixel pets and optional conversation context.

It does not yet bundle or install the Windows executable. The next milestone is a deterministic installer that publishes the application to `%LOCALAPPDATA%\CodexDesktopPet`, followed by local marketplace installation and hook trust testing in Codex desktop.

The official OpenAI Work Pets package is not a dependency. Its installed manifest declares a proprietary license, so this branch uses an independent implementation.
