# Agent Workflow Note (Class Development)

## Goal

Keep class work durable and reviewable without rushing a full kit drop.

## Required Sequence For Class PRs

1. Update authored content definitions first.
2. Update docs/spec sheet references to final content IDs.
3. Run validators and deterministic tests.
4. Only then adjust client presentation hooks (anim/vfx tags).

## Definition Of Done For A Spec Slice

- content exists for spec/loadout/abilities/status/signature objects
- docs reference only real IDs
- acceptance tests added/updated
- one golden loop test added/updated
- no new bespoke class logic bypassing primitive registry

## Guardrails

- No full-class rollout until AbilityRunner + primitive registry are merged.
- No new capability without:
  - primitive contract update
  - validator coverage
  - deterministic test coverage

## Drift Check

If docs and content disagree, treat as broken build.
