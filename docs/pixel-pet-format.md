# Pixel pet pack format

Codex Desktop Pet uses a **maximum 64×64 logical pixel canvas**. The built-in sprout mascot is rendered
on that canvas and scaled to 128×128 with nearest-neighbour sampling. A designer may use a smaller
canvas or leave transparent padding around a smaller character; frames are never smoothed.

Set `PetPackPath` in the settings window to either a pack directory or its `pet.json` file.

```json
{
  "id": "my-pixel-pet",
  "name": "My Pixel Pet",
  "description": "A tiny transparent pixel companion",
  "canvasSize": 64,
  "frameDurationMs": 360,
  "states": {
    "idle": ["idle-1.png", "idle-2.png"],
    "capturing": ["capturing.png"],
    "thinking": ["thinking-1.png", "thinking-2.png"],
    "ready": ["ready.png"],
    "needsInput": ["needs-input.png"],
    "error": ["error.png"]
  }
}
```

Rules:

- `id` is the stable local identity. Use ASCII letters, numbers, `-`, or `_`; importing a pack
  whose ID already exists is rejected instead of silently overwriting it.
- `name` and `description` are shown in the local pet library and can be edited later.
- `canvasSize` must be between 1 and 64.
- Every frame must be a transparent PNG no larger than 64×64 pixels.
- `idle` is required. Other states are optional and fall back to `idle`.
- Frame paths are relative to the pack directory and may not escape it.
- `frameDurationMs` is clamped to 80–2000 ms.
- State names are case-insensitive: `idle`, `capturing`, `thinking`, `ready`, `needsInput`, and
  `error`.

For a visually smaller pet, keep the frame at 64×64 and draw the character inside a smaller
transparent bounding box. This keeps animation alignment stable while still letting different
designers choose different apparent character sizes.

## Local library and sharing

Open the pet's context menu and choose **宠物库…**. The library can validate, select, rename,
export, and permanently delete local pets. To adopt a pet, choose its `pet.json` or a `.codexpet`
archive. A `.codexpet` file is a ZIP-compatible snapshot containing exactly one manifest and its
frames; it contains no owner identity or account credentials.

This local sharing format is independent from ChatGPT Work Pets. Revocable public `sharepet_`
URLs require a hosted backend and are not simulated by the desktop application.
