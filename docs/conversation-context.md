# Conversation context modes

The application now has two explicit conversation owners:

1. **Standalone pet** — each local pet uses its own persistent Codex app-server thread. **New pet conversation** switches that pet to a fresh thread without deleting the old Codex history. Screen comments use a separate ephemeral thread so they do not pollute the pet conversation.
2. **Official Pets companion** — by default, the helper answers selected text in its own persistent `official-pets:selection` Codex thread and shows the result in a helper bubble beside official Pets. When direct answers are disabled, it instead fills a bounded prompt into official Pets Quick Chat without pressing Enter; the official client then owns that submitted conversation and its retention behavior.

Closing the standalone answer bubble changes only visibility. It does not clear the current pet thread. Companion mode does not add a second local transcript.
