# Designer Handoff

## Ownership

Design and visual work lives under `Assets/Game/Art` and in an `ArtSandbox` scene. Runtime prefabs and generated scenes are integrated by the developer who owns that system; this prevents Unity scene and prefab merge conflicts.

## Week-one deliverables

- Final palette for Red team, Blue team, neutral UI, warning and success states.
- Six-to-eight-room floor plan with solver/builder staging areas.
- Clearly marked briefcase and trap placement sockets.
- HUD wireframes at 1280×720 and 1920×1080 for all four webcam-safe corners.
- Product logo, Operator/Support icons and first role cards.

## Asset rules

- Unity scale: one unit equals one metre.
- URP Lit materials; opaque textures should normally stay at or below 2048×2048.
- Prefixes: `ENV_`, `PROP_`, `UI_`, `SFX_`, `MAT_`, `TEX_`.
- Prefab pivots must support snap placement. Briefcases use their bottom-centre pivot.
- Every trap must communicate warning, active and cooldown states without relying only on colour.
- Real/fake electronics remain abstract and must not reproduce instructions for real explosives.

## Functional constraints

- Fake components may resemble the real case from a distance but require at least one attentive counter-clue.
- The controlled door must show a one-second warning and may never hide its closing state.
- Room names must be legible in a 720p stream.
- Reveal must compare real/fake contents and explain why the round ended.

The prototype generator is safe to rerun for code-owned generated content, but designer-authored scenes and art must not be placed under `Assets/Game/Generated`.
