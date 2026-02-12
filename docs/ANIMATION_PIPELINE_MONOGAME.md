# MonoGame Animation Pipeline

## Goal

Replace debug primitive actor rendering with data-driven atlas animation playback in `client-mg` without changing server/shared-sim contracts.

## Asset Root

Use this root for runtime animation assets:

- `/Users/nckzvth/Projects/Armament/content/animations`

Per class layout:

- `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/locomotion/*.png`
- `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/combat/*.png`
- `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/**/*.json` (frame metadata)

## Runtime Contract (MonoGame)

- No runtime sprite creation per frame.
- Load atlas texture once; draw with source rectangles.
- Direction selection uses 8-way facing from movement/aim.
- LMB can chain clips; RMB/skills map to discrete clips from cast slot codes.
- If class/spec clip is missing, fallback to debug rectangle and log once.

## Next Implementation Milestones

1. Add `AtlasAnimationLibrary` loader in `client-mg` from JSON + PNG.
2. Add `AnimationStateResolver` that maps gameplay state to clip ids.
3. Add `AnimatedEntityRenderer` for local and remote player entities.
4. Add asset validation step in `verify` for frame bounds sanity.
5. Remove debug rectangles for player entities after class pipeline is live.
