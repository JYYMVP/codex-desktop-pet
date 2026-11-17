# Conversation context modes

The current application invokes `codex exec --ephemeral` for every selection. Each answer is isolated and receives no previous questions or answers.

The planned context model has three explicit modes:

1. `isolated` — current default. Send only the selected text and the assistant instructions.
2. `session` — keep a bounded list of recent turns in memory until the process exits or the user clicks **Clear context**.
3. `persistent` — store an encrypted or user-readable local history only after explicit opt-in, with visible retention and deletion controls.

`session` should be the next implementation target. It offers follow-up questions without silently creating a long-lived record. The request builder should include only a bounded number of recent turns and enforce a character/token budget before invoking Codex.

The pet menu should always show the active mode and provide **Clear context**. Closing an answer bubble must not clear conversation state; it only changes visibility.
