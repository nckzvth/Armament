using System;

namespace Armament.SharedSim.Protocol;

public static class Quantization
{
    public const float PositionScale = 100.0f;
    public const float InputScale = 1000.0f;

    public static short QuantizePosition(float value)
    {
        var scaled = value * PositionScale;
        var clamped = Math.Clamp(scaled, short.MinValue, short.MaxValue);
        return (short)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }

    public static float DequantizePosition(short value) => value / PositionScale;

    public static short QuantizeInput(float value)
    {
        var clamped = Math.Clamp(value, -1f, 1f) * InputScale;
        return (short)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }

    public static float DequantizeInput(short value) => value / InputScale;
}
