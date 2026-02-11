# Spec Template

Spec: `spec.<class>.<spec>` (Role)
Fantasy in one line: `<short identity sentence>`

## Core Loop (10-12s)

1. `<step>`
2. `<step>`
3. `<step>`
4. `<step>`

## Resource

- id: `resource.<class>.<spec>.<name>`
- cap: `<value>`
- gain triggers: `<list>`
- spend triggers: `<list>`
- regen/decay rules: `<list>`

## Canonical Status

- id: `status.<class>.<spec>.<name>`
- stack behavior: `<cap/rules>`
- apply sources: `<abilities>`
- payoff condition: `<for example stacks==3>`

## Signature Objects

- zones: `<ids and caps>`
- links: `<ids and caps>`
- projectiles/traces: `<ids>`

## Slot Loadout

- `lmb`: `ability.<...>`
- `rmb`: `ability.<...>`
- `shift`: engine block + optional modifiers
- `e`: `ability.<...>`
- `r`: `ability.<...>`
- `q`: `ability.<...>`
- `t`: `ability.<...>`
- `1`: `ability.<...>`
- `2`: `ability.<...>`
- `3`: `ability.<...>`
- `4`: `ability.<...>`

## Ability Definitions

For each active slot ability include:

- id
- input behavior (`tap|hold_repeat|hold_release_charge`)
- cooldown
- resource costs
- targeting
- phase timings (`windup_ms`, `active_ms`, `recovery_ms`)
- ordered effect primitives
- interactions
- tuning knobs

## Capability Flags

- `Z`: `yes/no`
- `P`: `yes/no`
- `L`: `yes/no`
- `Pr`: `yes/no`
- `Th`: `yes/no`
- `Sh`: `yes/no`
- `Cl`: `yes/no`
- `He`: `yes/no`

## Acceptance Tests

1. `<deterministic behavior test>`
2. `<deterministic behavior test>`
3. `<deterministic behavior test>`

## Golden Loop Test

- scripted input sequence (10-12s)
- assert:
  - resource deltas
  - canonical payoff achieved
  - signature object lifecycle observed
  - role expectation met
