# Class System Contract (v1)

## Purpose

- Define the stable interface between input, authored class content, authoritative simulation, replication, and presentation hooks.
- Keep class implementation aligned with current architecture (authoritative server, deterministic shared sim, persistence discipline).
- Prevent spec drift by making class work pass schema/validation/tests before merge.

## Scope And Non-Goals

- In scope now: contract, schema planning, effect vocabulary, validation gates, and one anchor spec implementation path.
- Out of scope now: shipping all class kits at once.
- Rule: pipeline first, kits second.

## Input Slot Contract

Combat slots:
- `lmb` builder (tap/hold repeat)
- `rmb` spender/heavy (tap, hold repeat, or hold-release charge)
- `shift` block/guard state
- `e`, `r`, `q`, `t`, `1`, `2`, `3`, `4` ability slots

Non-combat reserved:
- `z` loot
- `f` interact
- `h` return

## Determinism Rules

- Sim is tick-based and authoritative on server.
- Content authoring uses milliseconds; loader converts to ticks with one rule:
  - `duration_ticks = ceil(duration_ms / tick_ms)`
- No nondeterministic APIs in simulation paths.
- Ability/effect execution order is deterministic and explicit.

## Ability Execution Model

Each ability uses a phase state machine:
- `windup_ms`
- `active_ms`
- `recovery_ms`

Standard triggers:
- `on_cast_start`
- `on_windup_end`
- `on_active_tick` (optional)
- `on_active_end`
- `on_cast_end`

Input behavior per ability:
- `tap`
- `hold_repeat`
- `hold_release_charge`

## Targeting Vocabulary

- `self`
- `unit`
- `point`
- `aim`
- `cone`
- `line`
- `circle`
- `nearest_in_range`

Required fields:
- `range_m`
- `team_filter` (`enemy|ally|any`)
- `max_targets`
- shape params where applicable

## Effect Primitive Vocabulary (v1)

Core:
- `StartCooldown`
- `RequireResource`
- `SpendResource`
- `GainResource`
- `RequireStatus`
- `ApplyStatus`
- `ConsumeStatus`
- `Cleanse`

Combat:
- `DealDamage`
- `Heal`
- `ApplyShield`
- `ApplyDR`

Control/threat:
- `AddThreat`
- `Taunt`
- `ApplyCC`
- `ApplyDisplacement` (`push|pull|knockback`)

Objects:
- `SpawnZone`
- `DespawnZone`
- `CreateLink`
- `BreakLink`

Ranged:
- `FireProjectile`
- `HitscanTrace`

Rule:
- No class-specific hardcoded combat branches in sim.
- New mechanics are added as reusable primitives or primitive extensions.

## Status And Cleanse Policy

Status fields:
- `status_id`
- `stack_cap`
- `stack_add_rule`
- `refresh_rule`
- `default_duration_ms`
- `dispel_tags`

Cleanse constraints:
- Cleanse only removes matching `dispel_tags`.
- Never removes reserved tags:
  - `boss_mechanic`
  - `unclean_core`
  - `immune`

## Content ID Rules

- lowercase snake_case
- stable prefixes:
  - `spec.<class>.<spec>`
  - `ability.<class>.<spec>.<slot>_<name>`
  - `status.<class>.<spec>.<name>`
  - `zone.<class>.<spec>.<name>`
  - `link.<class>.<spec>.<name>`
  - `projectile.<class>.<spec>.<name>`
  - `resource.<class>.<spec>.<name>`

## Replication Contract (Minimum)

Server-auth events/state needed by client presentation:
- `cast_start`
- `cast_resolve`
- `cast_fail`
- `resource_delta`
- `cooldown_started`
- `status_applied`
- `status_removed`
- `zone_spawned`
- `zone_despawned`
- `link_created`
- `link_broken`
- `hit_confirm`

## Testing Contract

Per spec:
- 3-6 deterministic acceptance tests
- 1 golden 10-12 second loop test

Validation must fail CI for:
- invalid schema
- dangling references
- slot mapping gaps
- unsupported primitives
- missing required targeting fields

## Phase Gates

Gate A: Contract and validation
- Contract approved
- JSON schema draft exists
- validation test target wired

Gate B: Ability runner migration
- Current combat behavior runs through AbilityRunner with parity
- No gameplay regressions on verify contract

Gate C: First anchor kit
- One spec fully authored and tested end-to-end

Gate D: Capability expansion
- Add primitives by capability roadmap only
- Add kits incrementally with tests
