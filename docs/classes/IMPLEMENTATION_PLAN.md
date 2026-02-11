# Class System Implementation Plan (Pipeline First)

## Why This Plan

Current project state is ideal for class work kickoff, but only if we first build reusable class infrastructure.

## Stage 0: Documentation And Contracts (current)

- Add contract docs (`CLASS_SYSTEM_CONTRACT.md`, matrix, templates).
- Set PR/issue workflow guardrails.
- Freeze initial vocabulary and slot contract.

Exit criteria:
- docs merged and referenced by root docs
- spec backlog visible in repository

## Stage 1: Schema + Validation

- Add content directories for specs/abilities/statuses/zones/links/projectiles.
- Define schema v1 (ability/status/zone/link/resource/spec loadout).
- Add validator tests:
  - schema validity
  - reference integrity
  - slot coverage
  - unsupported primitive detection

Exit criteria:
- validation runs in `verify` and CI
- invalid class content fails build

## Stage 2: AbilityRunner Parity Migration

- Implement deterministic `AbilityRunner` in shared sim.
- Migrate current hardcoded builder/spender/skills into ability definitions with parity behavior.
- Keep existing gameplay behavior stable while routing through runner.

Exit criteria:
- no gameplay regression in existing tests
- deterministic tests still pass

## Stage 3: First Anchor Spec

- Implement `spec.bastion.bulwark` as first full authored kit.
- Add acceptance tests + one golden loop test.

Exit criteria:
- one full spec playable with authoritative behavior and tests

## Stage 4: Capability Expansion

- Expand primitives by capability order from matrix.
- Add additional specs incrementally.

Exit criteria:
- each added capability has tests + at least one shipped spec dependency

## Non-Negotiables

- server authoritative outcomes only
- deterministic simulation path only
- no bypass of schema/validator flow for “quick kit hacks”

## Character Creation And Spec Selection Context

- Player flow target:
  1. `New Character`
  2. Choose base class: `Bastion | Exorcist | Tidebinder | Gunslinger | Dreadweaver | Arbiter`
  3. Choose a valid spec for that class
- Specs are constrained by class and must not be selectable cross-class.
- Persistence target:
  - character stores base class + selected spec
  - server resolves ability profile from character class/spec on join
  - client UI only presents server-valid choices
- Current stage status:
  - runtime profile compile pipeline exists
  - character-bound class/spec selection wiring is not yet implemented
