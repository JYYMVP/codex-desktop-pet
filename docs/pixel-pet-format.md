# Pixel pet format proposal

## Goals

The editor uses one fixed maximum logical canvas while allowing characters with different visible sizes. Version 1 fixes `A = 64`, so every frame is a transparent `64 × 64` RGBA image. A character may occupy any smaller rectangle inside the frame.

This keeps animation alignment deterministic, makes nearest-neighbor scaling crisp, and lets designers create tiny or large pets without changing the renderer.

## Coordinate model

- Logical canvas: `64 × 64` pixels.
- Origin: top-left.
- Default ground anchor: `(32, 60)`.
- Visible bounds: calculated from non-transparent pixels; never used to rescale individual frames.
- Display scale: positive integer (`2×`, `3×`, `4×`, and so on) with nearest-neighbor sampling.
- Every animation for one pet shares the same scale, anchor, and baseline.

Transparent padding is intentional. A `20 × 28` pet and a `56 × 58` pet can coexist in the same format while retaining their relative visual sizes.

## Package layout

A distributable `.codexpet` file is a ZIP archive:

```text
manifest.json
sprites.png
thumbnail.png
```

`sprites.png` is a horizontal or rectangular atlas of fixed `64 × 64` cells. `manifest.json` maps animation names to cell indexes and durations.

```json
{
  "formatVersion": 1,
  "id": "sample-pet",
  "name": "Sample Pet",
  "canvas": { "width": 64, "height": 64 },
  "anchor": { "x": 32, "y": 60 },
  "animations": {
    "idle": { "frames": [0, 1, 2, 1], "frameMs": 180, "loop": true },
    "capturing": { "frames": [3, 4], "frameMs": 120, "loop": true },
    "thinking": { "frames": [5, 6, 7, 6], "frameMs": 140, "loop": true },
    "ready": { "frames": [8, 9, 10], "frameMs": 120, "loop": false },
    "error": { "frames": [11, 12], "frameMs": 220, "loop": true }
  }
}
```

Required states are `idle`, `capturing`, `thinking`, `ready`, and `error`. Optional states can include `sleep`, `walk-left`, `walk-right`, and pointer reactions.

## Designer

The editor should provide a 64 × 64 grid, palette, pencil, eraser, fill, selection, flip, onion skin, frame timeline, animation preview, and anchor/baseline overlay. It must export exactly the pixels authored by the designer and must not smooth or independently resize frames.

## Work Pets interoperability

OpenAI Work Pets is proprietary, so this repository must not copy its scripts or assets. A clean-room exporter may transform this independent format into the observable Work Pets atlas contract for interoperability:

- nearest-neighbor scale each 64 × 64 frame without redrawing it;
- place it into the target cell using a shared scale and bottom-center anchor;
- preserve transparency and leave unused cells transparent;
- map our animation states to the target states through a separate export profile.

The desktop format remains canonical even when an exporter produces another atlas.
