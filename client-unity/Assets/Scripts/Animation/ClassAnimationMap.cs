#nullable enable
using System;
using UnityEngine;

namespace Armament.Client.Animation
{

[CreateAssetMenu(menuName = "Armament/Animation/Class Animation Map", fileName = "class.animationmap")]
public sealed class ClassAnimationMap : ScriptableObject
{
    public string idleClipId = string.Empty;
    public string moveClipId = string.Empty;
    public string blockLoopClipId = string.Empty;
    public string fastAttackClipId = string.Empty;
    public string heavyAttackClipId = string.Empty;
    public string stunClipId = string.Empty;
    public string hitReactClipId = string.Empty;
    public string deathClipId = string.Empty;
    public string lootClipId = string.Empty;
    public string interactClipId = string.Empty;

    [Tooltip("Optional chained sequence for LMB hold. If empty, fastAttackClipId is used.")]
    public string[] fastAttackChainClipIds = Array.Empty<string>();

    [Tooltip("Optional chained sequence for RMB hold. If empty, heavyAttackClipId is used.")]
    public string[] heavyAttackChainClipIds = Array.Empty<string>();

    [Tooltip("Mapped to cast slots E,R,Q,T,1,2,3,4 (indices 0..7)")]
    public string[] castClipIds = Array.Empty<string>();

    public string GetCastClipForSlotCode(byte slotCode)
    {
        var idx = slotCode switch
        {
            3 => 0,
            4 => 1,
            5 => 2,
            6 => 3,
            7 => 4,
            8 => 5,
            9 => 6,
            10 => 7,
            _ => -1
        };

        if (idx < 0 || castClipIds is null || idx >= castClipIds.Length)
        {
            return string.Empty;
        }

        return castClipIds[idx] ?? string.Empty;
    }

    public string GetFastAttackChainClip(int index)
    {
        if (fastAttackChainClipIds is null || fastAttackChainClipIds.Length == 0)
        {
            return fastAttackClipId;
        }

        if (index < 0)
        {
            index = 0;
        }

        var mapped = fastAttackChainClipIds[index % fastAttackChainClipIds.Length];
        return string.IsNullOrWhiteSpace(mapped) ? fastAttackClipId : mapped;
    }
}
}
