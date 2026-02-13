# Tiled Campaign Map Setup

This project now supports **Tiled JSON** as placement source-of-truth for campaign components.

## Where map files live

Place map files under the act folder, for example:

- `/Users/nckzvth/Projects/Armament/content/world/acts/act1/maps/zone.cp.tiled.json`

Then reference the file from zone content:

- `/Users/nckzvth/Projects/Armament/content/world/acts/act1/zones/zone.cp.json`

```json
{
  "id": "zone.cp",
  "key": "CP",
  "name": "Camp Perimeter",
  "encounterIds": ["enc.cp.perimeter_breach", "enc.cp.stolen_supplies", "enc.cp.seal_disturbed_plots"],
  "mapFile": "maps/zone.cp.tiled.json"
}
```

`mapFile` is relative to the act directory (`content/world/acts/<act-id>/`).

## Required Tiled object layer contract

Use an `Object Layer` (type `objectgroup`) and place objects with these `type` values:

- `encounter_anchor`
- `campaign_object`
- `campaign_hazard`
- `campaign_npc`

Required custom properties:

- `encounter_anchor`/`campaign_object`/`campaign_hazard`: `encounterId` (string)

Additional required properties by object type:

- `campaign_object`: `objectDefId` (string)
- `campaign_hazard`: `hazardId` (string)
- `campaign_npc`: `npcId` (string)

### Example object entries

```json
{
  "type": "campaign_object",
  "x": 116,
  "y": 102,
  "properties": [
    { "name": "encounterId", "type": "string", "value": "enc.cp.perimeter_breach" },
    { "name": "objectDefId", "type": "string", "value": "obj.corpse_pile" }
  ]
}
```

## Map-level properties

Set these on the Tiled map root properties:

- `worldMilliPerPixel` (float, recommended `100.0`)
- `invertY` (bool, recommended `true`)

These control conversion from Tiled coordinates to sim coordinates.

## Validation rules (fail-fast)

If a zone declares `mapFile`, validation requires:

1. map file exists and parses
2. layout encounter IDs exist and belong to the zone
3. every `campaign_object.objectDefId` exists in world objects and is listed in the encounter's `objectiveObjectIds`
4. every `campaign_hazard.hazardId` exists in world hazards and is listed in the encounter's `hazardIds`
5. for mapped encounters with objectives/hazards, each required `objectiveObjectIds`/`hazardIds` has at least one placement
6. every `campaign_npc.npcId` exists in `content/world/npcs/*.json` and belongs to that zone

## Runtime behavior

- If map placements exist for an encounter, object/hazard runtime placement uses map coordinates.
- NPC runtime placement uses `campaign_npc` map entries.
- Linked hazards on objects still activate from object definitions.
- Client receives typed replication for objects/hazards/npcs/objectives and renders them in-world.

## Authoring checklist

1. Add/refresh encounter/object/hazard content JSON first.
2. Build Tiled map with object placements for each mapped encounter.
3. Add `mapFile` in zone JSON.
4. Run:

```bash
cd /Users/nckzvth/Projects/Armament

dotnet run --project shared-sim/Tests/WorldContentValidation.Tests/Armament.WorldContentValidation.Tests.csproj
```

5. Launch playtest:

```bash
cd /Users/nckzvth/Projects/Armament && ./ops/scripts/run-dev.sh
```
