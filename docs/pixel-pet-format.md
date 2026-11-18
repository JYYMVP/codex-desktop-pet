# Pixel pet pack format

Codex Desktop Pet uses a **maximum 64×64 logical pixel canvas**. The built-in fish is rendered
on that canvas and scaled to 128×128 with nearest-neighbour sampling. A designer may use a smaller
canvas or leave transparent padding around a smaller character; frames are never smoothed.

Set `PetPackPath` in the settings window to either a pack directory or its `pet.json` file.

```json
{
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

## Future package/editor format

The current runtime deliberately starts with a transparent-PNG folder because it is easy to
author and inspect. A future `.codexpet` ZIP can package the same 64×64 frames, metadata,
thumbnail, shared bottom-centre anchor and animation timing into one file. The planned editor can
add a grid, palette, pencil, eraser, fill, onion skin, timeline and preview without changing the
canonical pixel canvas.

OpenAI Work Pets is proprietary, so this project does not copy its scripts or assets. Any future
interoperability exporter must remain a clean-room transform from this independent format.
