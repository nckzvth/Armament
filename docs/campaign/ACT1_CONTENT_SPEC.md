# Act 1 Content Spec

## Purpose

Define the detailed authored content target for Act 1 zones, quests, encounter progression, and bestiary usage.

Execution architecture and milestone sequencing live in:

- `docs/CAMPAIGN.md`

## Act 1 Core Flow

Start safe, escalate pressure, then force objective/hazard mastery:

- CP -> BT -> GG -> IG -> MA -> CC1 -> CC2 -> CC3

## Mechanics Teaching Sequence

1. Swarms + nuisance + simple spawners (CP)
2. Movement pressure + pulls/roots + DoT/cleanse checks (BT)
3. Ranged lane discipline + bruiser pressure (GG)
4. Objective combat + support disruption (IG)
5. Hazard corridor pressure + command-window boss pacing (MA)
6. Multi-floor remix with stronger objective load (CC1-CC3)

## Main Quest Spine

1. `The Contract Board`
- speak to quartermaster
- accept Deacon contract

2. `Perimeter Breach`
- defend camp edge
- seal disturbed plots
- recover stolen supplies

3. `Through the Bloody Thicket`
- complete 2 of 3 BT objective threads
- open graveyard approach

4. `Break the Graveyard Gate`
- defeat Spectral Captain

5. `Silence the Inner Graveyard`
- disable pylons/anchors
- defeat Reliquary Shade

6. `Breach the Mausoleum`
- survive hazards
- defeat Ossuary Warden

7. `The Corrupted Cathedral`
- clear CC1/CC2/CC3
- defeat Deacon

## Zone Specs

### CP: Camp Perimeter

Purpose:

- establish base pacing and objective interception

Primary enemies:

- Grave-Scrabbler
- Pallid Shambler
- Tomb Picker
- Bolt Runner
- Corpse Pile (spawner object)

Optional teasers:

- Censer Acolyte
- Marrow Stitcher (single interrupt tutorial)

Signature encounters:

- Perimeter Breach waves
- Stolen Supplies theft event
- Seal Disturbed Plots objective

Teaches:

- spawner priority
- objective interception
- basic zone pressure awareness

### BT: Bloody Thicket

Purpose:

- roaming pressure and movement-control checks

Primary enemies:

- Briar Wolf
- Root-Snare Husk
- Mire Spitter
- Tomb Picker
- Bolt Runner
- Torch Ruffian
- Coffin Hauler
- Corpse Pile (localized)

Rare/roaming enemies:

- Skinwalker Husk
- Gravewarden Constable
- Unclean Lieutenant

Roaming mini-boss:

- Hollow Stag

Progression objective model:

- complete 2 of 3 objective threads:
- cleanse grove
- survivor escort
- thief stash recovery

Teaches:

- pull/root response
- DoT/cleanse value
- lane denial response
- roaming adaptation

### GG: Graveyard Gate

Purpose:

- enforce lane discipline and first hard gate boss

Primary enemies:

- Bone Skirmisher
- Ribcage Archer
- Ossuary Sentinel
- Coffin Hauler
- Grave-Scrabbler
- Pallid Shambler
- Choir Wisp (teaser)

Boss:

- Spectral Captain

Encounter identity:

- telegraphed volley lanes
- guard add pressure
- static-position punishment

### IG: Inner Graveyard

Purpose:

- objective combat under ritual amplification pressure

Primary mechanics:

- Bell Pylons
- Relic Anchors
- support/assassin pressure mixes

Boss/mini-boss:

- Reliquary Shade

Teaches:

- object-linked boss gating
- objective prioritization over pure damage race

### MA: Mausoleum

Purpose:

- hazard corridor timing plus boss command windows

Primary mechanics:

- Grave-Gas Vents
- corridor timing and path pressure
- interrupt/position windows

Boss:

- Ossuary Warden

### CC1-CC3: Corrupted Cathedral

Purpose:

- multi-floor dungeon escalation to Act final boss

CC1 focus:

- bell-pressure floor objectives

CC2 focus:

- stitch-node/reassembly objective pressure

CC3 focus:

- desecration + unclean pressure
- Deacon final fight

## Bestiary Usage Requirements

Each enemy entry should define:

- id
- role tags
- stat profile
- AI brain id
- optional ability profile id

Minimum deterministic brain set:

- melee chase/hit
- ranged kite/shoot
- controller pull/root
- support heal/repair
- assassin dash pressure
- boss scripted pattern

## Content Authoring Constraints

- no unique one-off enemy logic in core sim for single encounter gimmicks
- object/hazard behavior must be authored through reusable definitions
- quest gating must be server-authoritative and persisted
- in campaign mode, enemies must originate from encounter content (`encounter.enemyIds`) rather than debug spawn shortcuts
- quest objective snapshots must appear for a character immediately after join/load (no hidden-first-frame objective state)
- objective markers should be driven by typed objective/object/hazard snapshots, not ad hoc client inference

## Testing Expectations For Act 1 Content

Per zone/floor slice:

- content validation passes
- deterministic encounter/objective tests pass
- quest progression path tests pass
- transition/regression tests pass
- join-flow regression check passes:
- objective list populated on first snapshot for a newly loaded character
- no placeholder enemy appears in campaign overworld without an active encounter definition
