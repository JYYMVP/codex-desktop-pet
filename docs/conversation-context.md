# Conversation context modes

The application now has two explicit conversation owners:

1. **Standalone pet** — each local pet uses its own persistent Codex app-server thread. **New pet conversation** switches that pet to a fresh thread without deleting the old Codex history. Screen comments use a separate ephemeral thread so they do not pollute the pet conversation.
2. **Official Pets companion** — the helper captures selected text and fills a bounded prompt into the official Pets Quick Chat. The helper does not press Enter and does not own or persist the resulting ChatGPT conversation. The user reviews and submits the prompt, and the official client owns its history and retention behavior.

Closing the standalone answer bubble changes only visibility. It does not clear the current pet thread. Companion mode does not add a second local transcript.
