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
                FastCooldownTicks = reader.ReadByte(),
                HeavyCooldownTicks = reader.ReadByte(),
                Skill1CooldownTicks = reader.ReadByte(),
                Skill2CooldownTicks = reader.ReadByte(),
                Skill3CooldownTicks = reader.ReadByte(),
                Skill4CooldownTicks = reader.ReadByte(),
                Skill5CooldownTicks = reader.ReadByte(),
                Skill6CooldownTicks = reader.ReadByte(),
                Skill7CooldownTicks = reader.ReadByte(),
                Skill8CooldownTicks = reader.ReadByte(),
                AggroTargetEntityId = reader.ReadUInt32(),
                AggroThreatValue = reader.ReadUInt16(),
                ForcedTargetTicks = reader.ReadByte(),
                DebugPrimaryStatusStacks = reader.ReadByte(),
                DebugConsumedStatusStacks = reader.ReadByte(),
                DebugLastCastSlotCode = reader.ReadByte(),
                DebugLastCastResultCode = reader.ReadByte(),
                DebugLastCastTargetTeamCode = reader.ReadByte(),
                DebugLastCastAffectedCount = reader.ReadByte(),
                DebugLastCastVfxCode = reader.ReadUInt16()
            });
        }

        return new WorldSnapshot
        {
            ServerTick = serverTick,
            LastProcessedInputSequence = lastProcessedInputSequence,
            ZoneKind = zoneKind,
            InstanceId = instanceId,
            Entities = entities
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
