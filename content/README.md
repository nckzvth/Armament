# Class Content Pack

This folder contains authoritative class content definitions consumed by server/shared-sim validation.

Layout:

- `specs/`
- `abilities/`
- `status/`
- `zones/`
- `links/`
- `projectiles/`
- `traces/`

Contract source:
- `/Users/nckzvth/Projects/Armament/docs/classes/CLASS_SYSTEM_CONTRACT.md`

Current status:
- `spec.bastion.bulwark` is authored and playable
- `spec.exorcist.warden` is authored and playable
- `spec.gunslinger.deadeye` is authored and playable
- `spec.dreadweaver.menace` is authored and playable (link/tether runtime)
- `spec.tidebinder.tidecaller` is authored and playable (healing primitive + healing zones)
- `spec.tidebinder.tempest` is authored and playable (damage-focused soaked + vortex/maelstrom zones)
- content validation + profile compilation checks run in `./ops/scripts/verify.sh`
