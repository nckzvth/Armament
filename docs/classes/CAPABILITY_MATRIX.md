# Class Capability Matrix

## Purpose

- Track capability unlock order and keep class-kit implementation tied to reusable engine primitives.
- Avoid bespoke per-class code paths.

## Capability Keys

- `Z` zones
- `P` displacement/cc
- `L` links/tethers
- `Pr` projectiles/hitscan
- `Th` threat/taunt
- `Sh` shields/damage reduction
- `Cl` cleanse
- `He` healing

## Planned Specs

| Spec Id | Role | Z | P | L | Pr | Th | Sh | Cl | He |
|---|---|---|---|---|---|---|---|---|---|
| `spec.bastion.bulwark` | Tank | Y | Y | - | - | Y | Y | - | - |
| `spec.bastion.cataclysm` | DPS | Y | Y | - | - | - | Y | - | - |
| `spec.exorcist.warden` | Tank | Y | Y | - | - | Y | Y | Y | - |
| `spec.exorcist.inquisitor` | DPS | Y | Y | - | - | - | Y | Y | - |
| `spec.tidebinder.tidecaller` | Healer | Y | Y | - | - | - | Y | Y | Y |
| `spec.tidebinder.tempest` | DPS | Y | Y | - | - | - | - | - | - |
| `spec.gunslinger.akimbo` | DPS | - | - | - | Y | - | - | - | - |
| `spec.gunslinger.deadeye` | DPS | - | - | - | Y | - | - | - | - |
| `spec.dreadweaver.menace` | Tank | Y | Y | Y | - | Y | Y | Y | - |
| `spec.dreadweaver.deceiver` | DPS | Y | Y | Y | - | - | Y | - | - |
| `spec.arbiter.aegis` | Healer | Y | Y | Y | - | - | Y | Y | Y |
| `spec.arbiter.edict` | DPS | Y | Y | Y | - | - | - | - | - |

## Unlock Order

1. `Z` and `P` baseline (zones, slow/root/push/pull, clamp rules)
2. `Th` and `Sh` (tank viability and mitigation identity)
3. `He` and `Cl` (group sustain and dispel policy)
4. `Pr` (ranged kits)
5. `L` (tethers/shared-state complexity)

## Current Repo Status

- Foundation exists for authoritative sim, persistence, and deterministic tests.
- `AbilityRunner` profile pipeline is active for authored specs.
- Implemented capabilities in runtime: `Z`, `P`, `Th`, `Sh`, `Cl`, `Pr`, baseline `L` (deterministic create/tick/break tether), and baseline `He` (authoritative heal primitive + healing zones).
- Next implementation target: expand `L` beyond baseline and deepen `He` support behavior (ally-prioritized targeting, HoT variants, stronger role telemetry).
