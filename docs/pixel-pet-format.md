# Pixel pet pack format

Codex Desktop Pet uses a **maximum 64×64 logical pixel canvas** for hand-authored pixel pets. The built-in droplet mascot is rendered
on that canvas and scaled with nearest-neighbour sampling. A designer may use a smaller
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
- `pixelArt` defaults to `true`; set it to `false` for smooth 192×208 ChatGPT Pets frames.
- Imported ChatGPT Pets packs use `canvasSize: 208` and `sourceFormat: chatgpt-pets-v1` or
  `chatgpt-pets-v2`. Hand-authored pixel packs should remain at 64 or below.
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

Choose **新建像素宠物** to draw a static pet on an 8×8 to 64×64 grid. The built-in editor provides
a pencil, eraser, flood fill, palette, custom hexadecimal colours, transparent background, and a
nearest-neighbour preview. It writes a valid one-frame `idle` pack directly into the local library.

This local sharing format is independent from ChatGPT Work Pets. Revocable public `sharepet_`
URLs require a hosted backend and are not simulated by the desktop application.

## Importing a ChatGPT Pets sprite sheet

Choose **导入 ChatGPT 精灵表** in the local pet library. The importer accepts the documented
transparent v1 `1536×1872` or v2 `1536×2288` layout with eight `192×208` cells per row. It verifies
required and unused cells, extracts the frames without resizing, and maps the standard rows to this
app's capturing, thinking, ready, needs-input, and error states. v2 look-direction rows are preserved
for future pointer-following interactions.

The repository does not include OpenAI's built-in pet artwork. Only import and export character art
that you have permission to use; local compatibility does not grant redistribution rights.

## Future editor format

Future editor versions can add onion skin, a timeline, thumbnails, shared bottom-centre anchors,
and animation previews without changing the current `.codexpet` archive format.

OpenAI Work Pets is proprietary, so this project does not copy its scripts or assets. Any future
interoperability exporter must remain a clean-room transform from this independent format.
