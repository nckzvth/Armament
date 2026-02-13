#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Armament.SharedSim.Protocol;

public static class ProtocolCodec
{
    public static byte[] Encode(IProtocolMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((byte)message.Type);

        switch (message)
        {
            case ClientHello hello:
                writer.Write(hello.ClientNonce);
                break;
            case ServerHello hello:
                writer.Write(hello.AssignedClientId);
                break;
            case JoinOverworldRequest join:
                writer.Write(join.AccountSubject);
                writer.Write(join.AccountDisplayName);
                writer.Write(join.CharacterSlot);
                writer.Write(join.CharacterName);
                writer.Write(join.BaseClassId);
                writer.Write(join.SpecId);
                break;
            case JoinOverworldAccepted accepted:
                writer.Write(accepted.EntityId);
                writer.Write(accepted.PlayerCount);
                writer.Write((byte)accepted.ZoneKind);
                writer.Write(accepted.BaseClassId);
                writer.Write(accepted.SpecId);
                break;
            case JoinDungeonRequest:
                break;
            case JoinDungeonAccepted dungeonAccepted:
                writer.Write(dungeonAccepted.EntityId);
                writer.Write(dungeonAccepted.DungeonInstanceId);
                writer.Write((byte)dungeonAccepted.ZoneKind);
                writer.Write(dungeonAccepted.BaseClassId);
                writer.Write(dungeonAccepted.SpecId);
                break;
            case DisconnectRequest:
                break;
            case InputCommand input:
                writer.Write(input.Sequence);
                writer.Write(input.ClientTick);
                writer.Write(input.MoveX);
                writer.Write(input.MoveY);
                writer.Write((uint)input.ActionFlags);
                break;
            case WorldSnapshot snapshot:
                writer.Write(snapshot.ServerTick);
                writer.Write(snapshot.LastProcessedInputSequence);
                writer.Write((byte)snapshot.ZoneKind);
                writer.Write(snapshot.InstanceId);
                writer.Write((ushort)snapshot.Entities.Count);
                foreach (var entity in snapshot.Entities)
                {
                    writer.Write(entity.EntityId);
                    writer.Write((byte)entity.Kind);
                    writer.Write(entity.QuantizedX);
                    writer.Write(entity.QuantizedY);
                    writer.Write(entity.Health);
                    writer.Write(entity.BuilderResource);
                    writer.Write(entity.SpenderResource);
                    writer.Write(entity.Currency);
                    writer.Write(entity.FastCooldownTicks);
                    writer.Write(entity.HeavyCooldownTicks);
                    writer.Write(entity.Skill1CooldownTicks);
                    writer.Write(entity.Skill2CooldownTicks);
                    writer.Write(entity.Skill3CooldownTicks);
                    writer.Write(entity.Skill4CooldownTicks);
                    writer.Write(entity.Skill5CooldownTicks);
                    writer.Write(entity.Skill6CooldownTicks);
                    writer.Write(entity.Skill7CooldownTicks);
                    writer.Write(entity.Skill8CooldownTicks);
                    writer.Write(entity.AggroTargetEntityId);
                    writer.Write(entity.AggroThreatValue);
                    writer.Write(entity.ForcedTargetTicks);
                    writer.Write(entity.DebugPrimaryStatusStacks);
                    writer.Write(entity.DebugConsumedStatusStacks);
                    writer.Write(entity.DebugLastCastSlotCode);
                    writer.Write(entity.DebugLastCastResultCode);
                    writer.Write(entity.DebugLastCastTargetTeamCode);
                    writer.Write(entity.DebugLastCastAffectedCount);
                    writer.Write(entity.DebugLastCastVfxCode);
                    writer.Write(entity.DebugLastCastFeedbackTicks);
                    writer.Write(entity.ArchetypeId);
                }
                writer.Write((ushort)snapshot.Zones.Count);
                foreach (var zone in snapshot.Zones)
                {
                    writer.Write(zone.ZoneRuntimeId);
                    writer.Write(zone.ZoneDefId);
                    writer.Write(zone.QuantizedX);
                    writer.Write(zone.QuantizedY);
                    writer.Write(zone.RemainingTicks);
                    writer.Write(zone.RadiusDeciUnits);
                }
                writer.Write((ushort)snapshot.Links.Count);
                foreach (var link in snapshot.Links)
                {
                    writer.Write(link.LinkRuntimeId);
                    writer.Write(link.LinkDefId);
                    writer.Write(link.OwnerEntityId);
                    writer.Write(link.TargetEntityId);
                    writer.Write(link.QuantizedX);
                    writer.Write(link.QuantizedY);
                    writer.Write(link.RemainingTicks);
                }
                writer.Write((ushort)snapshot.WorldObjects.Count);
                foreach (var obj in snapshot.WorldObjects)
                {
                    writer.Write(obj.ObjectId);
                    writer.Write(obj.ObjectDefId);
                    writer.Write(obj.Archetype);
                    writer.Write(obj.EncounterId);
                    writer.Write(obj.QuantizedX);
                    writer.Write(obj.QuantizedY);
                    writer.Write(obj.Health);
                    writer.Write(obj.MaxHealth);
                    writer.Write(obj.ObjectiveState);
                }
                writer.Write((ushort)snapshot.Hazards.Count);
                foreach (var hazard in snapshot.Hazards)
                {
                    writer.Write(hazard.HazardRuntimeId);
                    writer.Write(hazard.HazardId);
                    writer.Write(hazard.EncounterId);
                    writer.Write(hazard.QuantizedX);
                    writer.Write(hazard.QuantizedY);
                    writer.Write(hazard.RemainingTicks);
                    writer.Write(hazard.ObjectiveState);
                }
                writer.Write((ushort)snapshot.Npcs.Count);
                foreach (var npc in snapshot.Npcs)
                {
                    writer.Write(npc.NpcRuntimeId);
                    writer.Write(npc.NpcId);
                    writer.Write(npc.ZoneId);
                    writer.Write(npc.Name);
                    writer.Write(npc.QuantizedX);
                    writer.Write(npc.QuantizedY);
                    writer.Write(npc.InteractRadiusDeciUnits);
                    writer.Write(npc.ObjectiveState);
                }
                writer.Write((ushort)snapshot.Objectives.Count);
                foreach (var objective in snapshot.Objectives)
                {
                    writer.Write(objective.ObjectiveId);
                    writer.Write(objective.EncounterId);
                    writer.Write(objective.Kind);
                    writer.Write(objective.TargetId);
                    writer.Write(objective.Current);
                    writer.Write(objective.Required);
                    writer.Write(objective.State);
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported message type: {message.GetType().Name}");
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out IProtocolMessage? message)
    {
        message = null;
        if (payload.Length < 1)
        {
            return false;
        }

        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var type = (MessageType)reader.ReadByte();
        try
        {
            message = type switch
            {
                MessageType.ClientHello => new ClientHello { ClientNonce = reader.ReadUInt32() },
                MessageType.ServerHello => new ServerHello { AssignedClientId = reader.ReadUInt32() },
                MessageType.JoinOverworldRequest => DecodeJoinOverworldRequest(reader),
                MessageType.JoinOverworldAccepted => new JoinOverworldAccepted
                {
                    EntityId = reader.ReadUInt32(),
                    PlayerCount = reader.ReadUInt16(),
                    ZoneKind = (ZoneKind)reader.ReadByte(),
                    BaseClassId = ReadTrailingString(reader, string.Empty),
                    SpecId = ReadTrailingString(reader, string.Empty)
                },
                MessageType.JoinDungeonRequest => new JoinDungeonRequest(),
                MessageType.JoinDungeonAccepted => new JoinDungeonAccepted
                {
                    EntityId = reader.ReadUInt32(),
                    DungeonInstanceId = reader.ReadUInt32(),
                    ZoneKind = (ZoneKind)reader.ReadByte(),
                    BaseClassId = ReadTrailingString(reader, string.Empty),
                    SpecId = ReadTrailingString(reader, string.Empty)
                },
                MessageType.DisconnectRequest => new DisconnectRequest(),
                MessageType.InputCommand => new InputCommand
                {
                    Sequence = reader.ReadUInt32(),
                    ClientTick = reader.ReadUInt32(),
                    MoveX = reader.ReadInt16(),
                    MoveY = reader.ReadInt16(),
                    ActionFlags = (InputActionFlags)reader.ReadUInt32()
                },
                MessageType.WorldSnapshot => DecodeWorldSnapshot(reader),
                _ => null
            };
        }
        catch (EndOfStreamException)
        {
            message = null;
            return false;
        }

        return message is not null;
    }

    private static WorldSnapshot DecodeWorldSnapshot(BinaryReader reader)
    {
        var serverTick = reader.ReadUInt32();
        var lastProcessedInputSequence = reader.ReadUInt32();
        var zoneKind = (ZoneKind)reader.ReadByte();
        var instanceId = reader.ReadUInt32();
        var count = reader.ReadUInt16();
        var entities = new List<EntitySnapshot>(count);

        for (var i = 0; i < count; i++)
        {
                entities.Add(new EntitySnapshot
                {
                EntityId = reader.ReadUInt32(),
                Kind = (EntityKind)reader.ReadByte(),
                QuantizedX = reader.ReadInt16(),
                QuantizedY = reader.ReadInt16(),
                Health = reader.ReadUInt16(),
                BuilderResource = reader.ReadUInt16(),
                SpenderResource = reader.ReadUInt16(),
                Currency = reader.ReadUInt16(),
                FastCooldownTicks = reader.ReadUInt16(),
                HeavyCooldownTicks = reader.ReadUInt16(),
                Skill1CooldownTicks = reader.ReadUInt16(),
                Skill2CooldownTicks = reader.ReadUInt16(),
                Skill3CooldownTicks = reader.ReadUInt16(),
                Skill4CooldownTicks = reader.ReadUInt16(),
                Skill5CooldownTicks = reader.ReadUInt16(),
                Skill6CooldownTicks = reader.ReadUInt16(),
                Skill7CooldownTicks = reader.ReadUInt16(),
                Skill8CooldownTicks = reader.ReadUInt16(),
                AggroTargetEntityId = reader.ReadUInt32(),
                AggroThreatValue = reader.ReadUInt16(),
                ForcedTargetTicks = reader.ReadByte(),
                DebugPrimaryStatusStacks = reader.ReadByte(),
                DebugConsumedStatusStacks = reader.ReadByte(),
                DebugLastCastSlotCode = reader.ReadByte(),
                DebugLastCastResultCode = reader.ReadByte(),
                    DebugLastCastTargetTeamCode = reader.ReadByte(),
                    DebugLastCastAffectedCount = reader.ReadByte(),
                    DebugLastCastVfxCode = reader.ReadUInt16(),
                    DebugLastCastFeedbackTicks = reader.ReadByte(),
                    ArchetypeId = reader.ReadString()
                });
        }

        var zones = new List<WorldZoneSnapshot>();
        var links = new List<WorldLinkSnapshot>();
        var worldObjects = new List<WorldObjectSnapshot>();
        var hazards = new List<WorldHazardSnapshot>();
        var npcs = new List<WorldNpcSnapshot>();
        var objectives = new List<WorldObjectiveSnapshot>();

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var zoneCount = reader.ReadUInt16();
            zones = new List<WorldZoneSnapshot>(zoneCount);
            for (var i = 0; i < zoneCount; i++)
            {
                zones.Add(new WorldZoneSnapshot
                {
                    ZoneRuntimeId = reader.ReadUInt32(),
                    ZoneDefId = reader.ReadString(),
                    QuantizedX = reader.ReadInt16(),
                    QuantizedY = reader.ReadInt16(),
                    RemainingTicks = reader.ReadUInt16(),
                    RadiusDeciUnits = reader.ReadUInt16()
                });
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var linkCount = reader.ReadUInt16();
            links = new List<WorldLinkSnapshot>(linkCount);
            for (var i = 0; i < linkCount; i++)
            {
                links.Add(new WorldLinkSnapshot
                {
                    LinkRuntimeId = reader.ReadUInt32(),
                    LinkDefId = reader.ReadString(),
                    OwnerEntityId = reader.ReadUInt32(),
                    TargetEntityId = reader.ReadUInt32(),
                    QuantizedX = reader.ReadInt16(),
                    QuantizedY = reader.ReadInt16(),
                    RemainingTicks = reader.ReadUInt16()
                });
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var objectCount = reader.ReadUInt16();
            worldObjects = new List<WorldObjectSnapshot>(objectCount);
            for (var i = 0; i < objectCount; i++)
            {
                worldObjects.Add(new WorldObjectSnapshot
                {
                    ObjectId = reader.ReadUInt32(),
                    ObjectDefId = reader.ReadString(),
                    Archetype = reader.ReadString(),
                    EncounterId = reader.ReadString(),
                    QuantizedX = reader.ReadInt16(),
                    QuantizedY = reader.ReadInt16(),
                    Health = reader.ReadUInt16(),
                    MaxHealth = reader.ReadUInt16(),
                    ObjectiveState = reader.ReadByte()
                });
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var hazardCount = reader.ReadUInt16();
            hazards = new List<WorldHazardSnapshot>(hazardCount);
            for (var i = 0; i < hazardCount; i++)
            {
                hazards.Add(new WorldHazardSnapshot
                {
                    HazardRuntimeId = reader.ReadUInt32(),
                    HazardId = reader.ReadString(),
                    EncounterId = reader.ReadString(),
                    QuantizedX = reader.ReadInt16(),
                    QuantizedY = reader.ReadInt16(),
                    RemainingTicks = reader.ReadUInt16(),
                    ObjectiveState = reader.ReadByte()
                });
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var npcCount = reader.ReadUInt16();
            npcs = new List<WorldNpcSnapshot>(npcCount);
            for (var i = 0; i < npcCount; i++)
            {
                npcs.Add(new WorldNpcSnapshot
                {
                    NpcRuntimeId = reader.ReadUInt32(),
                    NpcId = reader.ReadString(),
                    ZoneId = reader.ReadString(),
                    Name = reader.ReadString(),
                    QuantizedX = reader.ReadInt16(),
                    QuantizedY = reader.ReadInt16(),
                    InteractRadiusDeciUnits = reader.ReadUInt16(),
                    ObjectiveState = reader.ReadByte()
                });
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var objectiveCount = reader.ReadUInt16();
            objectives = new List<WorldObjectiveSnapshot>(objectiveCount);
            for (var i = 0; i < objectiveCount; i++)
            {
                objectives.Add(new WorldObjectiveSnapshot
                {
                    ObjectiveId = reader.ReadString(),
                    EncounterId = reader.ReadString(),
                    Kind = reader.ReadString(),
                    TargetId = reader.ReadString(),
                    Current = reader.ReadUInt16(),
                    Required = reader.ReadUInt16(),
                    State = reader.ReadByte()
                });
            }
        }

        return new WorldSnapshot
        {
            ServerTick = serverTick,
            LastProcessedInputSequence = lastProcessedInputSequence,
            ZoneKind = zoneKind,
            InstanceId = instanceId,
            Entities = entities,
            Zones = zones,
            Links = links,
            WorldObjects = worldObjects,
            Hazards = hazards,
            Npcs = npcs,
            Objectives = objectives
        };
    }

    private static JoinOverworldRequest DecodeJoinOverworldRequest(BinaryReader reader)
    {
        // Backward-compatible decode:
        // v1 payloads only contained CharacterName.
        // v2 payloads contain AccountSubject/AccountDisplayName/CharacterSlot/CharacterName.
        var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remainingBytes <= 0)
        {
            return new JoinOverworldRequest();
        }

        var first = reader.ReadString();
        remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remainingBytes <= 0)
        {
            return new JoinOverworldRequest { CharacterName = first };
        }

        var second = reader.ReadString();
        var slot = reader.ReadInt32();
        var name = reader.ReadString();
        var remainingBytesV3 = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remainingBytesV3 <= 0)
        {
            return new JoinOverworldRequest
            {
                AccountSubject = first,
                AccountDisplayName = second,
                CharacterSlot = slot,
                CharacterName = name
            };
        }

        var baseClassId = reader.ReadString();
        var specId = reader.ReadString();
        return new JoinOverworldRequest
        {
            AccountSubject = first,
            AccountDisplayName = second,
            CharacterSlot = slot,
            CharacterName = name,
            BaseClassId = baseClassId,
            SpecId = specId
        };
    }

    private static string ReadTrailingString(BinaryReader reader, string fallback)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            return fallback;
        }

        return reader.ReadString();
    }
}
