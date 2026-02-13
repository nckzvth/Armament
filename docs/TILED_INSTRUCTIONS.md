# Tiled Integration Guide

## Purpose

Define the canonical setup for using Tiled JSON maps as campaign placement input for encounters, world objects, and hazards.

This guide aligns with implementation in:

- `server-dotnet/Src/ServerHost/TiledCampaignMapLoader.cs`
- `server-dotnet/Src/ServerHost/WorldContentLoader.cs`
- `server-dotnet/Src/GameServer/OverworldZone.cs`

## Where Map Files Live

Per-zone Tiled maps should live under the act folder:

- `content/world/acts/<act-id>/maps/<zone-id>.tiled.json`

Current Act 1 pre-Cathedral coverage:

- `content/world/acts/act1/maps/zone.cp.tiled.json`
- `content/world/acts/act1/maps/zone.bt.tiled.json`
- `content/world/acts/act1/maps/zone.gg.tiled.json`
- `content/world/acts/act1/maps/zone.ig.tiled.json`
- `content/world/acts/act1/maps/zone.ma.tiled.json`

Each zone that uses a map must declare `mapFile`:

```json
{
  "id": "zone.cp",
  "key": "CP",
  "name": "Camp Perimeter",
  "encounterIds": ["enc.cp.perimeter_breach", "enc.cp.stolen_supplies", "enc.cp.seal_disturbed_plots"],
  "mapFile": "maps/zone.cp.tiled.json"
}
```

`mapFile` is resolved relative to `content/world/acts/<act-id>/`.

## Required Tiled Contract

Use an `Object Layer` (`type = objectgroup`) for campaign runtime placements.

Supported object `type` values:

- `encounter_anchor`
- `campaign_object`
- `campaign_hazard`
- `campaign_npc`

Required custom properties:

- `encounter_anchor`/`campaign_object`/`campaign_hazard`:
  - `encounterId` (string)
- `campaign_object`:
  - `objectDefId` (string)
- `campaign_hazard`:
  - `hazardId` (string)
- `campaign_npc`:
  - `npcId` (string)

## Map-Level Properties

Add these map root properties:

- `worldMilliPerPixel` (float, recommended `100.0`)
- `invertY` (bool, recommended `true`)

These are used to convert Tiled coordinates into simulation world coordinates.

## Validation Rules

When a zone declares `mapFile`, validation requires:

1. map file exists and parses
2. layout encounter IDs exist and belong to the zone
3. each `campaign_object.objectDefId` exists in world object content and is listed in encounter `objectiveObjectIds`
4. each `campaign_hazard.hazardId` exists in world hazard content and is listed in encounter `hazardIds`
5. each mapped encounter with objective/hazard requirements has at least one placement for every required object/hazard id
6. each `campaign_npc.npcId` exists in world NPC content and belongs to that map zone

Validation entrypoint:

```bash
cd /Users/nckzvth/Projects/Armament

dotnet run --project shared-sim/Tests/WorldContentValidation.Tests/Armament.WorldContentValidation.Tests.csproj
```

## Runtime Behavior

- If map placements are authored for an encounter, runtime uses those positions.
- NPC placements authored as `campaign_npc` are spawned directly from map coordinates.
- If map placements are missing for a specific encounter, deterministic fallback placement is used.
- Linked hazards from object definitions still activate at object positions.
- Typed replication exposes object/hazard/npc/objective state to the client for rendering and quest log tracking.
- Campaign enemy spawning uses encounter definitions (`encounter.enemyIds`) and active quest gating.
- Campaign mode does not rely on placeholder/debug overworld enemy spawns.

## Authoring Workflow

1. Author/update encounter/object/hazard content ids first.
2. Place campaign objects/hazards in Tiled using matching ids.
3. Set zone `mapFile`.
4. Run world-content validation.
5. Run local playtest:

```bash
cd /Users/nckzvth/Projects/Armament && ./ops/scripts/run-dev.sh
```
