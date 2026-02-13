using System;
using System.Collections.Generic;

namespace Armament.SharedSim.Protocol;

public enum MessageType : byte
{
    ClientHello = 1,
    ServerHello = 2,
    JoinOverworldRequest = 3,
    JoinOverworldAccepted = 4,
    InputCommand = 5,
    WorldSnapshot = 6,
    JoinDungeonRequest = 7,
    JoinDungeonAccepted = 8,
    DisconnectRequest = 9
}

[Flags]
public enum InputActionFlags : uint
{
    None = 0,
    FastAttackHold = 1 << 0,
    HeavyAttackHold = 1 << 1,
    BlockHold = 1 << 2,
    Skill1 = 1 << 3,
    Skill2 = 1 << 4,
    Skill3 = 1 << 5,
    Skill4 = 1 << 6,
    Skill5 = 1 << 7,
    Skill6 = 1 << 8,
    Skill7 = 1 << 9,
    Skill8 = 1 << 10,
    Pickup = 1 << 11,
    InteractPortal = 1 << 12,
    Interact = 1 << 13
}

public enum EntityKind : byte
{
    Player = 1,
    Enemy = 2,
    Loot = 3,
    Zone = 4,
    Link = 5
}

public enum ZoneKind : byte
{
    Overworld = 1,
    Dungeon = 2
}

public interface IProtocolMessage
{
    MessageType Type { get; }
}

public sealed class ClientHello : IProtocolMessage
{
    public MessageType Type => MessageType.ClientHello;
    public uint ClientNonce { get; set; }
}

public sealed class ServerHello : IProtocolMessage
{
    public MessageType Type => MessageType.ServerHello;
    public uint AssignedClientId { get; set; }
}

public sealed class JoinOverworldRequest : IProtocolMessage
{
    public MessageType Type => MessageType.JoinOverworldRequest;
    public string AccountSubject { get; set; } = "local:guest";
    public string AccountDisplayName { get; set; } = "Guest";
    public int CharacterSlot { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string BaseClassId { get; set; } = "bastion";
    public string SpecId { get; set; } = "spec.bastion.bulwark";
}

public sealed class JoinOverworldAccepted : IProtocolMessage
{
    public MessageType Type => MessageType.JoinOverworldAccepted;
    public uint EntityId { get; set; }
    public ushort PlayerCount { get; set; }
    public ZoneKind ZoneKind { get; set; } = ZoneKind.Overworld;
    public string BaseClassId { get; set; } = "bastion";
    public string SpecId { get; set; } = "spec.bastion.bulwark";
}

public sealed class JoinDungeonRequest : IProtocolMessage
{
    public MessageType Type => MessageType.JoinDungeonRequest;
}

public sealed class JoinDungeonAccepted : IProtocolMessage
{
    public MessageType Type => MessageType.JoinDungeonAccepted;
    public uint EntityId { get; set; }
    public uint DungeonInstanceId { get; set; }
    public ZoneKind ZoneKind { get; set; } = ZoneKind.Dungeon;
    public string BaseClassId { get; set; } = "bastion";
    public string SpecId { get; set; } = "spec.bastion.bulwark";
}

public sealed class DisconnectRequest : IProtocolMessage
{
    public MessageType Type => MessageType.DisconnectRequest;
}

public sealed class InputCommand : IProtocolMessage
{
    public MessageType Type => MessageType.InputCommand;
    public uint Sequence { get; set; }
    public uint ClientTick { get; set; }
    public short MoveX { get; set; }
    public short MoveY { get; set; }
    public InputActionFlags ActionFlags { get; set; }
}

public sealed class WorldSnapshot : IProtocolMessage
{
    public MessageType Type => MessageType.WorldSnapshot;
    public uint ServerTick { get; set; }
    public uint LastProcessedInputSequence { get; set; }
    public ZoneKind ZoneKind { get; set; } = ZoneKind.Overworld;
    public uint InstanceId { get; set; }
    public List<EntitySnapshot> Entities { get; set; } = new();
    public List<WorldZoneSnapshot> Zones { get; set; } = new();
    public List<WorldLinkSnapshot> Links { get; set; } = new();
    public List<WorldObjectSnapshot> WorldObjects { get; set; } = new();
    public List<WorldHazardSnapshot> Hazards { get; set; } = new();
    public List<WorldNpcSnapshot> Npcs { get; set; } = new();
    public List<WorldObjectiveSnapshot> Objectives { get; set; } = new();
}

public sealed class EntitySnapshot
{
    public uint EntityId { get; set; }
    public EntityKind Kind { get; set; }
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort Health { get; set; }
    public ushort BuilderResource { get; set; }
    public ushort SpenderResource { get; set; }
    public ushort Currency { get; set; }
    public ushort FastCooldownTicks { get; set; }
    public ushort HeavyCooldownTicks { get; set; }
    public ushort Skill1CooldownTicks { get; set; }
    public ushort Skill2CooldownTicks { get; set; }
    public ushort Skill3CooldownTicks { get; set; }
    public ushort Skill4CooldownTicks { get; set; }
    public ushort Skill5CooldownTicks { get; set; }
    public ushort Skill6CooldownTicks { get; set; }
    public ushort Skill7CooldownTicks { get; set; }
    public ushort Skill8CooldownTicks { get; set; }
    public uint AggroTargetEntityId { get; set; }
    public ushort AggroThreatValue { get; set; }
    public byte ForcedTargetTicks { get; set; }
    public byte DebugPrimaryStatusStacks { get; set; }
    public byte DebugConsumedStatusStacks { get; set; }
    public byte DebugLastCastSlotCode { get; set; }
    public byte DebugLastCastResultCode { get; set; }
    public byte DebugLastCastTargetTeamCode { get; set; }
    public byte DebugLastCastAffectedCount { get; set; }
    public ushort DebugLastCastVfxCode { get; set; }
    public byte DebugLastCastFeedbackTicks { get; set; }
    public string ArchetypeId { get; set; } = string.Empty;
}

public sealed class WorldZoneSnapshot
{
    public uint ZoneRuntimeId { get; set; }
    public string ZoneDefId { get; set; } = string.Empty;
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort RemainingTicks { get; set; }
    public ushort RadiusDeciUnits { get; set; }
}

public sealed class WorldLinkSnapshot
{
    public uint LinkRuntimeId { get; set; }
    public string LinkDefId { get; set; } = string.Empty;
    public uint OwnerEntityId { get; set; }
    public uint TargetEntityId { get; set; }
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort RemainingTicks { get; set; }
}

public sealed class WorldObjectSnapshot
{
    public uint ObjectId { get; set; }
    public string ObjectDefId { get; set; } = string.Empty;
    public string Archetype { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort Health { get; set; }
    public ushort MaxHealth { get; set; }
    public byte ObjectiveState { get; set; }
}

public sealed class WorldHazardSnapshot
{
    public uint HazardRuntimeId { get; set; }
    public string HazardId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort RemainingTicks { get; set; }
    public byte ObjectiveState { get; set; }
}

public sealed class WorldNpcSnapshot
{
    public uint NpcRuntimeId { get; set; }
    public string NpcId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public short QuantizedX { get; set; }
    public short QuantizedY { get; set; }
    public ushort InteractRadiusDeciUnits { get; set; }
    public byte ObjectiveState { get; set; }
}

public sealed class WorldObjectiveSnapshot
{
    public string ObjectiveId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public ushort Current { get; set; }
    public ushort Required { get; set; }
    public byte State { get; set; }
}
