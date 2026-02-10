using System;
using System.Collections.Generic;

namespace Armament.SharedSim.Determinism;

public static class WorldHash
{
    public static uint Fnv1A32(IEnumerable<uint> values)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var value in values)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    public static uint HashPosition(float x, float y)
    {
        unchecked
        {
            var xi = BitConverter.SingleToInt32Bits(x);
            var yi = BitConverter.SingleToInt32Bits(y);
            return (uint)(xi * 397) ^ (uint)yi;
        }
    }
}
